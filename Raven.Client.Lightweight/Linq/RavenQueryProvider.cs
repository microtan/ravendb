//-----------------------------------------------------------------------
// <copyright file="RavenQueryProvider.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Raven.Abstractions.Data;
using Raven.Client.Connection.Async;
using Raven.Client.Connection;
using Raven.Client.Document;
using Raven.Json.Linq;
using System.Threading.Tasks;


namespace Raven.Client.Linq
{
	/// <summary>
	/// An implementation of <see cref="IRavenQueryProvider"/>
	/// </summary>
	public class RavenQueryProvider<T> : IRavenQueryProvider
	{
		private Action<QueryResult> afterQueryExecuted;
		private Action<IDocumentQueryCustomization> customizeQuery;
		private readonly string indexName;
		private readonly IDocumentQueryGenerator queryGenerator;
		private readonly RavenQueryStatistics ravenQueryStatistics;
		private readonly RavenQueryHighlightings highlightings;
		private readonly IDatabaseCommands databaseCommands;
		private readonly IAsyncDatabaseCommands asyncDatabaseCommands;
		
        private readonly bool isMapReduce;
        private readonly Dictionary<string, RavenJToken> transformerParamaters = new Dictionary<string, RavenJToken>();
 
	    /// <summary>
		/// Initializes a new instance of the <see cref="RavenQueryProvider{T}"/> class.
		/// </summary>
		public RavenQueryProvider(
			IDocumentQueryGenerator queryGenerator,
			string indexName,
			RavenQueryStatistics ravenQueryStatistics,
			RavenQueryHighlightings highlightings
            , IDatabaseCommands databaseCommands
            , IAsyncDatabaseCommands asyncDatabaseCommands,
			bool isMapReduce
)
		{
			FieldsToFetch = new HashSet<string>();
			FieldsToRename = new List<RenamedField>();

			this.queryGenerator = queryGenerator;
			this.indexName = indexName;
			this.ravenQueryStatistics = ravenQueryStatistics;
			this.highlightings = highlightings;
			this.databaseCommands = databaseCommands;
			this.asyncDatabaseCommands = asyncDatabaseCommands;
			this.isMapReduce = isMapReduce;
		}

		/// <summary>
		/// Gets the actions for customizing the generated lucene query
		/// </summary>
		public Action<IDocumentQueryCustomization> CustomizedQuery
		{
			get { return customizeQuery; }
		}


	    /// <summary>
		/// Gets the name of the index.
		/// </summary>
		/// <value>The name of the index.</value>
		public string IndexName
		{
			get { return indexName; }
		}

		/// <summary>
		/// Get the query generator
		/// </summary>
		public IDocumentQueryGenerator QueryGenerator
		{
			get { return queryGenerator; }
		}

		public Action<IDocumentQueryCustomization> CustomizeQuery
		{
			get { return customizeQuery; }
		}

		/// <summary>
		/// Set the fields to fetch
		/// </summary>
		public HashSet<string> FieldsToFetch { get; private set; }

        /// <summary>
        /// Gets the results transformer to use
        /// </summary>
	    public string ResultTransformer { get; private set; }
        public Dictionary<string, RavenJToken> TransformerParameters { get { return transformerParamaters; } }

		public void AddQueryInput(string name, RavenJToken value)
		{
			AddTransformerParameter(name, value);
		}

	    public void AddTransformerParameter(string name, RavenJToken value)
	    {
	        transformerParamaters[name] = value;
	    }

	   
	    /// <summary>
		/// Set the fields to rename
		/// </summary>
		public List<RenamedField> FieldsToRename { get; private set; }

