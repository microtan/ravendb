//-----------------------------------------------------------------------
// <copyright file="IDocumentStore.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Raven.Abstractions.Data;
using Raven.Client.Changes;
using Raven.Client.Connection;
using Raven.Client.Connection.Profiling;
using Raven.Client.Document;
using Raven.Client.Listeners;

using System.Collections.Specialized;
using Raven.Client.Indexes;
using Raven.Client.Connection.Async;


namespace Raven.Client
{
	/// <summary>
	/// Interface for managing access to RavenDB and open sessions.
	/// </summary>
	public interface IDocumentStore : IDisposalNotification
	{
		/// <summary>
		/// Subscribe to change notifications from the server
		/// </summary>
		IDatabaseChanges Changes(string database = null);

		/// <summary>
		/// Setup the context for aggressive caching.
		/// </summary>
		/// <param name="cacheDuration">Specify the aggressive cache duration</param>
		/// <remarks>
		/// Aggressive caching means that we will not check the server to see whatever the response
		/// we provide is current or not, but will serve the information directly from the local cache
		/// without touching the server.
		/// </remarks>
		IDisposable AggressivelyCacheFor(TimeSpan cacheDuration);

		/// <summary>
		/// Setup the context for aggressive caching.
		/// </summary>
		/// <remarks>
		/// Aggressive caching means that we will not check the server to see whatever the response
		/// we provide is current or not, but will serve the information directly from the local cache
		/// without touching the server.
		/// </remarks>
		IDisposable AggressivelyCache();

		/// <summary>
		/// Setup the context for no aggressive caching
		/// </summary>
		/// <remarks>
		/// This is mainly useful for internal use inside RavenDB, when we are executing
		/// queries that has been marked with WaitForNonStaleResults, we temporarily disable
		/// aggressive caching.
		/// </remarks>
		IDisposable DisableAggressiveCaching();

		/// <summary>
		/// Setup the WebRequest timeout for the session
		/// </summary>
		/// <param name="timeout">Specify the timeout duration</param>
		/// <remarks>
		/// Sets the timeout for the JsonRequest.  Scoped to the Current Thread.
		/// </remarks>
		IDisposable SetRequestsTimeoutFor(TimeSpan timeout);

		/// <summary>
		/// Gets the shared operations headers.
		/// </summary>
		/// <value>The shared operations headers.</value>
		NameValueCollection SharedOperationsHeaders { get; }

		/// <summary>
		/// Get the <see cref="HttpJsonRequestFactory"/> for this store
		/// </summary>
		HttpJsonRequestFactory JsonRequestFactory { get; }

		/// <summary>
		/// Whatever this instance has json request factory available
		/// </summary>
		bool HasJsonRequestFactory { get; }

		/// <summary>
		/// Gets or sets the identifier for this store.
		/// </summary>
		/// <value>The identifier.</value>
		string Identifier { get; set; }

		/// <summary>
		/// Initializes this instance.
		/// </summary>
		/// <returns></returns>
		IDocumentStore Initialize();

		/// <summary>
		/// Gets the async database commands.
		/// </summary>
		/// <value>The async database commands.</value>
		IAsyncDatabaseCommands AsyncDatabaseCommands { get; }

		/// <summary>
		/// Opens the async session.
		/// </summary>
		/// <returns></returns>
		IAsyncDocumentSession OpenAsyncSession();

		/// <summary>
		/// Opens the async session.
		/// </summary>
		/// <returns></returns>
		IAsyncDocumentSession OpenAsyncSession(string database);

		/// <summary>
		/// Opens the session.
		/// </summary>
		/// <returns></returns>
		IDocumentSession OpenSession();

		/// <summary>
		/// Opens the session for a particular database
		/// </summary>
		IDocumentSession OpenSession(string database);

		/// <summary>
		/// Opens the session with the specified options.
		/// </summary>
		IDocumentSession OpenSession(OpenSessionOptions sessionOptions);

		/// <summary>
		/// Gets the database commands.
		/// </summary>
		/// <value>The database commands.</value>
		IDatabaseCommands DatabaseCommands { get; }

		/// <summary>
		/// Executes the index creation.
		/// </summary>
		void ExecuteIndex(AbstractIndexCreationTask indexCreationTask);

        /// <summary>
        /// Executes the index creation.
        /// </summary>
        /// <param name="indexCreationTask"></param>
        Task ExecuteIndexAsync(AbstractIndexCreationTask indexCreationTask);

		/// <summary>
		/// Executes the transformer creation
		/// </summary>
		void ExecuteTransformer(AbstractTransformerCreationTask transformerCreationTask);

        Task ExecuteTransformerAsync(AbstractTransformerCreationTask transformerCreationTask);

		/// <summary>
		/// Gets the conventions.
		/// </summary>
		/// <value>The conventions.</value>
		DocumentConvention Conventions { get; }

		/// <summary>
		/// Gets the URL.
		/// </summary>
		string Url { get; }

		///<summary>
		/// Gets the etag of the last document written by any session belonging to this 
		/// document store
		///</summary>
		Etag GetLastWrittenEtag();

		BulkInsertOperation BulkInsert(string database = null, BulkInsertOptions options = null);

        DocumentSessionListeners Listeners { get; }

	    void SetListeners(DocumentSessionListeners listeners);
	}
}