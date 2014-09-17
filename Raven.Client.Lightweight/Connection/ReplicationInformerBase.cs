//-----------------------------------------------------------------------
// <copyright file="ReplicationInformer.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Raven.Abstractions;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Logging;
using Raven.Abstractions.Replication;
using Raven.Abstractions.Util;
using Raven.Client.Document;
using Raven.Client.Extensions;
using Raven.Imports.Newtonsoft.Json.Linq;

namespace Raven.Client.Connection
{
    using Raven.Abstractions.Connection;

	/// <summary>
	/// Replication and failover management on the client side
	/// </summary>
	public abstract class ReplicationInformerBase<TClient> : IReplicationInformerBase<TClient>
	{
		protected readonly ILog log = LogManager.GetCurrentClassLogger();

        protected bool firstTime = true;
		protected readonly Convention conventions;
	    private readonly HttpJsonRequestFactory requestFactory;
	    protected DateTime lastReplicationUpdate = DateTime.MinValue;
        protected readonly object replicationLock = new object();
		private static readonly List<OperationMetadata> Empty = new List<OperationMetadata>();
		protected static int readStripingBase;

		/// <summary>
		/// Notify when the failover status changed
		/// </summary>
		public event EventHandler<FailoverStatusChangedEventArgs> FailoverStatusChanged = delegate { };

        public int DelayTimeInMiliSec { get;  set; }

        public List<OperationMetadata> ReplicationDestinations { get; protected set; }

		/// <summary>
		/// Gets the replication destinations.
		/// </summary>
		/// <value>The replication destinations.</value>
		public List<OperationMetadata> ReplicationDestinationsUrls
		{
			get
			{
				if (conventions.FailoverBehavior == FailoverBehavior.FailImmediately)
					return Empty;

				return ReplicationDestinations
					.Select(operationMetadata => new OperationMetadata(operationMetadata))
					.ToList();
			}
		}

		///<summary>
		/// Create a new instance of this class
		///</summary>
		protected ReplicationInformerBase(Convention conventions, HttpJsonRequestFactory requestFactory, int delayTime=1000)
		{
			this.conventions = conventions;
		    this.requestFactory = requestFactory;
		    ReplicationDestinations = new List<OperationMetadata>();
		    DelayTimeInMiliSec = delayTime;
		}

        /// <summary>
        /// Refreshes the replication information.
        /// Expert use only.
        /// </summary>
        public abstract void RefreshReplicationInformation(TClient client);

		public abstract void ClearReplicationInformationLocalCache(TClient client);

		protected abstract void UpdateReplicationInformationFromDocument(JsonDocument document);

		protected readonly System.Collections.Concurrent.ConcurrentDictionary<string, FailureCounter> failureCounts = new System.Collections.Concurrent.ConcurrentDictionary<string, FailureCounter>();

		protected Task refreshReplicationInformationTask;

	    public class FailureCounter
		{
			public long Value;
			public DateTime LastCheck;
			public bool ForceCheck;

	        public Task CheckDestination = new CompletedTask();

	        public long Increment()
	        {
                ForceCheck = false;
	            LastCheck = SystemTime.UtcNow;
                return Interlocked.Increment(ref Value);
	        }

	        public long Reset()
	        {
                var oldVal = Interlocked.Exchange(ref Value, 0);
                LastCheck = SystemTime.UtcNow;
                ForceCheck = false;
	            return oldVal;
	        }
		}


		/// <summary>
		/// Get the current failure count for the url
		/// </summary>
		public long GetFailureCount(string operationUrl)
		{
			return GetHolder(operationUrl).Value;
		}

		/// <summary>
		/// Get failure last check time for the url
		/// </summary>
		public DateTime GetFailureLastCheck(string operationUrl)
		{
			return GetHolder(operationUrl).LastCheck;
		}

