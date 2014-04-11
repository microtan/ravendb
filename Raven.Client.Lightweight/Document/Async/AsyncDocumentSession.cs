//-----------------------------------------------------------------------
// <copyright file="AsyncDocumentSession.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Util;
using Raven.Client.Connection;
using Raven.Client.Connection.Async;
using Raven.Client.Document.SessionOperations;
using Raven.Client.Extensions;
using Raven.Client.Linq;
using Raven.Client.Indexes;
using Raven.Client.Util;
using Raven.Json.Linq;
using Raven.Client.Document.Batches;
using Raven.Client.WinRT.MissingFromWinRT;
using System.Diagnostics;

namespace Raven.Client.Document.Async
{
	/// <summary>
	/// Implementation for async document session 
	/// </summary>
	public class AsyncDocumentSession : InMemoryDocumentSessionOperations, IAsyncDocumentSessionImpl, IAsyncAdvancedSessionOperations, IDocumentQueryGenerator
	{
		private readonly AsyncDocumentKeyGeneration asyncDocumentKeyGeneration;

		/// <summary>
		/// Initializes a new instance of the <see cref="AsyncDocumentSession"/> class.
		/// </summary>
		public AsyncDocumentSession(string dbName, DocumentStore documentStore,
									IAsyncDatabaseCommands asyncDatabaseCommands,
									DocumentSessionListeners listeners,
									Guid id)
			: base(dbName, documentStore, listeners, id)
		{
			AsyncDatabaseCommands = asyncDatabaseCommands;
			GenerateDocumentKeysOnStore = false;
			asyncDocumentKeyGeneration = new AsyncDocumentKeyGeneration(this, entitiesAndMetadata.TryGetValue, (key, entity, metadata) => key);
		}

		/// <summary>
		/// Gets the async database commands.
		/// </summary>
		/// <value>The async database commands.</value>
		public IAsyncDatabaseCommands AsyncDatabaseCommands { get; private set; }
        /// <summary>
        /// Access the lazy operations
        /// </summary>
        public IAsyncLazySessionOperations Lazily
        {
            get { return this; }
        }

        /// <summary>
        /// Access the eager operations
        /// </summary>
        public IAsyncEagerSessionOperations Eagerly
        {
            get { return this; }
        }
        /// <summary>
        /// Begin a load while including the specified path 
        /// </summary>
        /// <param name="path">The path.</param>

        IAsyncLazyLoaderWithInclude<object> IAsyncLazySessionOperations.Include(string path)
	    {
            return new AsyncLazyMultiLoaderWithInclude<object>(this).Include(path);
	    }
       

	    /// <summary>
        /// Begin a load while including the specified path 
        /// </summary>
        /// <param name="path">The path.</param>
        IAsyncLazyLoaderWithInclude<T> IAsyncLazySessionOperations.Include<T>(Expression<Func<T, object>> path)
        {
            return  new AsyncLazyMultiLoaderWithInclude<T>(this).Include(path);
        }

        /// <summary>
        /// Loads the specified ids.
        /// </summary>
        /// <param name="ids">The ids.</param>
        Lazy<Task<T[]>> IAsyncLazySessionOperations.LoadAsync<T>(params string[] ids)
        {
            return Lazily.LoadAsync<T>(ids, null);
        }

        /// <summary>
        /// Loads the specified ids.
        /// </summary>
        Lazy<Task<T[]>> IAsyncLazySessionOperations.LoadAsync<T>(IEnumerable<string> ids)
        {
            return Lazily.LoadAsync<T>(ids, null);
        }

        /// <summary>
        /// Loads the specified id.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id">The id.</param>
        /// <returns></returns>
        Lazy<Task<T>> IAsyncLazySessionOperations.LoadAsync<T>(string id)
        {
            return Lazily.LoadAsync(id, (Action<T>)null);
        }

        /// <summary>
        /// Loads the specified ids and a function to call when it is evaluated
        /// </summary>
        public Lazy<Task<T[]>> LoadAsync<T>(IEnumerable<string> ids, Action<T[]> onEval)
        {
            return LazyLoadInternal(ids.ToArray(), new KeyValuePair<string, Type>[0], onEval);
        }

	   

	    /// <summary>
        /// Loads the specified id and a function to call when it is evaluated
        /// </summary>
        public Lazy<Task<T>> LoadAsync<T>(string id, Action<T> onEval)
        {
            if (IsLoaded(id))
                 return new Lazy<Task<T>>(() => LoadAsync<T>(id));
               
            var lazyLoadOperation = new LazyLoadOperation<T>(id, new LoadOperation(this, AsyncDatabaseCommands.DisableAllCaching, id), handleInternalMetadata: HandleInternalMetadata);
            return AddLazyOperation(lazyLoadOperation, onEval);
        }