		/// <summary>
		/// Change the result type for the query provider
		/// </summary>
		public IRavenQueryProvider For<S>()
		{
			if (typeof(T) == typeof(S))
				return this;

			var ravenQueryProvider = new RavenQueryProvider<S>(queryGenerator, indexName, ravenQueryStatistics, highlightings
				, databaseCommands
				, asyncDatabaseCommands,
				isMapReduce
			);
		    ravenQueryProvider.ResultTransformer = ResultTransformer;
			ravenQueryProvider.Customize(customizeQuery);
		    foreach (var transformerParam in this.transformerParamaters)
		    {
		        ravenQueryProvider.AddTransformerParameter(transformerParam.Key, transformerParam.Value);
		    }
			return ravenQueryProvider;
		}

		/// <summary>
		/// Executes the query represented by a specified expression tree.
		/// </summary>
		/// <param name="expression">An expression tree that represents a LINQ query.</param>
		/// <returns>
		/// The value that results from executing the specified query.
		/// </returns>
		public virtual object Execute(Expression expression)
		{
			return GetQueryProviderProcessor<T>().Execute(expression);
		}

		IQueryable<S> IQueryProvider.CreateQuery<S>(Expression expression)
		{
			return new RavenQueryInspector<S>(this, ravenQueryStatistics, highlightings, indexName, expression, (InMemoryDocumentSessionOperations) queryGenerator
											  , databaseCommands
											  , asyncDatabaseCommands, isMapReduce);
		}

		IQueryable IQueryProvider.CreateQuery(Expression expression)
		{
			Type elementType = TypeSystem.GetElementType(expression.Type);
			try
			{
				var makeGenericType = typeof(RavenQueryInspector<>).MakeGenericType(elementType);
				var args = new object[]
				{
					this, ravenQueryStatistics, highlightings, indexName, expression, queryGenerator
					, databaseCommands
					, asyncDatabaseCommands,
					isMapReduce
				};
				return (IQueryable) Activator.CreateInstance(makeGenericType, args);
			}
			catch (TargetInvocationException tie)
			{
				throw tie.InnerException;
			}
		}

		/// <summary>
		/// Executes the specified expression.
		/// </summary>
		/// <typeparam name="S"></typeparam>
		/// <param name="expression">The expression.</param>
		/// <returns></returns>
		S IQueryProvider.Execute<S>(Expression expression)
		{
			return (S)Execute(expression);
		}

		/// <summary>
		/// Executes the query represented by a specified expression tree.
		/// </summary>
		/// <param name="expression">An expression tree that represents a LINQ query.</param>
		/// <returns>
		/// The value that results from executing the specified query.
		/// </returns>
		object IQueryProvider.Execute(Expression expression)
		{
			return Execute(expression);
		}

		/// <summary>
		/// Callback to get the results of the query
		/// </summary>
		public void AfterQueryExecuted(Action<QueryResult> afterQueryExecutedCallback)
		{
			this.afterQueryExecuted = afterQueryExecutedCallback;
		}

		/// <summary>
		/// Customizes the query using the specified action
		/// </summary>
		/// <param name="action">The action.</param>
		public virtual void Customize(Action<IDocumentQueryCustomization> action)
		{
			if (action == null)
				return;
			customizeQuery += action;
		}

	    public void TransformWith(string transformerName)
	    {
	        this.ResultTransformer = transformerName;
	    }

		/// <summary>
		/// Move the registered after query actions
		/// </summary>
		public void MoveAfterQueryExecuted<K>(IAsyncDocumentQuery<K> documentQuery)
		{
			if (afterQueryExecuted != null)
				documentQuery.AfterQueryExecuted(afterQueryExecuted);
		}

		/// <summary>
		/// Convert the expression to a Lucene query
		/// </summary>
        [Obsolete("Use ToAsyncDocumentQuery instead.")]
		public IAsyncDocumentQuery<TResult> ToAsyncLuceneQuery<TResult>(Expression expression)
		{
		    return ToAsyncDocumentQuery<TResult>(expression);
		}

