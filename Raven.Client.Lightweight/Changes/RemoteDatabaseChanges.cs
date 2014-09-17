﻿using Raven.Abstractions;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Logging;
using Raven.Abstractions.Util;
using Raven.Client.Connection;
using Raven.Client.Document;
using Raven.Client.Extensions;
using Raven.Database.Util;
using Raven.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Raven.Client.Changes
{
    public class RemoteDatabaseChanges : RemoteChangesClientBase<IDatabaseChanges, DatabaseConnectionState>, IDatabaseChanges
    {
        private static readonly ILog logger = LogManager.GetCurrentClassLogger();

        private readonly ConcurrentSet<string> watchedDocs = new ConcurrentSet<string>();
        private readonly ConcurrentSet<string> watchedPrefixes = new ConcurrentSet<string>();
        private readonly ConcurrentSet<string> watchedTypes = new ConcurrentSet<string>();
        private readonly ConcurrentSet<string> watchedCollections = new ConcurrentSet<string>();
        private readonly ConcurrentSet<string> watchedIndexes = new ConcurrentSet<string>();
        private readonly ConcurrentSet<string> watchedBulkInserts = new ConcurrentSet<string>();
        private bool watchAllDocs;
        private bool watchAllIndexes;
        private bool watchAllTransformers;
        
        private readonly Func<string, Etag, string[], OperationMetadata, Task<bool>> tryResolveConflictByUsingRegisteredConflictListenersAsync;

        protected readonly DocumentConvention Conventions;

        public RemoteDatabaseChanges(string url, string apiKey,
                                       ICredentials credentials,
                                       HttpJsonRequestFactory jsonRequestFactory, DocumentConvention conventions,
                                       IReplicationInformerBase replicationInformer,
                                       Action onDispose,                                
                                       Func<string, Etag, string[], OperationMetadata, Task<bool>> tryResolveConflictByUsingRegisteredConflictListenersAsync)
            : base ( url, apiKey, credentials, jsonRequestFactory, conventions, replicationInformer, onDispose)
        {
            this.Conventions = conventions;
            this.tryResolveConflictByUsingRegisteredConflictListenersAsync = tryResolveConflictByUsingRegisteredConflictListenersAsync;
        }

        protected override async Task SubscribeOnServer()
        {
            if (watchAllDocs)
                await Send("watch-docs", null).ConfigureAwait(false);

            if (watchAllIndexes)
                await Send("watch-indexes", null).ConfigureAwait(false);

            if (watchAllTransformers)
                await Send("watch-transformers", null).ConfigureAwait(false);

            foreach (var watchedDoc in watchedDocs)
            {
                await Send("watch-doc", watchedDoc).ConfigureAwait(false);
            }

            foreach (var watchedPrefix in watchedPrefixes)
            {
                await Send("watch-prefix", watchedPrefix).ConfigureAwait(false);
            }

            foreach (var watchedCollection in watchedCollections)
            {
                await Send("watch-collection", watchedCollection);
            }

            foreach (var watchedType in watchedTypes)
            {
                await Send("watch-type", watchedType);
            }

            foreach (var watchedIndex in watchedIndexes)
            {
                await Send("watch-indexes", watchedIndex).ConfigureAwait(false);
            }

            foreach (var watchedBulkInsert in watchedBulkInserts)
            {
                await Send("watch-bulk-operation", watchedBulkInsert).ConfigureAwait(false);
            }
        }

        protected override void NotifySubscribers(string type, RavenJObject value, IEnumerable<KeyValuePair<string, DatabaseConnectionState>> connections)
        {
            switch (type)
            {
                case "DocumentChangeNotification":
                    var documentChangeNotification = value.JsonDeserialization<DocumentChangeNotification>();
                    foreach (var counter in connections)
                    {
                        counter.Value.Send(documentChangeNotification);
                    }
                    break;

                case "BulkInsertChangeNotification":
                    var bulkInsertChangeNotification = value.JsonDeserialization<BulkInsertChangeNotification>();
                    foreach (var counter in connections)
                    {
                        counter.Value.Send(bulkInsertChangeNotification);
                    }
                    break;

                case "IndexChangeNotification":
                    var indexChangeNotification = value.JsonDeserialization<IndexChangeNotification>();
                    foreach (var counter in connections)
                    {
                        counter.Value.Send(indexChangeNotification);
                    }
                    break;
                case "TransformerChangeNotification":
                    var transformerChangeNotification = value.JsonDeserialization<TransformerChangeNotification>();
                    foreach (var counter in connections)
                    {
                        counter.Value.Send(transformerChangeNotification);
                    }
                    break;
                case "ReplicationConflictNotification":
                    var replicationConflictNotification = value.JsonDeserialization<ReplicationConflictNotification>();
                    foreach (var counter in connections)
                    {
                        counter.Value.Send(replicationConflictNotification);
                    }

                    if (replicationConflictNotification.ItemType == ReplicationConflictTypes.DocumentReplicationConflict)
                    {
                        tryResolveConflictByUsingRegisteredConflictListenersAsync(replicationConflictNotification.Id,
                                                                             replicationConflictNotification.Etag,
                                                                             replicationConflictNotification.Conflicts, null)
                            .ContinueWith(t =>
                            {
                                t.AssertNotFailed();

                                if (t.Result)
                                {
                                    logger.Debug("Document replication conflict for {0} was resolved by one of the registered conflict listeners",
                                                 replicationConflictNotification.Id);
                                }
                            });
                    }

                    break;
                default:
                    break;
            }
        }

        public IObservableWithTask<IndexChangeNotification> ForIndex(string indexName)
        {
            var counter = Counters.GetOrAdd("indexes/" + indexName, s =>
            {
                var indexSubscriptionTask = AfterConnection(() =>
                {
                    watchedIndexes.TryAdd(indexName);
                    return Send("watch-index", indexName);
                });

                return new DatabaseConnectionState(
                    () =>
                    {
                        watchedIndexes.TryRemove(indexName);
                        Send("unwatch-index", indexName);
                        Counters.Remove("indexes/" + indexName);
                    },
                    indexSubscriptionTask);
            });
            counter.Inc();
            var taskedObservable = new TaskedObservable<IndexChangeNotification, DatabaseConnectionState>(
                counter,
                notification => string.Equals(notification.Name, indexName, StringComparison.OrdinalIgnoreCase));

            counter.OnIndexChangeNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;


            return taskedObservable;
        }

        public IObservableWithTask<DocumentChangeNotification> ForDocument(string docId)
        {
            var counter = Counters.GetOrAdd("docs/" + docId, s =>
            {
                var documentSubscriptionTask = AfterConnection(() =>
                {
                    watchedDocs.TryAdd(docId);
                    return Send("watch-doc", docId);
                });

                return new DatabaseConnectionState(
                    () =>
                    {
                        watchedDocs.TryRemove(docId);
                        Send("unwatch-doc", docId);
                        Counters.Remove("docs/" + docId);
                    },
                    documentSubscriptionTask);
            });

            var taskedObservable = new TaskedObservable<DocumentChangeNotification, DatabaseConnectionState>(
                counter,
                notification => string.Equals(notification.Id, docId, StringComparison.OrdinalIgnoreCase));

            counter.OnDocumentChangeNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        public IObservableWithTask<DocumentChangeNotification> ForAllDocuments()
        {
            var counter = Counters.GetOrAdd("all-docs", s =>
            {
                var documentSubscriptionTask = AfterConnection(() =>
                {
                    watchAllDocs = true;
                    return Send("watch-docs", null);
                });
                return new DatabaseConnectionState(
                    () =>
                    {
                        watchAllDocs = false;
                        Send("unwatch-docs", null);
                        Counters.Remove("all-docs");
                    },
                    documentSubscriptionTask);
            });
            var taskedObservable = new TaskedObservable<DocumentChangeNotification, DatabaseConnectionState>(
                counter,
                notification => true);

            counter.OnDocumentChangeNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        public IObservableWithTask<IndexChangeNotification> ForAllIndexes()
        {
            var counter = Counters.GetOrAdd("all-indexes", s =>
            {
                var indexSubscriptionTask = AfterConnection(() =>
                {
                    watchAllIndexes = true;
                    return Send("watch-indexes", null);
                });

                return new DatabaseConnectionState(
                    () =>
                    {
                        watchAllIndexes = false;
                        Send("unwatch-indexes", null);
                        Counters.Remove("all-indexes");
                    },
                    indexSubscriptionTask);
            });
            var taskedObservable = new TaskedObservable<IndexChangeNotification, DatabaseConnectionState>(
                counter,
                notification => true);

            counter.OnIndexChangeNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        public IObservableWithTask<TransformerChangeNotification> ForAllTransformers()
        {
            var counter = Counters.GetOrAdd("all-transformers", s =>
            {
                var indexSubscriptionTask = AfterConnection(() =>
                {
                    watchAllTransformers = true;
                    return Send("watch-transformers", null);
                });

                return new DatabaseConnectionState(
                    () =>
                    {
                        watchAllTransformers = false;
                        Send("unwatch-transformers", null);
                        Counters.Remove("all-transformers");
                    },
                    indexSubscriptionTask);
            });
            var taskedObservable = new TaskedObservable<TransformerChangeNotification, DatabaseConnectionState>(
                counter,
                notification => true);

            counter.OnTransformerChangeNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        public IObservableWithTask<DocumentChangeNotification> ForDocumentsStartingWith(string docIdPrefix)
        {
            var counter = Counters.GetOrAdd("prefixes/" + docIdPrefix, s =>
            {
                var documentSubscriptionTask = AfterConnection(() =>
                {
                    watchedPrefixes.TryAdd(docIdPrefix);
                    return Send("watch-prefix", docIdPrefix);
                });

                return new DatabaseConnectionState(
                    () =>
                    {
                        watchedPrefixes.TryRemove(docIdPrefix);
                        Send("unwatch-prefix", docIdPrefix);
                        Counters.Remove("prefixes/" + docIdPrefix);
                    },
                    documentSubscriptionTask);
            });
            var taskedObservable = new TaskedObservable<DocumentChangeNotification, DatabaseConnectionState>(
                counter,
                notification => notification.Id != null && notification.Id.StartsWith(docIdPrefix, StringComparison.OrdinalIgnoreCase));

            counter.OnDocumentChangeNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        public IObservableWithTask<DocumentChangeNotification> ForDocumentsInCollection(string collectionName)
        {
            if (collectionName == null) throw new ArgumentNullException("collectionName");

            var counter = Counters.GetOrAdd("collections/" + collectionName, s =>
            {
                var documentSubscriptionTask = AfterConnection(() =>
                {
                    watchedCollections.TryAdd(collectionName);
                    return Send("watch-collection", collectionName);
                });

                return new DatabaseConnectionState(
                    () =>
                    {
                        watchedCollections.TryRemove(collectionName);
                        Send("unwatch-collection", collectionName);
                        Counters.Remove("collections/" + collectionName);
                    },
                    documentSubscriptionTask);
            });

            var taskedObservable = new TaskedObservable<DocumentChangeNotification, DatabaseConnectionState>(
                counter,
                notification => string.Equals(collectionName, notification.CollectionName, StringComparison.OrdinalIgnoreCase));

            counter.OnDocumentChangeNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        public IObservableWithTask<DocumentChangeNotification> ForDocumentsInCollection<TEntity>()
        {
            var collectionName = Conventions.GetTypeTagName(typeof(TEntity));
            return ForDocumentsInCollection(collectionName);
        }

        public IObservableWithTask<DocumentChangeNotification> ForDocumentsOfType(string typeName)
        {
            if (typeName == null) throw new ArgumentNullException("typeName");
            var encodedTypeName = Uri.EscapeDataString(typeName);

            var counter = Counters.GetOrAdd("types/" + typeName, s =>
            {
                var documentSubscriptionTask = AfterConnection(() =>
                {
                    watchedTypes.TryAdd(typeName);
                    return Send("watch-type", encodedTypeName);
                });

                return new DatabaseConnectionState(
                    () =>
                    {
                        watchedTypes.TryRemove(typeName);
                        Send("unwatch-type", encodedTypeName);
                        Counters.Remove("types/" + typeName);
                    },
                    documentSubscriptionTask);
            });

            var taskedObservable = new TaskedObservable<DocumentChangeNotification, DatabaseConnectionState>(
                counter,
                notification => string.Equals(typeName, notification.TypeName, StringComparison.OrdinalIgnoreCase));

            counter.OnDocumentChangeNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        public IObservableWithTask<DocumentChangeNotification> ForDocumentsOfType(Type type)
        {
            if (type == null) 
				throw new ArgumentNullException("type");

	        var typeName = Conventions.FindClrTypeName(type);
            return ForDocumentsOfType(typeName);
        }

        public IObservableWithTask<DocumentChangeNotification> ForDocumentsOfType<TEntity>()
        {
			var typeName = Conventions.FindClrTypeName(typeof(TEntity));
            return ForDocumentsOfType(typeName);
        }

        public IObservableWithTask<ReplicationConflictNotification> ForAllReplicationConflicts()
        {
            var counter = Counters.GetOrAdd("all-replication-conflicts", s =>
            {
                var indexSubscriptionTask = AfterConnection(() =>
                {
                    watchAllIndexes = true;
                    return Send("watch-replication-conflicts", null);
                });

                return new DatabaseConnectionState(
                    () =>
                    {
                        watchAllIndexes = false;
                        Send("unwatch-replication-conflicts", null);
                        Counters.Remove("all-replication-conflicts");
                    },
                    indexSubscriptionTask);
            });
            var taskedObservable = new TaskedObservable<ReplicationConflictNotification, DatabaseConnectionState>(
                counter,
                notification => true);

            counter.OnReplicationConflictNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        public IObservableWithTask<BulkInsertChangeNotification> ForBulkInsert(Guid operationId)
        {
            var id = operationId.ToString();

            var counter = Counters.GetOrAdd("bulk-operations/" + id, s =>
            {
                watchedBulkInserts.TryAdd(id);
                var documentSubscriptionTask = AfterConnection(() =>
                {
                    if (watchedBulkInserts.Contains(id)) // might have been removed in the meantime
                        return Send("watch-bulk-operation", id);
                    return Task;
                });

                return new DatabaseConnectionState(
                    () =>
                    {
                        watchedBulkInserts.TryRemove(id);
                        Send("unwatch-bulk-operation", id);
                        Counters.Remove("bulk-operations/" + operationId);
                    },
                    documentSubscriptionTask);
            });

            var taskedObservable = new TaskedObservable<BulkInsertChangeNotification, DatabaseConnectionState>(counter,
                                                                                      notification =>
                                                                                      notification.OperationId == operationId);

            counter.OnBulkInsertChangeNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        private Task AfterConnection(Func<Task> action)
        {
            return Task.ContinueWith(task =>
            {
                task.AssertNotFailed();
                return action();
            })
            .Unwrap();
        }

    }
}