        /// <summary>
        /// Loads the specified entities with the specified id after applying
        /// conventions on the provided id to get the real document id.
        /// </summary>
        /// <remarks>
        /// This method allows you to call:
        /// Load{Post}(1)
        /// And that call will internally be translated to 
        /// Load{Post}("posts/1");
        /// 
        /// Or whatever your conventions specify.
        /// </remarks>
        Lazy<Task<T>> IAsyncLazySessionOperations.LoadAsync<T>(ValueType id, Action<T> onEval)
        {
            var documentKey = Conventions.FindFullDocumentKeyFromNonStringIdentifier(id, typeof(T), false);
            return Lazily.LoadAsync(documentKey, onEval);
        }

        Lazy<Task<T[]>> IAsyncLazySessionOperations.LoadAsync<T>(params ValueType[] ids)
        {
            var documentKeys = ids.Select(id => Conventions.FindFullDocumentKeyFromNonStringIdentifier(id, typeof(T), false));
            return Lazily.LoadAsync<T>(documentKeys, null);
        }

        Lazy<Task<T[]>> IAsyncLazySessionOperations.LoadAsync<T>(IEnumerable<ValueType> ids)
        {
            var documentKeys = ids.Select(id => Conventions.FindFullDocumentKeyFromNonStringIdentifier(id, typeof(T), false));
            return Lazily.LoadAsync<T>(documentKeys, null);
        }

        Lazy<Task<T[]>> IAsyncLazySessionOperations.LoadAsync<T>(IEnumerable<ValueType> ids, Action<T[]> onEval)
        {
            var documentKeys = ids.Select(id => Conventions.FindFullDocumentKeyFromNonStringIdentifier(id, typeof(T), false));
            return LazyLoadInternal(documentKeys.ToArray(), new KeyValuePair<string, Type>[0], onEval);
        }

        Lazy<Task<TResult>> IAsyncLazySessionOperations.LoadAsync<TTransformer, TResult>(string id)
        {
            var transformer = new TTransformer().TransformerName;
            var ids = new[] { id };
            var lazyLoadOperation = new LazyTransformerLoadOperation<TResult>(ids, transformer,
                                                                          new LoadTransformerOperation(this, transformer, ids),
                                                                          singleResult: true);
            return AddLazyOperation<TResult>(lazyLoadOperation, null);
        }

        Lazy<Task<TResult[]>> IAsyncLazySessionOperations.LoadAsync<TTransformer, TResult>(string[] ids)
        {
            var transformer = new TTransformer().TransformerName;
            var lazyLoadOperation = new LazyTransformerLoadOperation<TResult>(ids, transformer,
                                                                          new LoadTransformerOperation(this, transformer, ids),
                                                                          singleResult: false);
            return AddLazyOperation<TResult[]>(lazyLoadOperation, null);
        }
      
       

	    
        public Lazy<Task<TResult[]>> MoreLikeThisAsync<TResult>(MoreLikeThisQuery query)
        {
            var multiLoadOperation = new MultiLoadOperation(this, AsyncDatabaseCommands.DisableAllCaching, null, null);
            var lazyOp = new LazyMoreLikeThisOperation<TResult>(multiLoadOperation, query);
            return AddLazyOperation<TResult[]>(lazyOp, null);
        }

        Lazy<Task<T[]>> IAsyncLazySessionOperations.LoadStartingWithAsync<T>(string keyPrefix, string matches, int start, int pageSize, string exclude, RavenPagingInformation pagingInformation)
        {
            var operation = new LazyStartsWithOperation<T>(keyPrefix, matches, exclude, start, pageSize, this, pagingInformation);

            return AddLazyOperation<T[]>(operation, null);
        }
	    /// <summary>
        /// Loads the specified entities with the specified id after applying
        /// conventions on the provided id to get the real document id.
        /// </summary>
        /// <remarks>
        /// This method allows you to call:
        /// Load{Post}(1)
        /// And that call will internally be translated to 
        /// Load{Post}("posts/1");
        /// 
        /// Or whatever your conventions specify.
        /// </remarks>
         Lazy<Task<T>> IAsyncLazySessionOperations.LoadAsync<T>(ValueType id)
        {
            return Lazily.LoadAsync(id, (Action<T>)null);
        }