        /// <summary>
        /// Convert the expression to a Lucene query
        /// </summary>
        public IAsyncDocumentQuery<TResult> ToAsyncDocumentQuery<TResult>(Expression expression)
        {
            var processor = GetQueryProviderProcessor<T>();
            var documentQuery = (IAsyncDocumentQuery<TResult>)processor.GetAsyncDocumentQueryFor(expression);

            if (FieldsToRename.Count > 0)
                documentQuery.AfterQueryExecuted(processor.RenameResults);

            return documentQuery;
        }

		/// <summary>
		/// Register the query as a lazy query in the session and return a lazy
		/// instance that will evaluate the query only when needed
		/// </summary>
		public Lazy<IEnumerable<S>> Lazily<S>(Expression expression, Action<IEnumerable<S>> onEval)
		{
			var processor = GetQueryProviderProcessor<S>();
			var query = processor.GetDocumentQueryFor(expression);
			if (afterQueryExecuted != null)
				query.AfterQueryExecuted(afterQueryExecuted);

			var renamedFields = FieldsToFetch.Select(field =>
			{
				var renamedField = FieldsToRename.FirstOrDefault(x => x.OriginalField == field);
				if (renamedField != null)
					return renamedField.NewField ?? field;
				return field;
			}).ToArray();

			if (renamedFields.Length > 0)
				query.AfterQueryExecuted(processor.RenameResults);
		
			if (FieldsToFetch.Count > 0)
				query = query.SelectFields<S>(FieldsToFetch.ToArray(), renamedFields);

			return query.Lazily(onEval);
		}

        /// <summary>
        /// Register the query as a lazy async query in the session and return a lazy async 
        /// instance that will evaluate the query only when needed
        /// </summary>
        public Lazy<Task<IEnumerable<S>>> LazilyAsync<S>(Expression expression, Action<IEnumerable<S>> onEval)
        {
            var processor = GetQueryProviderProcessor<S>();
            var query = processor.GetDocumentQueryForAsync(expression);

            if (afterQueryExecuted != null)
                query.AfterQueryExecuted(afterQueryExecuted);

            var renamedFields = FieldsToFetch.Select(field =>
            {
                var renamedField = FieldsToRename.FirstOrDefault(x => x.OriginalField == field);
                if (renamedField != null)
                    return renamedField.NewField ?? field;
                return field;
            }).ToArray();

            if (renamedFields.Length > 0)
                query.AfterQueryExecuted(processor.RenameResults);

            if (FieldsToFetch.Count > 0)
                query = query.SelectFields<S>(FieldsToFetch.ToArray(), renamedFields);

            return query.LazilyAsync(onEval);
        }
       
		/// <summary>
		/// Register the query as a lazy-count query in the session and return a lazy
		/// instance that will evaluate the query only when needed
		/// </summary>
		public Lazy<int> CountLazily<S>(Expression expression)
		{
			var processor = GetQueryProviderProcessor<S>();
			var query = processor.GetDocumentQueryFor(expression);
			return query.CountLazily();
		}

		protected virtual RavenQueryProviderProcessor<S> GetQueryProviderProcessor<S>()
		{
			return new RavenQueryProviderProcessor<S>(queryGenerator, customizeQuery, afterQueryExecuted, indexName,
				FieldsToFetch, 
				FieldsToRename,
				isMapReduce, ResultTransformer, transformerParamaters);
		}

		/// <summary>
		/// Convert the expression to a Lucene query
		/// </summary>
        [Obsolete("Use ToDocumentQuery instead.")]
		public IDocumentQuery<TResult> ToLuceneQuery<TResult>(Expression expression)
		{
		    return ToDocumentQuery<TResult>(expression);
		}

        /// <summary>
        /// Convert the expression to a Lucene query
        /// </summary>
        public IDocumentQuery<TResult> ToDocumentQuery<TResult>(Expression expression)
        {
            var processor = GetQueryProviderProcessor<T>();
            var result = (IDocumentQuery<TResult>)processor.GetDocumentQueryFor(expression);
            result.SetResultTransformer(ResultTransformer);
            return result;
        }


    }
}