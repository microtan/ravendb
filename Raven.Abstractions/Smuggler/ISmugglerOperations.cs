﻿// -----------------------------------------------------------------------
//  <copyright file="ISmugglerOperations.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Raven.Abstractions.Data;
using Raven.Abstractions.Smuggler.Data;
using Raven.Abstractions.Util;
using Raven.Database.Data;
using Raven.Imports.Newtonsoft.Json;
using Raven.Json.Linq;

namespace Raven.Abstractions.Smuggler
{
	public interface ISmugglerOperations
	{
		SmugglerOptions Options { get; }

        [Obsolete("Use RavenFS instead.")]
		Task DeleteAttachment(string key);

		Task DeleteDocument(string key);

        [Obsolete("Use RavenFS instead.")]
		Task<Etag> ExportAttachmentsDeletion(JsonTextWriter jsonWriter, Etag startAttachmentsDeletionEtag, Etag maxAttachmentEtag);

		Task<Etag> ExportDocumentsDeletion(JsonTextWriter jsonWriter, Etag startDocsEtag, Etag maxEtag);

		LastEtagsInfo FetchCurrentMaxEtags();

        [Obsolete("Use RavenFS instead.")]
		Task<List<AttachmentInformation>> GetAttachments(int start, Etag etag, int maxRecords);

        [Obsolete("Use RavenFS instead.")]
		Task<byte[]> GetAttachmentData(AttachmentInformation attachmentInformation);

		JsonDocument GetDocument(string key);

		Task<IAsyncEnumerator<RavenJObject>> GetDocuments(RavenConnectionStringOptions src, Etag lastEtag, int take);

		Task<RavenJArray> GetIndexes(RavenConnectionStringOptions src, int totalCount);

		Task<DatabaseStatistics> GetStats();

		Task<RavenJArray> GetTransformers(RavenConnectionStringOptions src, int start);

		Task<string> GetVersion(RavenConnectionStringOptions server);

		void PurgeTombstones(ExportDataResult result);

        [Obsolete("Use RavenFS instead.")]
		Task PutAttachment(RavenConnectionStringOptions dst, AttachmentExportInfo attachmentExportInfo);

		Task PutDocument(RavenJObject document, int size);

		Task PutIndex(string indexName, RavenJToken index);

		Task PutTransformer(string transformerName, RavenJToken transformer);

		void ShowProgress(string format, params object[] args);

		Task<RavenJObject> TransformDocument(RavenJObject document, string transformScript);

		void Initialize(SmugglerOptions options);

		void Configure(SmugglerOptions options);
	}
}