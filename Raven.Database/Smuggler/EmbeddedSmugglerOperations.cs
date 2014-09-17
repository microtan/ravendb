﻿// -----------------------------------------------------------------------
//  <copyright file="EmbeddedSmugglerOperations.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Indexing;
using Raven.Abstractions.Smuggler;
using Raven.Abstractions.Smuggler.Data;
using Raven.Abstractions.Util;
using Raven.Database.Data;
using Raven.Imports.Newtonsoft.Json;
using Raven.Json.Linq;

namespace Raven.Database.Smuggler
{
	public class EmbeddedSmugglerOperations : ISmugglerOperations
	{
		private readonly DocumentDatabase database;

		private List<JsonDocument> bulkInsertBatch = new List<JsonDocument>();

		public EmbeddedSmugglerOperations(DocumentDatabase database)
		{
			this.database = database;
		}

		public Action<string> Progress { get; set; }

		public Task<RavenJArray> GetIndexes(RavenConnectionStringOptions src, int totalCount)
		{
			return new CompletedTask<RavenJArray>(database.Indexes.GetIndexes(totalCount, 128));
		}

		public JsonDocument GetDocument(string key)
		{
			return database.Documents.Get(key, null);
		}

		public Task<IAsyncEnumerator<RavenJObject>> GetDocuments(RavenConnectionStringOptions src, Etag lastEtag, int take)
		{
			const int dummy = 0;
			var enumerator = database.Documents.GetDocuments(dummy, Math.Min(Options.BatchSize, take), lastEtag, CancellationToken.None)
				.ToList()
				.Cast<RavenJObject>()
				.GetEnumerator();

			return new CompletedTask<IAsyncEnumerator<RavenJObject>>(new AsyncEnumeratorBridge<RavenJObject>(enumerator));
		}

        [Obsolete("Use RavenFS instead.")]
		public Task<Etag> ExportAttachmentsDeletion(JsonTextWriter jsonWriter, Etag startAttachmentsDeletionEtag, Etag maxAttachmentEtag)
		{
			var lastEtag = startAttachmentsDeletionEtag;
			database.TransactionalStorage.Batch(accessor =>
			{
				foreach (var listItem in accessor.Lists.Read(Constants.RavenPeriodicExportsAttachmentsTombstones, startAttachmentsDeletionEtag, maxAttachmentEtag, int.MaxValue))
				{
					var o = new RavenJObject
                    {
                        {"Key", listItem.Key}
                    };
					o.WriteTo(jsonWriter);
					lastEtag = listItem.Etag;
				}
			});
			return new CompletedTask<Etag>(lastEtag);
		}

		public Task<RavenJArray> GetTransformers(RavenConnectionStringOptions src, int start)
		{
			return new CompletedTask<RavenJArray>(database.Transformers.GetTransformers(start, Options.BatchSize));
		}

		public Task<Etag> ExportDocumentsDeletion(JsonTextWriter jsonWriter, Etag startDocsEtag, Etag maxEtag)
		{
			var lastEtag = startDocsEtag;
			database.TransactionalStorage.Batch(accessor =>
			{
				foreach (var listItem in accessor.Lists.Read(Constants.RavenPeriodicExportsDocsTombstones, startDocsEtag, maxEtag, int.MaxValue))
				{
					var o = new RavenJObject
                    {
                        {"Key", listItem.Key}
                    };
					o.WriteTo(jsonWriter);
					lastEtag = listItem.Etag;
				}
			});
			return new CompletedTask<Etag>(lastEtag);
		}

		public LastEtagsInfo FetchCurrentMaxEtags()
		{
			LastEtagsInfo result = null;
			database.TransactionalStorage.Batch(accessor =>
			{
				result = new LastEtagsInfo
				{
					LastDocsEtag = accessor.Staleness.GetMostRecentDocumentEtag(),
					LastAttachmentsEtag = accessor.Staleness.GetMostRecentAttachmentEtag()
				};

				var lastDocumentTombstone = accessor.Lists.ReadLast(Constants.RavenPeriodicExportsDocsTombstones);
				if (lastDocumentTombstone != null)
					result.LastDocDeleteEtag = lastDocumentTombstone.Etag;

				var attachmentTombstones =
					accessor.Lists.Read(Constants.RavenPeriodicExportsAttachmentsTombstones, Etag.Empty, null, int.MaxValue)
							.OrderBy(x => x.Etag).ToArray();
				if (attachmentTombstones.Any())
				{
					result.LastAttachmentsDeleteEtag = attachmentTombstones.Last().Etag;
				}
			});

			return result;
		}

		public Task PutIndex(string indexName, RavenJToken index)
		{
			if (index != null)
			{
				database.Indexes.PutIndex(indexName, index.Value<RavenJObject>("definition").JsonDeserialization<IndexDefinition>());
			}

			return new CompletedTask();
		}