         internal  Lazy<Task<T>> AddLazyOperation<T>(ILazyOperation operation, Action<T> onEval)
         {
             pendingLazyOperations.Add(operation);
             var lazyValue = new Lazy<Task<T>>(() => ExecuteAllPendingLazyOperationsAsync()  
                 .ContinueWith(t =>
             {
                   if(t.Exception != null)
                        throw new InvalidOperationException("Could not perform add lazy operation", t.Exception);

                    return (T)operation.Result;
             }));

             if (onEval != null)
                 onEvaluateLazy[operation] = theResult => onEval((T)theResult);

             return  lazyValue;
         }

         internal Lazy<Task<int>> AddLazyCountOperation(ILazyOperation operation)
         {
             pendingLazyOperations.Add(operation);
             var lazyValue = new Lazy<Task<int>>(() => ExecuteAllPendingLazyOperationsAsync()
                 .ContinueWith(t =>
                 {
                     if(t.Exception != null)
                         throw new InvalidOperationException("Could not perform lazy count", t.Exception);
                     return operation.QueryResult.TotalResults;
                 }));

             return lazyValue;
         }
         public async Task<ResponseTimeInformation> ExecuteAllPendingLazyOperationsAsync()
         {
             if (pendingLazyOperations.Count == 0)
                 return new ResponseTimeInformation();

             try
             {
                 var sw = Stopwatch.StartNew();

                 IncrementRequestCount();

                 var responseTimeDuration = new ResponseTimeInformation();

                 while (await ExecuteLazyOperationsSingleStep(responseTimeDuration))
                 {
                     await Task.Delay(100);
                 }

                 responseTimeDuration.ComputeServerTotal();


                 foreach (var pendingLazyOperation in pendingLazyOperations)
                 {
                     Action<object> value;
                     if (onEvaluateLazy.TryGetValue(pendingLazyOperation, out value))
                         value(pendingLazyOperation.Result);
                 }
                 responseTimeDuration.TotalClientDuration = sw.Elapsed;
                 return responseTimeDuration;
             }
             finally
             {
                 pendingLazyOperations.Clear();
             }
         }

         private async Task<bool> ExecuteLazyOperationsSingleStep(ResponseTimeInformation responseTimeInformation)
         {
             var disposables = pendingLazyOperations.Select(x => x.EnterContext()).Where(x => x != null).ToList();
             try
             {
                 var requests = pendingLazyOperations.Select(x => x.CreateRequest()).ToArray();
                 var responses = await AsyncDatabaseCommands.MultiGetAsync(requests);

                 for (int i = 0; i < pendingLazyOperations.Count; i++)
                 {
                     long totalTime;
                     long.TryParse(responses[i].Headers["Temp-Request-Time"], out totalTime);

                     responseTimeInformation.DurationBreakdown.Add(new ResponseTimeItem
                     {
                         Url = requests[i].UrlAndQuery,
                         Duration = TimeSpan.FromMilliseconds(totalTime)
                     });
                     if (responses[i].RequestHasErrors())
                     {
                         throw new InvalidOperationException("Got an error from server, status code: " + responses[i].Status +
                                                             Environment.NewLine + responses[i].Result);
                     }
                     pendingLazyOperations[i].HandleResponse(responses[i]);
                     if (pendingLazyOperations[i].RequiresRetry)
                     {
                         return true;
                     }
                 }
                 return false;
             }
             finally
             {
                 foreach (var disposable in disposables)
                 {
                     disposable.Dispose();
                 }
             }
         }
         /// <summary>
         /// Register to lazily load documents and include
         /// </summary>
         public Lazy<Task<T[]>> LazyLoadInternal<T>(string[] ids, KeyValuePair<string, Type>[] includes, Action<T[]> onEval)
         {
             var multiLoadOperation = new MultiLoadOperation(this, AsyncDatabaseCommands.DisableAllCaching, ids, includes);
             var lazyOp = new LazyMultiLoadOperation<T>(multiLoadOperation, ids, includes);
             return AddLazyOperation(lazyOp, onEval);
         }



		/// <summary>
		/// Load documents with the specified key prefix
		/// </summary>
		public Task<IEnumerable<T>> LoadStartingWithAsync<T>(string keyPrefix, string matches = null, int start = 0, int pageSize = 25, string exclude = null, RavenPagingInformation pagingInformation = null)
		{
			return AsyncDatabaseCommands.StartsWithAsync(keyPrefix, matches, start, pageSize, exclude: exclude, pagingInformation: pagingInformation)
										.ContinueWith(task => (IEnumerable<T>)task.Result.Select(TrackEntity<T>).ToList());
		}

