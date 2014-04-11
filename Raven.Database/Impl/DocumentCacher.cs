﻿using System;
using System.Collections.Specialized;
using System.Runtime.Caching;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Logging;
using Raven.Database.Config;
using Raven.Json.Linq;

namespace Raven.Database.Impl
{
	public class DocumentCacher : IDocumentCacher
	{
		private readonly InMemoryRavenConfiguration configuration;
		private readonly MemoryCache cachedSerializedDocuments;
		private static readonly ILog log = LogManager.GetCurrentClassLogger();
		
		[ThreadStatic]
		private static bool skipSettingDocumentInCache;

		public DocumentCacher(InMemoryRavenConfiguration configuration)
		{
			this.configuration = configuration;
			cachedSerializedDocuments = new MemoryCache(typeof(DocumentCacher).FullName + ".Cache", new NameValueCollection
			{
				{"physicalMemoryLimitPercentage", configuration.MemoryCacheLimitPercentage.ToString()},
				{"pollingInterval",  configuration.MemoryCacheLimitCheckInterval.ToString(@"hh\:mm\:ss")},
				{"cacheMemoryLimitMegabytes", configuration.MemoryCacheLimitMegabytes.ToString()}
			});
			log.Info(@"MemoryCache Settings:
  PhysicalMemoryLimit = {0}
  CacheMemoryLimit    = {1}
  PollingInterval     = {2}", cachedSerializedDocuments.PhysicalMemoryLimit, cachedSerializedDocuments.CacheMemoryLimit,
			  cachedSerializedDocuments.PollingInterval);
		}

		public static IDisposable SkipSettingDocumentsInDocumentCache()
		{
			var old = skipSettingDocumentInCache;
			skipSettingDocumentInCache = true;

			return new DisposableAction(() => skipSettingDocumentInCache = old);
		}

		public CachedDocument GetCachedDocument(string key, Etag etag)
		{
			CachedDocument cachedDocument;
			try
			{
				cachedDocument = (CachedDocument)cachedSerializedDocuments.Get("Doc/" + key + "/" + etag);
			}
			catch (OverflowException)
			{
				// this is a bug in the framework
				// http://connect.microsoft.com/VisualStudio/feedback/details/735033/memorycache-set-fails-with-overflowexception-exception-when-key-is-u7337-u7f01-u2117-exception-message-negating-the-minimum-value-of-a-twos-complement-number-is-invalid 
				// in this case, we just threat it as uncachable
				return null;
			}
			if (cachedDocument == null)
				return null;
			return new CachedDocument
			{
				Document = (RavenJObject)cachedDocument.Document.CreateSnapshot(),
				Metadata = (RavenJObject)cachedDocument.Metadata.CreateSnapshot(),
				Size = cachedDocument.Size
			};
		}

		public void SetCachedDocument(string key, Etag etag, RavenJObject doc, RavenJObject metadata, int size)
		{
			if (skipSettingDocumentInCache)
				return;

			var documentClone = ((RavenJObject)doc.CloneToken());
			documentClone.EnsureCannotBeChangeAndEnableSnapshotting();
			var metadataClone = ((RavenJObject)metadata.CloneToken());
			metadataClone.EnsureCannotBeChangeAndEnableSnapshotting();
			try
			{
				cachedSerializedDocuments.Set("Doc/" + key + "/" + etag, new CachedDocument
				{
					Document = documentClone,
					Metadata = metadataClone,
					Size = size
				}, new CacheItemPolicy
				{
					SlidingExpiration = configuration.MemoryCacheExpiration,
				});
			}
			catch (OverflowException)
			{
				// this is a bug in the framework
				// http://connect.microsoft.com/VisualStudio/feedback/details/735033/memorycache-set-fails-with-overflowexception-exception-when-key-is-u7337-u7f01-u2117-exception-message-negating-the-minimum-value-of-a-twos-complement-number-is-invalid 
				// in this case, we just threat it as uncachable
			}

		}

		public void RemoveCachedDocument(string key, Etag etag)
		{
			try
			{
				cachedSerializedDocuments.Remove("Doc/" + key + "/" + etag);
			}
			catch (OverflowException)
			{
				// this is a bug in the framework
				// http://connect.microsoft.com/VisualStudio/feedback/details/735033/memorycache-set-fails-with-overflowexception-exception-when-key-is-u7337-u7f01-u2117-exception-message-negating-the-minimum-value-of-a-twos-complement-number-is-invalid 
				// in this case, we just threat it as uncachable
			}
		}

		public void Dispose()
		{
			cachedSerializedDocuments.Dispose();
		}
	}
}