        [Obsolete("Use RavenFS instead.")]
		public Task PutAttachment(RavenConnectionStringOptions dst, AttachmentExportInfo attachmentExportInfo)
		{
			if (attachmentExportInfo != null)
			{
				// we filter out content length, because getting it wrong will cause errors 
				// in the server side when serving the wrong value for this header.
				// worse, if we are using http compression, this value is known to be wrong
				// instead, we rely on the actual size of the data provided for us
				attachmentExportInfo.Metadata.Remove("Content-Length");
				database.Attachments.PutStatic(attachmentExportInfo.Key, null, attachmentExportInfo.Data,
									attachmentExportInfo.Metadata);
			}

			return new CompletedTask();
		}

		public Task PutDocument(RavenJObject document, int size)
		{
			if (document != null)
			{
				var metadata = document.Value<RavenJObject>("@metadata");
				var key = metadata.Value<string>("@id");
				document.Remove("@metadata");

				bulkInsertBatch.Add(new JsonDocument
				{
					Key = key,
					Metadata = metadata,
					DataAsJson = document,
				});

				if (Options.BatchSize > bulkInsertBatch.Count)
					return new CompletedTask();
			}

			var batchToSave = new List<IEnumerable<JsonDocument>> { bulkInsertBatch };
			bulkInsertBatch = new List<JsonDocument>();
			database.Documents.BulkInsert(new BulkInsertOptions { BatchSize = Options.BatchSize, OverwriteExisting = true }, batchToSave, Guid.NewGuid(), CancellationToken.None);
			return new CompletedTask();
		}

		public Task PutTransformer(string transformerName, RavenJToken transformer)
		{
			if (transformer != null)
			{
				var transformerDefinition =
					JsonConvert.DeserializeObject<TransformerDefinition>(transformer.Value<RavenJObject>("definition").ToString());
				database.Transformers.PutTransform(transformerName, transformerDefinition);
			}

			return new CompletedTask();
		}

		public Task DeleteDocument(string key)
		{
			if (key != null)
			{
				database.Documents.Delete(key, null, null);
			}
			return new CompletedTask();
		}

		public SmugglerOptions Options { get; private set; }

        [Obsolete("Use RavenFS instead.")]
		public Task DeleteAttachment(string key)
		{
			database.Attachments.DeleteStatic(key, null);
			return new CompletedTask();
		}

		public void PurgeTombstones(ExportDataResult result)
		{
			database.TransactionalStorage.Batch(accessor =>
			{
				// since remove all before is inclusive, but we want last etag for function FetchCurrentMaxEtags we modify ranges
				accessor.Lists.RemoveAllBefore(Constants.RavenPeriodicExportsDocsTombstones, result.LastDocDeleteEtag.IncrementBy(-1));
				accessor.Lists.RemoveAllBefore(Constants.RavenPeriodicExportsAttachmentsTombstones, result.LastAttachmentsDeleteEtag.IncrementBy(-1));
			});
		}

		public Task<string> GetVersion(RavenConnectionStringOptions server)
		{
			return new CompletedTask<string>(DocumentDatabase.ProductVersion);
		}

		public Task<DatabaseStatistics> GetStats()
		{
			return new CompletedTask<DatabaseStatistics>(database.Statistics);
		}

		public Task<RavenJObject> TransformDocument(RavenJObject document, string transformScript)
		{
			return new CompletedTask<RavenJObject>(document);
		}

		public void Initialize(SmugglerOptions options)
		{
			Options = options;
		}

		public void Configure(SmugglerOptions options)
		{
			var current = options.BatchSize;
			var maxNumberOfItemsToProcessInSingleBatch = database.Configuration.MaxNumberOfItemsToProcessInSingleBatch;

			options.BatchSize = Math.Min(current, maxNumberOfItemsToProcessInSingleBatch);
		}

		public void ShowProgress(string format, params object[] args)
		{
			if (Progress != null)
			{
				Progress(string.Format(format, args));
			}
		}

        [Obsolete("Use RavenFS instead.")]
		public Task<List<AttachmentInformation>> GetAttachments(int start, Etag etag, int maxRecords)
		{
			var attachments = database
				.Attachments
				.GetAttachments(start, maxRecords, etag, null, 1024 * 1024 * 10)
				.ToList();

			return new CompletedTask<List<AttachmentInformation>>(attachments);
		}

        [Obsolete("Use RavenFS instead.")]
		public Task<byte[]> GetAttachmentData(AttachmentInformation attachmentInformation)
		{
			var attachment = database.Attachments.GetStatic(attachmentInformation.Key);
			if (attachment == null) 
				return null;

			var data = attachment.Data;
			attachment.Data = () =>
			{
				var memoryStream = new MemoryStream();
				database.TransactionalStorage.Batch(accessor => data().CopyTo(memoryStream));
				memoryStream.Position = 0;
				return memoryStream;
			};

			return new CompletedTask<byte[]>(attachment.Data().ReadData());
		}
	}
}