		public Task<IEnumerable<TResult>> LoadStartingWithAsync<TTransformer, TResult>(string keyPrefix, string matches = null, int start = 0, int pageSize = 25,
		                                                    string exclude = null, RavenPagingInformation pagingInformation = null,
		                                                    Action<ILoadConfiguration> configure = null) where TTransformer : AbstractTransformerCreationTask, new()
		{
			var transformer = new TTransformer().TransformerName;

			var configuration = new RavenLoadConfiguration();
			if (configure != null)
			{
				configure(configuration);
			}

			return AsyncDatabaseCommands.StartsWithAsync(keyPrefix, matches, start, pageSize, exclude: exclude,
			                                             pagingInformation: pagingInformation, transformer: transformer,
			                                             queryInputs: configuration.QueryInputs)
			                            .ContinueWith(
				                            task => (IEnumerable<TResult>) task.Result.Select(TrackEntity<TResult>).ToList());
		}

		public Task<IAsyncEnumerator<StreamResult<T>>> StreamAsync<T>(IAsyncDocumentQuery<T> query)
		{
			return StreamAsync(query, new Reference<QueryHeaderInformation>());
		}

		public Task<IAsyncEnumerator<StreamResult<T>>> StreamAsync<T>(IQueryable<T> query)
		{
			return StreamAsync(query, new Reference<QueryHeaderInformation>());
		}


		public async Task<IAsyncEnumerator<StreamResult<T>>> StreamAsync<T>(IQueryable<T> query, Reference<QueryHeaderInformation> queryHeaderInformation)
		{
			var queryInspector = (IRavenQueryProvider)query.Provider;
			var indexQuery = queryInspector.ToAsyncDocumentQuery<T>(query.Expression);
            return await StreamAsync(indexQuery, queryHeaderInformation).ConfigureAwait(false);
		}

		public async Task<IAsyncEnumerator<StreamResult<T>>> StreamAsync<T>(IAsyncDocumentQuery<T> query, Reference<QueryHeaderInformation> queryHeaderInformation)
		{
			var ravenQueryInspector = ((IRavenQueryInspector)query);
			var indexQuery = ravenQueryInspector.GetIndexQuery(true);

            if (indexQuery.WaitForNonStaleResults || indexQuery.WaitForNonStaleResultsAsOfNow)
                throw new NotSupportedException(
                    "Since Stream() does not wait for indexing (by design), streaming query with WaitForNonStaleResults is not supported.");


            var enumerator = await AsyncDatabaseCommands.StreamQueryAsync(ravenQueryInspector.AsyncIndexQueried, indexQuery, queryHeaderInformation).ConfigureAwait(false);
			var queryOperation = ((AsyncDocumentQuery<T>)query).InitializeQueryOperation(null);
			queryOperation.DisableEntitiesTracking = true;

			return new QueryYieldStream<T>(this, enumerator, queryOperation);
		}

		public Task<IAsyncEnumerator<StreamResult<T>>> StreamAsync<T>(Etag fromEtag, int start = 0,
																	 int pageSize = Int32.MaxValue, RavenPagingInformation pagingInformation = null)
		{
			return StreamAsync<T>(fromEtag: fromEtag, startsWith: null, matches: null, start: start, pageSize: pageSize, pagingInformation: pagingInformation);
		}

		public Task<IAsyncEnumerator<StreamResult<T>>> StreamAsync<T>(string startsWith, string matches = null, int start = 0,
								   int pageSize = Int32.MaxValue, RavenPagingInformation pagingInformation = null)
		{
			return StreamAsync<T>(fromEtag: null, startsWith: startsWith, matches: matches, start: start, pageSize: pageSize, pagingInformation: pagingInformation);
		}

		private async Task<IAsyncEnumerator<StreamResult<T>>> StreamAsync<T>(Etag fromEtag, string startsWith, string matches, int start, int pageSize, RavenPagingInformation pagingInformation = null)
		{
			var enumerator = await AsyncDatabaseCommands.StreamDocsAsync(fromEtag, startsWith, matches, start, pageSize, pagingInformation: pagingInformation).ConfigureAwait(false);
			return new DocsYieldStream<T>(this, enumerator);
		}

		public abstract class YieldStream<T> : IAsyncEnumerator<StreamResult<T>>
		{
			protected readonly AsyncDocumentSession parent;
			protected readonly IAsyncEnumerator<RavenJObject> enumerator;

			protected YieldStream(AsyncDocumentSession parent, IAsyncEnumerator<RavenJObject> enumerator)
			{
				this.parent = parent;
				this.enumerator = enumerator;
			}

