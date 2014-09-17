﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using Mono.CSharp;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Logging;
using Raven.Abstractions.Replication;
using Raven.Bundles.Replication.Data;
using Raven.Bundles.Replication.Plugins;
using Raven.Bundles.Replication.Responders;
using Raven.Bundles.Replication.Tasks;
using Raven.Database.Bundles.Replication.Plugins;
using Raven.Database.Bundles.Replication.Utils;
using Raven.Database.Server.Controllers;
using Raven.Database.Storage;
using Raven.Database.Util;
using Raven.Json.Linq;

namespace Raven.Database.Bundles.Replication.Controllers
{
	public class ReplicationController : BundlesApiController
	{
		private static readonly ILog log = LogManager.GetCurrentClassLogger();

		public override string BundleName
		{
			get { return "replication"; }
		}

		private ReplicationTask replicationTask;
		public ReplicationTask ReplicationTask
		{
			get { return replicationTask ?? (replicationTask = Database.StartupTasks.OfType<ReplicationTask>().FirstOrDefault()); }
		}

		public IEnumerable<AbstractDocumentReplicationConflictResolver> DocsReplicationConflictResolvers
		{
			get
			{
				var exported = Database.Configuration.Container.GetExportedValues<AbstractDocumentReplicationConflictResolver>();

				var config = GetReplicationConfig();

				if (config == null || config.DocumentConflictResolution == StraightforwardConflictResolution.None)
					return exported;

				var withConfiguredResolvers = exported.ToList();

				switch (config.DocumentConflictResolution)
				{
					case StraightforwardConflictResolution.ResolveToLocal:
						withConfiguredResolvers.Add(LocalDocumentReplicationConflictResolver.Instance);
						break;
					case StraightforwardConflictResolution.ResolveToRemote:
						withConfiguredResolvers.Add(RemoteDocumentReplicationConflictResolver.Instance);
						break;
					case StraightforwardConflictResolution.ResolveToLatest:
						withConfiguredResolvers.Add(LatestDocumentReplicationConflictResolver.Instance);
						break;
					default:
						throw new ArgumentOutOfRangeException("config.DocumentConflictResolution");
				}

				return withConfiguredResolvers;
			}
		}

		[HttpGet]
		[Route("replication/topology")]
		[Route("databases/{databaseName}/replication/topology")]
		public HttpResponseMessage TopologyGet()
		{
			var documentsController = new DocumentsController();
			documentsController.InitializeFrom(this);
			return documentsController.DocGet(Constants.RavenReplicationDestinations);
		}

		[Obsolete("Use RavenFS instead.")]
		public IEnumerable<AbstractAttachmentReplicationConflictResolver> AttachmentReplicationConflictResolvers
		{
			get
			{
				var exported = Database.Configuration.Container.GetExportedValues<AbstractAttachmentReplicationConflictResolver>();

				var config = GetReplicationConfig();

				if (config == null || config.AttachmentConflictResolution == StraightforwardConflictResolution.None)
					return exported;

				var withConfiguredResolvers = exported.ToList();

				switch (config.AttachmentConflictResolution)
				{
					case StraightforwardConflictResolution.ResolveToLocal:
						withConfiguredResolvers.Add(LocalAttachmentReplicationConflictResolver.Instance);
						break;
					case StraightforwardConflictResolution.ResolveToRemote:
						withConfiguredResolvers.Add(RemoteAttachmentReplicationConflictResolver.Instance);
						break;
					case StraightforwardConflictResolution.ResolveToLatest:
						// ignore this resolver for attachments
						break;
					default:
						throw new ArgumentOutOfRangeException("config.AttachmentConflictResolution");
				}

				return withConfiguredResolvers;
			}
		}

