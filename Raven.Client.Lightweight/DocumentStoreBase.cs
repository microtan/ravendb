﻿using System;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using Raven.Abstractions.Data;
using Raven.Client.Changes;
using Raven.Client.Connection;
using Raven.Client.Connection.Profiling;
using Raven.Client.Document.DTC;
using Raven.Client.Indexes;
using Raven.Client.Listeners;
using Raven.Client.Document;
#if NETFX_CORE
using Raven.Client.WinRT.Connection;
#else
using Raven.Abstractions.Util.Encryptors;
#endif
using Raven.Client.Connection.Async;
using Raven.Client.Util;

namespace Raven.Client
{
	using System.Collections.Generic;

	using Raven.Abstractions.Util.Encryptors;

	/// <summary>
	/// Contains implementation of some IDocumentStore operations shared by DocumentStore implementations
	/// </summary>
	public abstract class DocumentStoreBase : IDocumentStore
	{
		protected DocumentStoreBase()
		{
			InitializeEncryptor();

			LastEtagHolder = new GlobalLastEtagHolder();
			TransactionRecoveryStorage = new VolatileOnlyTransactionRecoveryStorage();
		}

	    public DocumentSessionListeners Listeners
	    {
	        get { return listeners; }
	    }
	    public void SetListeners(DocumentSessionListeners newListeners)
	    {
            this.listeners = newListeners;
	    }

	    public abstract void Dispose();
		
		/// <summary>
		/// 
		/// </summary>
		public abstract event EventHandler AfterDispose;

		/// <summary>
		/// Whatever the instance has been disposed
		/// </summary>
		public bool WasDisposed { get; protected set; }

		/// <summary>
		/// Subscribe to change notifications from the server
		/// </summary>

		public abstract IDisposable AggressivelyCacheFor(TimeSpan cacheDuration);

		public abstract IDatabaseChanges Changes(string database = null);

		public abstract IDisposable DisableAggressiveCaching();

		public abstract IDisposable SetRequestsTimeoutFor(TimeSpan timeout);

#if !NETFX_CORE
		/// <summary>
		/// Gets the shared operations headers.
		/// </summary>
		/// <value>The shared operations headers.</value>
		public virtual NameValueCollection SharedOperationsHeaders { get; protected set; }
#else
		public virtual IDictionary<string,string> SharedOperationsHeaders { get; protected set; }
#endif

		public abstract bool HasJsonRequestFactory { get; }
		public abstract HttpJsonRequestFactory JsonRequestFactory { get; }
		public abstract string Identifier { get; set; }
		public abstract IDocumentStore Initialize();
		public abstract IAsyncDatabaseCommands AsyncDatabaseCommands { get; }
		public abstract IAsyncDocumentSession OpenAsyncSession();
		public abstract IAsyncDocumentSession OpenAsyncSession(string database);

#if !NETFX_CORE
		public abstract IDocumentSession OpenSession();
		public abstract IDocumentSession OpenSession(string database);
		public abstract IDocumentSession OpenSession(OpenSessionOptions sessionOptions);
		public abstract IDatabaseCommands DatabaseCommands { get; }
        
		/// <summary>
		/// Executes the index creation.
		/// </summary>
		public virtual void ExecuteIndex(AbstractIndexCreationTask indexCreationTask)
		{
			indexCreationTask.Execute(DatabaseCommands, Conventions);
		}

	    public Task ExecuteIndexAsync(AbstractIndexCreationTask indexCreationTask)
	    {
	        return indexCreationTask.ExecuteAsync(AsyncDatabaseCommands, Conventions);
	    }

	    /// <summary>
		/// Executes the transformer creation
		/// </summary>
		public virtual void ExecuteTransformer(AbstractTransformerCreationTask transformerCreationTask)
		{
			transformerCreationTask.Execute(DatabaseCommands, Conventions);
		}