			public void Dispose()
			{
				enumerator.Dispose();
			}

			public async Task<bool> MoveNextAsync()
			{
                if (await enumerator.MoveNextAsync().ConfigureAwait(false) == false)
					return false;

				SetCurrent();

				return true;
			}

			protected abstract void SetCurrent();

			public StreamResult<T> Current { get; protected set; }
		}
		public class QueryYieldStream<T> : YieldStream<T>
		{
			private readonly QueryOperation queryOperation;

			public QueryYieldStream(AsyncDocumentSession parent, IAsyncEnumerator<RavenJObject> enumerator, QueryOperation queryOperation)
				: base(parent, enumerator)
			{
				this.queryOperation = queryOperation;
			}

			protected override void SetCurrent()
			{
				var meta = enumerator.Current.Value<RavenJObject>(Constants.Metadata);

				string key = null;
				Etag etag = null;
				if (meta != null)
				{
					key = meta.Value<string>(Constants.DocumentIdFieldName);
					var value = meta.Value<string>("@etag");
					if (value != null)
						etag = Etag.Parse(value);
				}

				Current = new StreamResult<T>
				{
					Document = queryOperation.Deserialize<T>(enumerator.Current),
					Etag = etag,
					Key = key,
					Metadata = meta
				};
			}
		}

		public class DocsYieldStream<T> : YieldStream<T>
		{
			public DocsYieldStream(AsyncDocumentSession parent, IAsyncEnumerator<RavenJObject> enumerator)
				: base(parent, enumerator)
			{
			}

			protected override void SetCurrent()
			{
				var document = SerializationHelper.RavenJObjectToJsonDocument(enumerator.Current);

				Current = new StreamResult<T>
				{
					Document = (T)parent.ConvertToEntity(typeof(T),document.Key, document.DataAsJson, document.Metadata),
					Etag = document.Etag,
					Key = document.Key,
					Metadata = document.Metadata
				};
			}
		}

		/// <summary>
		/// Queries the index specified by <typeparamref name="TIndexCreator"/> using lucene syntax.
		/// </summary>
		/// <typeparam name="T">The result of the query</typeparam>
		/// <typeparam name="TIndexCreator">The type of the index creator.</typeparam>
		/// <returns></returns>
		[Obsolete("Use AsyncDocumentQuery instead")]
		public IAsyncDocumentQuery<T> AsyncLuceneQuery<T, TIndexCreator>() where TIndexCreator : AbstractIndexCreationTask, new()
		{
		    return AsyncDocumentQuery<T, TIndexCreator>();
		}

        /// <summary>
        /// Queries the index specified by <typeparamref name="TIndexCreator"/> using lucene syntax.
        /// </summary>
        /// <typeparam name="T">The result of the query</typeparam>
        /// <typeparam name="TIndexCreator">The type of the index creator.</typeparam>
        /// <returns></returns>
        public IAsyncDocumentQuery<T> AsyncDocumentQuery<T, TIndexCreator>() where TIndexCreator : AbstractIndexCreationTask, new()
        {
            var index = new TIndexCreator();

            return AsyncDocumentQuery<T>(index.IndexName, index.IsMapReduce);
        }

        /// <summary>
        /// Query the specified index using Lucene syntax
        /// </summary>
        [Obsolete("Use AsyncDocumentQuery instead.")]
        public IAsyncDocumentQuery<T> AsyncLuceneQuery<T>(string index, bool isMapReduce)
        {
            return AsyncDocumentQuery<T>(index, isMapReduce);
        }

		/// <summary>
		/// Query the specified index using Lucene syntax
		/// </summary>
		public IAsyncDocumentQuery<T> AsyncDocumentQuery<T>(string index, bool isMapReduce)
		{
			return new AsyncDocumentQuery<T>(this,null,AsyncDatabaseCommands, index, new string[0], new string[0], theListeners.QueryListeners, isMapReduce);
		}

        /// <summary>
        /// Dynamically query RavenDB using Lucene syntax
        /// </summary>
        [Obsolete("Use AsyncDocumentQuery instead.")]
        public IAsyncDocumentQuery<T> AsyncLuceneQuery<T>()
        {
            return AsyncDocumentQuery<T>();
        }

		/// <summary>
		/// Dynamically query RavenDB using Lucene syntax
		/// </summary>
		public IAsyncDocumentQuery<T> AsyncDocumentQuery<T>()
		{
			var indexName = "dynamic";
			if (typeof(T).IsEntityType())
			{
				indexName += "/" + Conventions.GetTypeTagName(typeof(T));
			}
			return new AsyncDocumentQuery<T>(this, null, AsyncDatabaseCommands, indexName, new string[0], new string[0], theListeners.QueryListeners, false);
		}