	    /// <summary>
	    /// Should execute the operation using the specified operation URL
	    /// </summary>
	    public virtual bool ShouldExecuteUsing(OperationMetadata operationMetadata, OperationMetadata primaryOperation, int currentRequest, string method, bool primary, Exception error)
	    {
	        if (primary == false)
	            AssertValidOperation(method, error);

	        var failureCounter = GetHolder(operationMetadata.Url);
	        if (failureCounter.Value == 0)
	            return true;

	        if (failureCounter.ForceCheck)
	            return true;

	        var currentTask = failureCounter.CheckDestination;
            if (currentTask.Status != TaskStatus.Running && DelayTimeInMiliSec > 0)
	        {
	            var checkDestination = new Task(async delegate
	            {
	                for (int i = 0; i < 3; i++)
	                {
	                    try
	                    {
	                        var r = await TryOperationAsync<object>(async metadata =>
	                        {
	                            var requestParams = new CreateHttpJsonRequestParams(null, GetServerCheckUrl(metadata.Url), "GET", metadata.Credentials, conventions);
	                            await requestFactory.CreateHttpJsonRequest(requestParams).ReadResponseJsonAsync().ConfigureAwait(false);
	                            return null;
	                        }, operationMetadata, primaryOperation, avoidThrowing: true).ConfigureAwait(false);
	                        if (r.Success)
	                        {
	                            ResetFailureCount(operationMetadata.Url);
	                            return;
	                        }
	                    }
	                    catch (ObjectDisposedException)
	                    {
	                        return; // disposed, nothing to do here
	                    }
                        await Task.Delay(DelayTimeInMiliSec).ConfigureAwait(false);
	                }
	            });

	            var old = Interlocked.CompareExchange(ref failureCounter.CheckDestination, checkDestination, currentTask);
	            if (old == currentTask)
	            {
	                  checkDestination.Start(TaskScheduler.Default);
	            }
	        }

	        return false;
	    }

	    protected abstract string GetServerCheckUrl(string baseUrl);

	    protected void AssertValidOperation(string method, Exception e)
		{
			switch (conventions.FailoverBehaviorWithoutFlags)
			{
				case FailoverBehavior.AllowReadsFromSecondaries:
					if (method == "GET")
						return;
					break;
				case FailoverBehavior.AllowReadsFromSecondariesAndWritesToSecondaries:
					return;
				case FailoverBehavior.FailImmediately:
					var allowReadFromAllServers = conventions.FailoverBehavior.HasFlag(FailoverBehavior.ReadFromAllServers);
					if (allowReadFromAllServers && method == "GET")
						return;
					break;
			}
			throw new InvalidOperationException("Could not replicate " + method +
												" operation to secondary node, failover behavior is: " +
												conventions.FailoverBehavior, e);
		}

		protected FailureCounter GetHolder(string operationUrl)
		{
			return failureCounts.GetOrAdd(operationUrl, new FailureCounter());
		}

		/// <summary>
		/// Determines whether this is the first failure on the specified operation URL.
		/// </summary>
		/// <param name="operationUrl">The operation URL.</param>
		public bool IsFirstFailure(string operationUrl)
		{
			FailureCounter value = GetHolder(operationUrl);
			return value.Value == 0;
		}

		/// <summary>
		/// Increments the failure count for the specified operation URL
		/// </summary>
		/// <param name="operationUrl">The operation URL.</param>
		public void IncrementFailureCount(string operationUrl)
		{
			var value = GetHolder(operationUrl);
			
			if (value.Increment() == 1)// first failure
			{
				FailoverStatusChanged(this, new FailoverStatusChangedEventArgs
				{
					Url = operationUrl,
					Failing = true
				});
			}
		}

        protected static bool IsInvalidDestinationsDocument(JsonDocument document)
		{
			return document == null ||
				   document.DataAsJson.ContainsKey("Destinations") == false ||
				   document.DataAsJson["Destinations"] == null ||
				   document.DataAsJson["Destinations"].Type == JTokenType.Null;
		}

		/// <summary>
		/// Resets the failure count for the specified URL
		/// </summary>
		/// <param name="operationUrl">The operation URL.</param>
		public virtual void ResetFailureCount(string operationUrl)
		{
			var value = GetHolder(operationUrl);
			if (value.Reset() != 0)
			{
				FailoverStatusChanged(this,
					new FailoverStatusChangedEventArgs
					{
						Url = operationUrl,
						Failing = false
					});
			}
		}

		public virtual int GetReadStripingBase()
		{
			return Interlocked.Increment(ref readStripingBase);
		}
		