		[HttpPost]
		[Route("replication/replicateDocs")]
		[Route("databases/{databaseName}/replication/replicateDocs")]
		public async Task<HttpResponseMessage> DocReplicatePost()
		{
			var src = GetQueryStringValue("from");
			if (string.IsNullOrEmpty(src))
				return GetEmptyMessage(HttpStatusCode.BadRequest);
		
			while (src.EndsWith("/"))
				src = src.Substring(0, src.Length - 1);// remove last /, because that has special meaning for Raven

			if (string.IsNullOrEmpty(src))
				return GetEmptyMessage(HttpStatusCode.BadRequest);
			
			var array = await ReadJsonArrayAsync();
			if (ReplicationTask != null)
				ReplicationTask.HandleHeartbeat(src);
		
			using (Database.DisableAllTriggersForCurrentThread())
			{
				Database.TransactionalStorage.Batch(actions =>
				{
					string lastEtag = Etag.Empty.ToString();
					foreach (RavenJObject document in array)
					{
						var metadata = document.Value<RavenJObject>("@metadata");
						if (metadata[Constants.RavenReplicationSource] == null)
						{
							// not sure why, old document from when the user didn't have replication
							// that we suddenly decided to replicate, choose the source for that
							metadata[Constants.RavenReplicationSource] = RavenJToken.FromObject(src);
						}
						lastEtag = metadata.Value<string>("@etag");
						var id = metadata.Value<string>("@id");
						document.Remove("@metadata");
						ReplicateDocument(actions, id, metadata, document, src);
					}

					var replicationDocKey = Constants.RavenReplicationSourcesBasePath + "/" + src;
					var replicationDocument = Database.Documents.Get(replicationDocKey, null);
					var lastAttachmentId = Etag.Empty;
					if (replicationDocument != null)
					{
						lastAttachmentId =
							replicationDocument.DataAsJson.JsonDeserialization<SourceReplicationInformation>().
								LastAttachmentEtag;
					}

					Guid serverInstanceId;
					if (Guid.TryParse(GetQueryStringValue("dbid"), out serverInstanceId) == false)
						serverInstanceId = Database.TransactionalStorage.Id;
					Database.Documents.Put(replicationDocKey, null,
								 RavenJObject.FromObject(new SourceReplicationInformation
								 {
									 Source = src,
									 LastDocumentEtag = Etag.Parse(lastEtag),
									 LastAttachmentEtag = lastAttachmentId,
									 ServerInstanceId = serverInstanceId
								 }),
								 new RavenJObject(), null);
				});
			}

			return GetEmptyMessage();
		}

		[HttpPost]
		[Route("replication/replicateAttachments")]
		[Route("databases/{databaseName}/replication/replicateAttachments")]
        [Obsolete("Use RavenFS instead.")]
		public async Task<HttpResponseMessage> AttachmentReplicatePost()
		{
			var src = GetQueryStringValue("from");
			if (string.IsNullOrEmpty(src))
				return GetEmptyMessage(HttpStatusCode.BadRequest);
			
			while (src.EndsWith("/"))
				src = src.Substring(0, src.Length - 1);// remove last /, because that has special meaning for Raven
			if (string.IsNullOrEmpty(src))
				return GetEmptyMessage(HttpStatusCode.BadRequest);
			
			var array = await ReadBsonArrayAsync();
			using (Database.DisableAllTriggersForCurrentThread())
			{
				Database.TransactionalStorage.Batch(actions =>
				{
					Etag lastEtag = Etag.Empty;
					foreach (RavenJObject attachment in array)
					{
						var metadata = attachment.Value<RavenJObject>("@metadata");
						if (metadata[Constants.RavenReplicationSource] == null)
						{
							// not sure why, old attachment from when the user didn't have replication
							// that we suddenly decided to replicate, choose the source for that
							metadata[Constants.RavenReplicationSource] = RavenJToken.FromObject(src);
						}

						lastEtag = Etag.Parse(attachment.Value<byte[]>("@etag"));
						var id = attachment.Value<string>("@id");

						ReplicateAttachment(actions, id, metadata, attachment.Value<byte[]>("data"), src);
					}

					var replicationDocKey = Constants.RavenReplicationSourcesBasePath + "/" + src;
					var replicationDocument = Database.Documents.Get(replicationDocKey, null);
					Etag lastDocId = null;
					if (replicationDocument != null)
					{
						lastDocId =
							replicationDocument.DataAsJson.JsonDeserialization<SourceReplicationInformation>().
								LastDocumentEtag;
					}
					
					Guid serverInstanceId;
					if (Guid.TryParse(GetQueryStringValue("dbid"), out serverInstanceId) == false)
						serverInstanceId = Database.TransactionalStorage.Id;
					Database.Documents.Put(replicationDocKey, null,
								 RavenJObject.FromObject(new SourceReplicationInformation
								 {
									 Source = src,
									 LastDocumentEtag = lastDocId,
									 LastAttachmentEtag = lastEtag,
									 ServerInstanceId = serverInstanceId
								 }),
								 new RavenJObject(), null);
				});
			}

			return GetEmptyMessage();
		}