		/// <summary>
		/// Get the accessor for advanced operations
		/// </summary>
		/// <remarks>
		/// Those operations are rarely needed, and have been moved to a separate 
		/// property to avoid cluttering the API
		/// </remarks>
		public IAsyncAdvancedSessionOperations Advanced
		{
			get { return this; }
		}

		/// <summary>
		/// Begin a load while including the specified path 
		/// </summary>
		/// <param name="path">The path.</param>
		public IAsyncLoaderWithInclude<object> Include(string path)
		{
			return new AsyncMultiLoaderWithInclude<object>(this).Include(path);
		}

		/// <summary>
		/// Begin a load while including the specified path 
		/// </summary>
		/// <param name="path">The path.</param>
		public IAsyncLoaderWithInclude<T> Include<T>(Expression<Func<T, object>> path)
		{
			return new AsyncMultiLoaderWithInclude<T>(this).Include(path);
		}

		/// <summary>
		/// Begin a load while including the specified path 
		/// </summary>
		/// <param name="path">The path.</param>
		public IAsyncLoaderWithInclude<T> Include<T, TInclude>(Expression<Func<T, object>> path)
		{
			return new AsyncMultiLoaderWithInclude<T>(this).Include<TInclude>(path);
		}

		/// <summary>
		/// Begins the async load operation, with the specified id after applying
		/// conventions on the provided id to get the real document id.
		/// </summary>
		/// <remarks>
		/// This method allows you to call:
		/// LoadAsync{Post}(1)
		/// And that call will internally be translated to 
		/// LoadAsync{Post}("posts/1");
		/// 
		/// Or whatever your conventions specify.
		/// </remarks>
		public Task<T> LoadAsync<T>(ValueType id)
		{
			var documentKey = Conventions.FindFullDocumentKeyFromNonStringIdentifier(id, typeof(T), false);
			return LoadAsync<T>(documentKey);
		}

		/// <summary>
		/// Begins the async multi-load operation, with the specified ids after applying
		/// conventions on the provided ids to get the real document ids.
		/// </summary>
		/// <remarks>
		/// This method allows you to call:
		/// LoadAsync{Post}(1,2,3)
		/// And that call will internally be translated to 
		/// LoadAsync{Post}("posts/1","posts/2","posts/3");
		/// 
		/// Or whatever your conventions specify.
		/// </remarks>
		public Task<T[]> LoadAsync<T>(params ValueType[] ids)
		{
			var documentKeys = ids.Select(id => Conventions.FindFullDocumentKeyFromNonStringIdentifier(id, typeof(T), false));
			return LoadAsync<T>(documentKeys);
		}

		/// <summary>
		/// Begins the async multi-load operation, with the specified ids after applying
		/// conventions on the provided ids to get the real document ids.
		/// </summary>
		/// <remarks>
		/// This method allows you to call:
		/// LoadAsync{Post}(new List&lt;int&gt;(){1,2,3})
		/// And that call will internally be translated to 
		/// LoadAsync{Post}("posts/1","posts/2","posts/3");
		/// 
		/// Or whatever your conventions specify.
		/// </remarks>
		public Task<T[]> LoadAsync<T>(IEnumerable<ValueType> ids)
		{
			var documentKeys = ids.Select(id => Conventions.FindFullDocumentKeyFromNonStringIdentifier(id, typeof(T), false));
			return LoadAsync<T>(documentKeys);
		}

		/// <summary>
		/// Begins the async load operation
		/// </summary>
		/// <param name="id">The id.</param>
		/// <returns></returns>
		public async Task<T> LoadAsync<T>(string id)
		{
			if (id == null) throw new ArgumentNullException("id", "The document id cannot be null");
			object entity;
			if (entitiesByKey.TryGetValue(id, out entity))
			{
			    return (T) entity;
			}
		    JsonDocument value;
		    if (includedDocumentsByKey.TryGetValue(id, out value))
		    {
		        includedDocumentsByKey.Remove(id);
		        return TrackEntity<T>(value);
		    }
		    if (IsDeleted(id))
		        return default(T);

			IncrementRequestCount();
			var loadOperation = new LoadOperation(this, AsyncDatabaseCommands.DisableAllCaching, id);
			return await CompleteLoadAsync<T>(id, loadOperation);
		}

