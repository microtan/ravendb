﻿using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Raven.Abstractions;
using Raven.Abstractions.Connection;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Logging;
using Raven.Abstractions.Util;
using Raven.Client.Connection;
using Raven.Client.Document;
using Raven.Client.Extensions;
using Raven.Json.Linq;
using System.Collections.Generic;

namespace Raven.Client.Changes
{
    public abstract class RemoteChangesClientBase<TChangesApi, TConnectionState> : IDisposable, IObserver<string>, IConnectableChanges<TChangesApi>
                                where TConnectionState : class, IChangesConnectionState
                                where TChangesApi : class, IConnectableChanges
    {
        private static readonly ILog logger = LogManager.GetCurrentClassLogger();

        private Timer clientSideHeartbeatTimer;

        private readonly string url;
        private readonly OperationCredentials credentials;
        private readonly HttpJsonRequestFactory jsonRequestFactory;
        private readonly Convention conventions;
        private readonly IReplicationInformerBase replicationInformer;

        private readonly Action onDispose;                

        private IDisposable connection;
        private DateTime lastHeartbeat;

        private static int connectionCounter;
        private readonly string id;

        protected readonly AtomicDictionary<TConnectionState> Counters = new AtomicDictionary<TConnectionState>(StringComparer.OrdinalIgnoreCase);        

        public RemoteChangesClientBase(
            string url,
            string apiKey,
            ICredentials credentials,
            HttpJsonRequestFactory jsonRequestFactory,
            Convention conventions,
            IReplicationInformerBase replicationInformer,
            Action onDispose )
        {
            // Precondition
            TChangesApi api = this as TChangesApi;
            if (api == null)
                throw new InvalidCastException(string.Format("The derived class does not implements {0}. Make sure the {0} interface is implemented by this class.", typeof(TChangesApi).Name));

            ConnectionStatusChanged = LogOnConnectionStatusChanged;

            id = Interlocked.Increment(ref connectionCounter) + "/" + Base62Util.Base62Random();

            this.url = url;
            this.credentials = new OperationCredentials(apiKey, credentials);
            this.jsonRequestFactory = jsonRequestFactory;
            this.conventions = conventions;
            this.replicationInformer = replicationInformer;
            this.onDispose = onDispose;            

            this.Task = EstablishConnection()
                        .ObserveException()
                        .ContinueWith(task =>
                        {
                            task.AssertNotFailed();
                            return this as TChangesApi;
                        });
        }

        public bool Connected { get; private set; }
        public event EventHandler ConnectionStatusChanged;

        private void LogOnConnectionStatusChanged(object sender, EventArgs eventArgs)
        {
            logger.Info("Connection ({1}) status changed, new status: {0}", Connected, url);
        }


        public Task<TChangesApi> Task { get; private set; }

        public void WaitForAllPendingSubscriptions()
        {
            foreach (var kvp in Counters)
            {
                kvp.Value.Task.Wait();
            }
        }

        private async Task EstablishConnection()
        {
            if (disposed)
                return;

            if (clientSideHeartbeatTimer != null)
            {
                clientSideHeartbeatTimer.Dispose();
                clientSideHeartbeatTimer = null;
            }

            var requestParams = new CreateHttpJsonRequestParams(null, url + "/changes/events?id=" + id, "GET", credentials, conventions)
            {
                AvoidCachingRequest = true,
                DisableRequestCompression = true
            };

            logger.Info("Trying to connect to {0} with id {1}", requestParams.Url, id);
            bool retry = false;
            IObservable<string> serverEvents = null;
            try
            {
                serverEvents = await jsonRequestFactory.CreateHttpJsonRequest(requestParams)
                                                       .ServerPullAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.WarnException("Could not connect to server: " + url + " and id " + id, e);

                Connected = false;
                ConnectionStatusChanged(this, EventArgs.Empty);

                if (disposed)
                    throw;

                bool timeout;
                if (replicationInformer.IsServerDown(e, out timeout) == false)
                    throw;

                if (replicationInformer.IsHttpStatus(e, HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable))
                    throw;

                logger.Warn("Failed to connect to {0} with id {1}, will try again in 15 seconds", url, id);
                retry = true;
            }

            if (retry)
            {
                await Time.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                await EstablishConnection().ConfigureAwait(false);
                return;
            }
            if (disposed)
            {
                Connected = false;
                ConnectionStatusChanged(this, EventArgs.Empty);
                throw new ObjectDisposedException( this.GetType().Name );
            }

            Connected = true;
            ConnectionStatusChanged(this, EventArgs.Empty);
            connection = (IDisposable)serverEvents;
            serverEvents.Subscribe(this);

            clientSideHeartbeatTimer = new Timer(ClientSideHeartbeat, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

            await SubscribeOnServer();
        }

        private void ClientSideHeartbeat(object _)
        {
            TimeSpan elapsedTimeSinceHeartbeat = SystemTime.UtcNow - lastHeartbeat;
            if (elapsedTimeSinceHeartbeat.TotalSeconds < 45)
                return;
            OnError(new TimeoutException("Over 45 seconds have passed since we got a server heartbeat, even though we should get one every 10 seconds or so.\r\n" +
                                         "This connection is now presumed dead, and will attempt reconnection"));
        }

        private Task lastSendTask;

        protected Task Send(string command, string value)
        {
            lock (this)
            {
                logger.Info("Sending command {0} - {1} to {2} with id {3}", command, value, url, id);
                var sendTask = lastSendTask;
                if (sendTask != null)
                {
                    sendTask.ContinueWith(_ =>
                    {
                        Send(command, value);
                    });
                }

                try
                {
                    var sendUrl = url + "/changes/config?id=" + id + "&command=" + command;
                    if (string.IsNullOrEmpty(value) == false)
                        sendUrl += "&value=" + Uri.EscapeUriString(value);

                    var requestParams = new CreateHttpJsonRequestParams(null, sendUrl, "GET", credentials, conventions)
                    {
                        AvoidCachingRequest = true
                    };
                    var httpJsonRequest = jsonRequestFactory.CreateHttpJsonRequest(requestParams);
                    return lastSendTask =
                        httpJsonRequest.ExecuteRequestAsync()
                            .ObserveException()
                            .ContinueWith(task => lastSendTask = null);
                }
                catch (Exception e)
                {
                    return new CompletedTask(e).Task.ObserveException();
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            DisposeAsync().Wait();
        }

        private volatile bool disposed;

        public Task DisposeAsync()
        {
            if (disposed)
                return new CompletedTask();
            disposed = true;
            onDispose();

            if (clientSideHeartbeatTimer != null)
                clientSideHeartbeatTimer.Dispose();
            clientSideHeartbeatTimer = null;

            return Send("disconnect", null).
                ContinueWith(_ =>
                {
                    try
                    {
                        if (connection != null)
                            connection.Dispose();
                    }
                    catch (Exception e)
                    {
                        logger.ErrorException("Got error from server connection for " + url + " on id " + id, e);

                    }
                });
        }


        public virtual void OnError(Exception error)
        {
            logger.ErrorException("Got error from server connection for " + url + " on id " + id, error);

            RenewConnection();
        }

        private void RenewConnection()
        {
            Time.Delay(TimeSpan.FromSeconds(15))
                .ContinueWith(_ => EstablishConnection())
                .Unwrap()
                .ObserveException()
                .ContinueWith(task =>
                {
                    if (task.IsFaulted == false)
                        return;

                    foreach (var keyValuePair in Counters)
                    {
                        keyValuePair.Value.Error(task.Exception);
                    }
                    Counters.Clear();
                });
        }

        public void OnNext(string dataFromConnection)
        {
            lastHeartbeat = SystemTime.UtcNow;

            var ravenJObject = RavenJObject.Parse(dataFromConnection);
            var value = ravenJObject.Value<RavenJObject>("Value");
            var type = ravenJObject.Value<string>("Type");

            logger.Debug("Got notification from {0} id {1} of type {2}", url, id, dataFromConnection);

            switch (type)
            {
                case "Disconnect":
                    if (connection != null)
                        connection.Dispose();
                    RenewConnection();
                    break;
                case "Initialized":
                case "Heartbeat":
                    break;
                default:
                    NotifySubscribers(type, value, Counters);
                    break;
            }
        }


        protected abstract Task SubscribeOnServer();
        protected abstract void NotifySubscribers(string type, RavenJObject value, IEnumerable<KeyValuePair<string, TConnectionState>> connections);


        public virtual void OnCompleted()
        { }
    }
}