		[HttpGet]
		[HttpPost]
		[Route("replication/info")]
		[Route("databases/{databaseName}/replication/info")]
		public HttpResponseMessage ReplicationInfoGet()
		{
		    var replicationStatistics = ReplicationUtils.GetReplicationInformation(Database);
			return GetMessageWithObject(replicationStatistics);
		}

		[HttpGet]
		[Route("replication/lastEtag")]
		[Route("databases/{databaseName}/replication/lastEtag")]
		public HttpResponseMessage ReplicationLastEtagGet()
		{
			string src;
			string dbid;
			var result = GetValuesForLastEtag(out src, out dbid);
			if (result != null)
				return result;

			using (Database.DisableAllTriggersForCurrentThread())
			{
				var document = Database.Documents.Get(Constants.RavenReplicationSourcesBasePath + "/" + src, null);

				SourceReplicationInformation sourceReplicationInformation;

				var serverInstanceId = Database.TransactionalStorage.Id; // this is my id, sent to the remote serve

				if (document == null)
				{
					sourceReplicationInformation = new SourceReplicationInformation()
					{
						Source = src,
						ServerInstanceId = serverInstanceId
					};
				}
				else
				{
					sourceReplicationInformation = document.DataAsJson.JsonDeserialization<SourceReplicationInformation>();
					sourceReplicationInformation.ServerInstanceId = serverInstanceId;
				}

				var currentEtag = GetQueryStringValue("currentEtag");
				Log.Debug(() => string.Format("Got replication last etag request from {0}: [Local: {1} Remote: {2}]", src, sourceReplicationInformation.LastDocumentEtag, currentEtag));
				return GetMessageWithObject(sourceReplicationInformation);
			}
		}