		private async Task<T> CompleteLoadAsync<T>(string id, LoadOperation loadOperation)
		{
			loadOperation.LogOperation();
			using (loadOperation.EnterLoadContext())
			{
				var result = await AsyncDatabaseCommands.GetAsync(id);

				if (loadOperation.SetResult(result) == false)
					return loadOperation.Complete<T>();

				return await CompleteLoadAsync<T>(id, loadOperation);
			}
		}

		/// <summary>
		/// Begins the async multi load operation
		/// </summary>
		/// <param name="ids">The ids.</param>
		/// <returns></returns>
		public Task<T[]> LoadAsync<T>(params string[] ids)
		{
			return LoadAsync<T>(ids.AsEnumerable());
		}

		public Task<T[]> LoadAsync<T>(IEnumerable<string> ids)
		{
			return LoadAsyncInternal<T>(ids.ToArray(), new KeyValuePair<string, Type>[0]);
		}

		public async Task<T> LoadAsync<TTransformer, T>(string id) where TTransformer : AbstractTransformerCreationTask, new()
		{
			var transformer = new TTransformer();
            var result = await LoadAsyncInternal<T>(new[] { id }, null, transformer.TransformerName).ConfigureAwait(false);
			return result.FirstOrDefault();
		}

		public async Task<T> LoadAsync<TTransformer, T>(string id, Action<ILoadConfiguration> configure) where TTransformer : AbstractTransformerCreationTask, new()
		{
            var result = await LoadAsync<TTransformer, T>(new[] { id }.AsEnumerable(), configure).ConfigureAwait(false);
			return result.FirstOrDefault();
		}

		public async Task<TResult[]> LoadAsync<TTransformer, TResult>(IEnumerable<string> ids, Action<ILoadConfiguration> configure) where TTransformer : AbstractTransformerCreationTask, new()
		{
			var transformer = new TTransformer();
			var ravenLoadConfiguration = new RavenLoadConfiguration();
			configure(ravenLoadConfiguration);
            var result = await LoadAsyncInternal<TResult>(ids.ToArray(), null, transformer.TransformerName, ravenLoadConfiguration.QueryInputs).ConfigureAwait(false);
			return result;
		}

		public Task<T[]> LoadAsync<TTransformer, T>(params string[] ids) where TTransformer : AbstractTransformerCreationTask, new()
		{
			var transformer = new TTransformer();
			return LoadAsyncInternal<T>(ids, null, transformer.TransformerName);
		}

		public async Task<T[]> LoadAsyncInternal<T>(string[] ids, KeyValuePair<string, Type>[] includes, string transformer, Dictionary<string, RavenJToken> queryInputs = null)
		{
			if (ids.Length == 0)
				return new T[0];

			IncrementRequestCount();

			var includePaths = includes != null ? includes.Select(x => x.Key).ToArray() : null;

			if (typeof(T).IsArray)
			{
				// Returns array of arrays, public APIs don't surface that yet though as we only support Transform
				// With a single Id
                var arrayOfArrays = (await AsyncDatabaseCommands.GetAsync(ids, includePaths, transformer, queryInputs).ConfigureAwait(false))
											.Results
											.Select(x => x.Value<RavenJArray>("$values").Cast<RavenJObject>())
											.Select(values =>
											{
												var array = values.Select(y =>
												{
													HandleInternalMetadata(y);
													return ConvertToEntity(typeof(T),null, y, new RavenJObject());
												}).ToArray();
												var newArray = Array.CreateInstance(typeof(T).GetElementType(), array.Length);
												Array.Copy(array, newArray, array.Length);
												return newArray;
											})
											.Cast<T>()
											.ToArray();

				return arrayOfArrays;
			}

            var getResponse = (await this.AsyncDatabaseCommands.GetAsync(ids, includePaths, transformer, queryInputs).ConfigureAwait(false));
			var items = new List<T>();
			foreach (var result in getResponse.Results)
			{
				if (result == null)
				{
					items.Add(default(T));
					continue;
				}
				var transformedResults = result.Value<RavenJArray>("$values").ToArray()
					  .Select(JsonExtensions.ToJObject)
					  .Select(x =>
					  {
						  this.HandleInternalMetadata(x);
						  return this.ConvertToEntity(typeof(T),null, x, new RavenJObject());
					  })
					  .Cast<T>();


				items.AddRange(transformedResults);

			}

			if (items.Count > ids.Length)
			{
				throw new InvalidOperationException(String.Format("A load was attempted with transformer {0}, and more than one item was returned per entity - please use {1}[] as the projection type instead of {1}",
					transformer,
					typeof(T).Name));
			}

			return items.ToArray();
		}