        public async Task<T> ExecuteWithReplicationAsync<T>(string method, string primaryUrl, OperationCredentials primaryCredentials, int currentRequest, int currentReadStripingBase, Func<OperationMetadata, Task<T>> operation)
        {
            var localReplicationDestinations = ReplicationDestinationsUrls; // thread safe copy
            var primaryOperation = new OperationMetadata(primaryUrl, primaryCredentials);

            var shouldReadFromAllServers = conventions.FailoverBehavior.HasFlag(FailoverBehavior.ReadFromAllServers);
            var operationResult = new AsyncOperationResult<T>();

            if (shouldReadFromAllServers && method == "GET")
            {
                var replicationIndex = currentReadStripingBase%(localReplicationDestinations.Count + 1);
                // if replicationIndex == destinations count, then we want to use the master
                // if replicationIndex < 0, then we were explicitly instructed to use the master
                if (replicationIndex < localReplicationDestinations.Count && replicationIndex >= 0)
                {
                    // if it is failing, ignore that, and move to the master or any of the replicas
                    if (ShouldExecuteUsing(localReplicationDestinations[replicationIndex], primaryOperation, currentRequest, method, false, error: null))
                    {
                        operationResult = await TryOperationAsync(operation, localReplicationDestinations[replicationIndex], primaryOperation, true).ConfigureAwait(false);
                        if (operationResult.Success)
                            return operationResult.Result;
                    }
                }
            }

            if (ShouldExecuteUsing(primaryOperation,primaryOperation, currentRequest, method, true, error: null))
            {
                operationResult = await TryOperationAsync(operation, primaryOperation, null, !operationResult.WasTimeout && localReplicationDestinations.Count > 0)
                    .ConfigureAwait(false);

                if (operationResult.Success)
                    return operationResult.Result;

                IncrementFailureCount(primaryOperation.Url);
                if (!operationResult.WasTimeout && IsFirstFailure(primaryOperation.Url))
                {
                    operationResult = await TryOperationAsync(operation, primaryOperation, null, localReplicationDestinations.Count > 0).ConfigureAwait(false);

                    if (operationResult.Success)
                        return operationResult.Result;
                    IncrementFailureCount(primaryOperation.Url);
                }
            }

            for (var i = 0; i < localReplicationDestinations.Count; i++)
            {
                var replicationDestination = localReplicationDestinations[i];
                if (ShouldExecuteUsing(replicationDestination, primaryOperation, currentRequest, method, false, operationResult.Error) == false)
                    continue;

                var hasMoreReplicationDestinations = localReplicationDestinations.Count > i + 1;
                operationResult = await TryOperationAsync(operation, replicationDestination, primaryOperation, !operationResult.WasTimeout && hasMoreReplicationDestinations).ConfigureAwait(false);

                if (operationResult.Success)
                    return operationResult.Result;

                IncrementFailureCount(replicationDestination.Url);
                if (!operationResult.WasTimeout && IsFirstFailure(replicationDestination.Url))
                {
                    operationResult = await TryOperationAsync(operation, replicationDestination, primaryOperation, hasMoreReplicationDestinations).ConfigureAwait(false);

                    // tuple = await TryOperationAsync(operation, replicationDestination, primaryOperation, localReplicationDestinations.Count > i + 1).ConfigureAwait(false);
                    if (operationResult.Success)
                        return operationResult.Result;
                    IncrementFailureCount(replicationDestination.Url);
                }
            }

            // this should not be thrown, but since I know the value of should...
            throw new InvalidOperationException(@"Attempted to connect to master and all replicas have failed, giving up.
There is a high probability of a network problem preventing access to all the replicas.
Failed to get in touch with any of the " + (1 + localReplicationDestinations.Count) + " Raven instances.");
        }

	    protected class AsyncOperationResult<T>
	    {
	        public T Result;
	        public bool WasTimeout;
	        public bool Success;
	        public Exception Error;
	    }

        protected async virtual Task<AsyncOperationResult<T>> TryOperationAsync<T>(Func<OperationMetadata, Task<T>> operation, OperationMetadata operationMetadata,
            OperationMetadata primaryOperationMetadata, bool avoidThrowing)
        {
            var tryWithPrimaryCredentials = IsFirstFailure(operationMetadata.Url) && primaryOperationMetadata != null;
            bool shouldTryAgain = false;

            try
            {

                var result = await operation(tryWithPrimaryCredentials ? new OperationMetadata(operationMetadata.Url, primaryOperationMetadata.Credentials) : operationMetadata).ConfigureAwait(false);
                ResetFailureCount(operationMetadata.Url);
                return new AsyncOperationResult<T>
                {
                    Result = result,
                    Success = true
                };
            }
            catch (Exception e)
            {
                var ae = e as AggregateException;
                ErrorResponseException errorResponseException;
                if (ae != null)
                {
                    errorResponseException = ae.ExtractSingleInnerException() as ErrorResponseException;
                }
                else
                {
                    errorResponseException = e as ErrorResponseException;
                }
                if (tryWithPrimaryCredentials && operationMetadata.Credentials.HasCredentials() && errorResponseException != null)
                {
                    IncrementFailureCount(operationMetadata.Url);

                    if (errorResponseException.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        shouldTryAgain = true;
                    }
                }

                if (shouldTryAgain == false)
                {
                    if (avoidThrowing == false)
                        throw;

                    bool wasTimeout;
                    if (IsServerDown(e, out wasTimeout))
                    {
                        return new AsyncOperationResult<T>
                        {
                            Success = false,
                            WasTimeout = wasTimeout,
                            Error = e
                        };
                    }
                    throw;
                }
            }
            return await TryOperationAsync(operation, operationMetadata, primaryOperationMetadata, avoidThrowing);
        }

		public bool IsHttpStatus(Exception e, params HttpStatusCode[] httpStatusCode)
		{
			var aggregateException = e as AggregateException;
			if (aggregateException != null)
			{
				e = aggregateException.ExtractSingleInnerException();
			}

			var ere = e as ErrorResponseException ?? e.InnerException as ErrorResponseException;
		    if (ere != null)
		    {
			    return httpStatusCode.Contains(ere.StatusCode);
		    }
			var webException = (e as WebException) ?? (e.InnerException as WebException);
			if (webException != null)
			{
				var httpWebResponse = webException.Response as HttpWebResponse;
				if (httpWebResponse != null && httpStatusCode.Contains(httpWebResponse.StatusCode))
					return true;
			}

			return false;
		}

		public virtual bool IsServerDown(Exception e, out bool timeout)
		{
			timeout = false;

			var aggregateException = e as AggregateException;
			if (aggregateException != null)
			{
				e = aggregateException.ExtractSingleInnerException();
			}

		    var ere = e as ErrorResponseException ?? e.InnerException as ErrorResponseException;
		    if (ere != null)
		    {
		        if (IsServerDown(ere.StatusCode, out timeout))
		            return true;
		    }

			var webException = (e as WebException) ?? (e.InnerException as WebException);
			if (webException != null)
			{
				switch (webException.Status)
				{
					case WebExceptionStatus.Timeout:
						timeout = true;
						return true;
					case WebExceptionStatus.NameResolutionFailure:
					case WebExceptionStatus.ReceiveFailure:
					case WebExceptionStatus.PipelineFailure:
					case WebExceptionStatus.ConnectionClosed:
					case WebExceptionStatus.ConnectFailure:
					case WebExceptionStatus.SendFailure:
						return true;
				}

				var httpWebResponse = webException.Response as HttpWebResponse;
				if (httpWebResponse != null)
				{
					if (IsServerDown(httpWebResponse.StatusCode, out timeout))
                        return true;
				}
			}
			return
 e.InnerException is SocketException ||
 e.InnerException is IOException;
		}

        private static bool IsServerDown(HttpStatusCode httpStatusCode, out bool timeout)
	    {
            timeout = false;
            switch (httpStatusCode)
	        {
	            case HttpStatusCode.RequestTimeout:
	            case HttpStatusCode.GatewayTimeout:
	                timeout = true;
	                return true;
	            case HttpStatusCode.BadGateway:
	            case HttpStatusCode.ServiceUnavailable:
	                return true;
	        }
	        return false;
	    }

	    public virtual void Dispose()
		{
			var replicationInformationTaskCopy = refreshReplicationInformationTask;
			if (replicationInformationTaskCopy != null)
				replicationInformationTaskCopy.Wait();
		}

		public void ForceCheck(string primaryUrl, bool shouldForceCheck)
		{
			var failureCounter = this.GetHolder(primaryUrl);
			failureCounter.ForceCheck = shouldForceCheck;
		}
	}

    /// <summary>
	/// The event arguments for when the failover status changed
	/// </summary>
	public class FailoverStatusChangedEventArgs : EventArgs
	{
		/// <summary>
		/// Whatever that url is now failing
		/// </summary>
		public bool Failing { get; set; }
		/// <summary>
		/// The url whose failover status changed
		/// </summary>
		public string Url { get; set; }
	}
}