	    public Task ExecuteTransformerAsync(AbstractTransformerCreationTask transformerCreationTask)
	    {
	        return transformerCreationTask.ExecuteAsync(AsyncDatabaseCommands, Conventions);
	    }
#endif

		private DocumentConvention conventions;

		/// <summary>
		/// Gets the conventions.
		/// </summary>
		/// <value>The conventions.</value>
		public virtual DocumentConvention Conventions
		{
			get { return conventions ?? (conventions = new DocumentConvention()); }
			set { conventions = value; }
		}

		private string url;

		/// <summary>
		/// Gets or sets the URL.
		/// </summary>
		public virtual string Url
		{
			get { return url; }
			set { url = value.EndsWith("/") ? value.Substring(0, value.Length - 1) : value; }
		}

		/// <summary>
		/// Failover servers used by replication informers when cannot fetch the list of replication 
		/// destinations if a master server is down.
		/// </summary>
		public FailoverServers FailoverServers { get; set; }

		/// <summary>
		/// Whenever or not we will use FIPS compliant encryption algorithms (must match server settings).
		/// </summary>
		public bool UseFipsEncryptionAlgorithms { get; set; }

		///<summary>
		/// Whatever or not we will automatically enlist in distributed transactions
		///</summary>
		public bool EnlistInDistributedTransactions
		{
			get { return Conventions.EnlistInDistributedTransactions; }
			set { Conventions.EnlistInDistributedTransactions = value; }
		}
		/// <summary>
		/// The resource manager id for the document store.
		/// IMPORTANT: Using Guid.NewGuid() to set this value is almost certainly a mistake, you should set
		/// it to a value that remains consistent between restart of the system.
		/// </summary>
		public Guid ResourceManagerId { get; set; }

		
		protected bool initialized;


		///<summary>
		/// Gets the etag of the last document written by any session belonging to this 
		/// document store
		///</summary>
		public virtual Etag GetLastWrittenEtag()
		{
			return LastEtagHolder.GetLastWrittenEtag();
		}

#if !NETFX_CORE
		public abstract BulkInsertOperation BulkInsert(string database = null, BulkInsertOptions options = null);
#endif
		protected void EnsureNotClosed()
		{
			if (WasDisposed)
				throw new ObjectDisposedException(GetType().Name, "The document store has already been disposed and cannot be used");
		}

		protected void AssertInitialized()
		{
			if (!initialized)
				throw new InvalidOperationException("You cannot open a session or access the database commands before initializing the document store. Did you forget calling Initialize()?");
		}

		private DocumentSessionListeners listeners = new DocumentSessionListeners();

		/// <summary>
		/// Registers the conversion listener.
		/// </summary>
		public DocumentStoreBase RegisterListener(IDocumentConversionListener conversionListener)
		{
			listeners.ConversionListeners = listeners.ConversionListeners.Concat(new[] { conversionListener, }).ToArray();
			return this;
		}

		/// <summary>
		/// Registers the extended conversion listener.
		/// </summary>
		public DocumentStoreBase RegisterListener(IExtendedDocumentConversionListener conversionListener)
		{
			listeners.ExtendedConversionListeners = listeners.ExtendedConversionListeners.Concat(new[] { conversionListener, }).ToArray();
			return this;
		}

		/// <summary>
		/// Registers the query listener.
		/// </summary>
		/// <param name="queryListener">The query listener.</param>
		public DocumentStoreBase RegisterListener(IDocumentQueryListener queryListener)
		{
			listeners.QueryListeners = listeners.QueryListeners.Concat(new[] { queryListener }).ToArray();
			return this;
		}
		
		/// <summary>
		/// Registers the store listener.
		/// </summary>
		/// <param name="documentStoreListener">The document store listener.</param>
		public IDocumentStore RegisterListener(IDocumentStoreListener documentStoreListener)
		{
			listeners.StoreListeners = listeners.StoreListeners.Concat(new[] { documentStoreListener }).ToArray();
			return this;
		}