	    public Lazy<Task<T[]>> LazyAsyncLoadInternal<T>(string[] ids, KeyValuePair<string, Type>[] includes, Action<T[]> onEval)
	    {
	        throw new NotImplementedException();
	    }

	    /// <summary>
		/// Begins the async multi load operation
		/// </summary>
		public async Task<T[]> LoadAsyncInternal<T>(string[] ids, KeyValuePair<string, Type>[] includes)
		{
			IncrementRequestCount();
			var multiLoadOperation = new MultiLoadOperation(this, AsyncDatabaseCommands.DisableAllCaching, ids, includes);

			multiLoadOperation.LogOperation();
			var includePaths = includes != null ? includes.Select(x => x.Key).ToArray() : null;
			MultiLoadResult result;
			do
			{
				multiLoadOperation.LogOperation();
				using (multiLoadOperation.EnterMultiLoadContext())
				{
                    result = await AsyncDatabaseCommands.GetAsync(ids, includePaths).ConfigureAwait(false);
				}
			} while (multiLoadOperation.SetResult(result));
			return multiLoadOperation.Complete<T>();
		}

     

		/// <summary>
		/// Begins the async save changes operation
		/// </summary>
		/// <returns></returns>
		public async Task SaveChangesAsync()
		{
			await asyncDocumentKeyGeneration.GenerateDocumentKeysForSaveChanges();

			using (EntityToJson.EntitiesToJsonCachingScope())
			{
				var data = PrepareForSaveChanges();
				if (data.Commands.Count == 0)
					return;

				IncrementRequestCount();

				var result = await AsyncDatabaseCommands.BatchAsync(data.Commands.ToArray());
				UpdateBatchResults(result, data);
			}
		}

		/// <summary>
		/// Get the json document by key from the store
		/// </summary>
		protected override JsonDocument GetJsonDocument(string documentKey)
		{
			throw new NotSupportedException("Cannot get a document in a synchronous manner using async document session");
		}

		/// <summary>
		/// Commits the specified tx id.
		/// </summary>
		/// <param name="txId">The tx id.</param>
		public override void Commit(string txId)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Rollbacks the specified tx id.
		/// </summary>
		/// <param name="txId">The tx id.</param>
		public override void Rollback(string txId)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Dynamically queries RavenDB using LINQ
		/// </summary>
		/// <typeparam name="T">The result of the query</typeparam>
		public IRavenQueryable<T> Query<T>()
		{
			string indexName = "dynamic";
			if (typeof(T).IsEntityType())
			{
				indexName += "/" + Conventions.GetTypeTagName(typeof(T));
			}

			return Query<T>(indexName);
		}

		public IRavenQueryable<T> Query<T, TIndexCreator>() where TIndexCreator : AbstractIndexCreationTask, new()
		{
			var indexCreator = new TIndexCreator();
			return Query<T>(indexCreator.IndexName, indexCreator.IsMapReduce);
		}

		public IRavenQueryable<T> Query<T>(string indexName, bool isMapReduce = false)
		{
			var ravenQueryStatistics = new RavenQueryStatistics();
			var highlightings = new RavenQueryHighlightings();
			return new RavenQueryInspector<T>(
				new RavenQueryProvider<T>(this, indexName, ravenQueryStatistics, highlightings, null, AsyncDatabaseCommands, isMapReduce),
				ravenQueryStatistics,
				highlightings,
				indexName,
				null,
				this, null, AsyncDatabaseCommands, isMapReduce);
		}

		/// <summary>
		/// Create a new query for <typeparam name="T"/>
		/// </summary>
		IDocumentQuery<T> IDocumentQueryGenerator.Query<T>(string indexName, bool isMapReduce)
		{
			throw new NotSupportedException("You can't get a sync query from async session");
		}

		/// <summary>
		/// Create a new query for <typeparam name="T"/>
		/// </summary>
		public IAsyncDocumentQuery<T> AsyncQuery<T>(string indexName, bool isMapReduce = false)
		{
			return AsyncDocumentQuery<T>(indexName, isMapReduce);
		}

		protected override string GenerateKey(object entity)
		{
			throw new NotSupportedException("Async session cannot generate keys synchronously");
		}

		protected override void RememberEntityForDocumentKeyGeneration(object entity)
		{
			asyncDocumentKeyGeneration.Add(entity);
		}

		protected override Task<string> GenerateKeyAsync(object entity)
		{
			return Conventions.GenerateDocumentKeyAsync(dbName, AsyncDatabaseCommands, entity);
		}

	   
      
	}
}