		[HttpPut]
		[Route("replication/lastEtag")]
		[Route("databases/{databaseName}/replication/lastEtag")]
		public HttpResponseMessage ReplicationLastEtagPut()
		{
			string src;
			string dbid;
			var result = GetValuesForLastEtag(out src, out dbid);
			if (result != null)
				return result;

			using (Database.DisableAllTriggersForCurrentThread())
			{
				var document = Database.Documents.Get(Constants.RavenReplicationSourcesBasePath + "/" + src, null);

				SourceReplicationInformation sourceReplicationInformation;

				Etag docEtag = null, attachmentEtag = null;
				try
				{
					docEtag = Etag.Parse(GetQueryStringValue("docEtag"));
				}
				catch
				{

				}
				try
				{
					attachmentEtag = Etag.Parse(GetQueryStringValue("attachmentEtag"));
				}
				catch
				{

				}
				Guid serverInstanceId;
				if (Guid.TryParse(dbid, out serverInstanceId) == false)
					serverInstanceId = Database.TransactionalStorage.Id;

				if (document == null)
				{
					sourceReplicationInformation = new SourceReplicationInformation()
					{
						ServerInstanceId = serverInstanceId,
						LastAttachmentEtag = attachmentEtag ?? Etag.Empty,
						LastDocumentEtag = docEtag ?? Etag.Empty,
						Source = src
					};
				}
				else
				{
					sourceReplicationInformation = document.DataAsJson.JsonDeserialization<SourceReplicationInformation>();
					sourceReplicationInformation.ServerInstanceId = serverInstanceId;
					sourceReplicationInformation.LastDocumentEtag = docEtag ?? sourceReplicationInformation.LastDocumentEtag;
					sourceReplicationInformation.LastAttachmentEtag = attachmentEtag ?? sourceReplicationInformation.LastAttachmentEtag;
				}

				var etag = document == null ? Etag.Empty : document.Etag;
				var metadata = document == null ? new RavenJObject() : document.Metadata;

				var newDoc = RavenJObject.FromObject(sourceReplicationInformation);
				log.Debug("Updating replication last etags from {0}: [doc: {1} attachment: {2}]", src,
								  sourceReplicationInformation.LastDocumentEtag,
								  sourceReplicationInformation.LastAttachmentEtag);

				Database.Documents.Put(Constants.RavenReplicationSourcesBasePath + "/" + src, etag, newDoc, metadata, null);
			}

			return GetEmptyMessage();
		}

		[HttpPost]
		[Route("replication/heartbeat")]
		[Route("databases/{databaseName}/replication/heartbeat")]
		public HttpResponseMessage HeartbeatPost()
		{
			var src = GetQueryStringValue("from");

			var replicationTask = Database.StartupTasks.OfType<ReplicationTask>().FirstOrDefault();
			if (replicationTask == null)
			{
				return GetMessageWithObject(new
				{
					Error = "Cannot find replication task setup in the database"
				}, HttpStatusCode.NotFound);

			}

			replicationTask.HandleHeartbeat(src);

			return GetEmptyMessage();
		}

		private HttpResponseMessage GetValuesForLastEtag(out string src, out string dbid)
		{
			src = GetQueryStringValue("from");
			dbid = GetQueryStringValue("dbid");
			if (dbid == Database.TransactionalStorage.Id.ToString())
				throw new InvalidOperationException("Both source and target databases have database id = " + dbid +
				                                    "\r\nDatabase cannot replicate to itself.");

			if (string.IsNullOrEmpty(src))
				return GetEmptyMessage(HttpStatusCode.BadRequest);

			while (src.EndsWith("/"))
				src = src.Substring(0, src.Length - 1); // remove last /, because that has special meaning for Raven
			if (string.IsNullOrEmpty(src))
				return GetEmptyMessage(HttpStatusCode.BadRequest);
			return null;
		}

		private void ReplicateDocument(IStorageActionsAccessor actions, string id, RavenJObject metadata, RavenJObject document, string src)
		{
			new DocumentReplicationBehavior
			{
				Actions = actions,
				Database = Database,
				ReplicationConflictResolvers = DocsReplicationConflictResolvers,
				Src = src
			}.Replicate(id, metadata, document);
		}

        [Obsolete("Use RavenFS instead.")]
		private void ReplicateAttachment(IStorageActionsAccessor actions, string id, RavenJObject metadata, byte[] data, string src)
		{
			new AttachmentReplicationBehavior
			{
				Actions = actions,
				Database = Database,
				ReplicationConflictResolvers = AttachmentReplicationConflictResolvers,
				Src = src
			}.Replicate(id, metadata, data);
		}

		private ReplicationConfig GetReplicationConfig()
		{
			var configDoc = Database.Documents.Get(Constants.RavenReplicationConfig, null);

			if (configDoc == null)
				return null;

			ReplicationConfig config;
			try
			{
				config = configDoc.DataAsJson.JsonDeserialization<ReplicationConfig>();
				return config;
			}
			catch (Exception e)
			{
				Log.Warn("Could not deserialize a replication config", e);
				return null;
			}
		}
	}
}