		/// <summary>
		/// Registers the delete listener.
		/// </summary>
		/// <param name="deleteListener">The delete listener.</param>
		public DocumentStoreBase RegisterListener(IDocumentDeleteListener deleteListener)
		{
			listeners.DeleteListeners = listeners.DeleteListeners.Concat(new[] { deleteListener }).ToArray();
			return this;
		}

		/// <summary>
		/// Registers the conflict listener.
		/// </summary>
		/// <param name="conflictListener">The conflict listener.</param>
		public DocumentStoreBase RegisterListener(IDocumentConflictListener conflictListener)
		{
			listeners.ConflictListeners = listeners.ConflictListeners.Concat(new[] { conflictListener }).ToArray();
			return this;
		}

		/// <summary>
		/// Gets a read-only collection of the registered conversion listeners.
		/// </summary>
		public ReadOnlyCollection<IDocumentConversionListener> RegisteredConversionListeners
		{
			get { return new ReadOnlyCollection<IDocumentConversionListener>(listeners.ConversionListeners); }
		}

		/// <summary>
		/// Gets a read-only collection of the registered query listeners.
		/// </summary>
		public ReadOnlyCollection<IDocumentQueryListener> RegisteredQueryListeners
		{
			get { return new ReadOnlyCollection<IDocumentQueryListener>(listeners.QueryListeners); }
		}

		/// <summary>
		/// Gets a read-only collection of the registered store listeners.
		/// </summary>
		public ReadOnlyCollection<IDocumentStoreListener> RegisteredStoreListeners
		{
			get { return new ReadOnlyCollection<IDocumentStoreListener>(listeners.StoreListeners); }
		}

		/// <summary>
		/// Gets a read-only collection of the registered delete listeners.
		/// </summary>
		public ReadOnlyCollection<IDocumentDeleteListener> RegisteredDeleteListeners
		{
			get { return new ReadOnlyCollection<IDocumentDeleteListener>(listeners.DeleteListeners); }
		}

		/// <summary>
		/// Gets a read-only collection of the registered conflict listeners.
		/// </summary>
		public ReadOnlyCollection<IDocumentConflictListener> RegisteredConflictListeners
		{
			get { return new ReadOnlyCollection<IDocumentConflictListener>(listeners.ConflictListeners); }
		}

		protected virtual void AfterSessionCreated(InMemoryDocumentSessionOperations session)
		{
			var onSessionCreatedInternal = SessionCreatedInternal;
			if (onSessionCreatedInternal != null)
				onSessionCreatedInternal(session);
		}

		///<summary>
		/// Internal notification for integration tools, mainly
		///</summary>
		public event Action<InMemoryDocumentSessionOperations> SessionCreatedInternal;

		protected readonly ProfilingContext profilingContext = new ProfilingContext();

		public ILastEtagHolder LastEtagHolder { get; set; }
		public ITransactionRecoveryStorage TransactionRecoveryStorage { get; set; }

		/// <summary>
		///  Get the profiling information for the given id
		/// </summary>
		public ProfilingInformation GetProfilingInformationFor(Guid id)
		{
			return profilingContext.TryGet(id);
		}

		/// <summary>
		/// Setup the context for aggressive caching.
		/// </summary>
		public IDisposable AggressivelyCache()
		{
			return AggressivelyCacheFor(TimeSpan.FromDays(1));
		}

#if !NETFX_CORE
		protected void InitializeEncryptor()
		{
			var setting = ConfigurationManager.AppSettings["Raven/Encryption/FIPS"];

			bool fips;
			if (string.IsNullOrEmpty(setting) || !bool.TryParse(setting, out fips))
				fips = UseFipsEncryptionAlgorithms;

			Encryptor.Initialize(fips);
		}
#else
		protected void InitializeEncryptor()
		{
			Encryptor.Initialize(UseFipsEncryptionAlgorithms);
		}
#endif

    }
}
