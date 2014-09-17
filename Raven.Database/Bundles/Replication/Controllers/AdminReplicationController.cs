﻿using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

using Raven.Abstractions.Connection;
using Raven.Abstractions.Data;
using Raven.Abstractions.Replication;
using Raven.Database.Bundles.Replication.Impl;
using Raven.Database.Server.Controllers;
using Raven.Json.Linq;

namespace Raven.Database.Bundles.Replication.Controllers
{
	public class AdminReplicationController : AdminBundlesApiController
	{
		public override string BundleName
		{
			get { return "replication"; }
		}

		private readonly HttpRavenRequestFactory requestFactory;

		public AdminReplicationController()
		{
			requestFactory = new HttpRavenRequestFactory();
		}

		[HttpPost]
		[Route("admin/replication/purge-tombstones")]
		[Route("databases/{databaseName}/admin/replication/purge-tombstones")]
		public HttpResponseMessage PurgeTombstones()
		{
			var docEtagStr = GetQueryStringValue("docEtag");
			var attachmentEtagStr = GetQueryStringValue("attachmentEtag");

			Etag docEtag, attachmentEtag;

			var docEtagParsed = Etag.TryParse(docEtagStr, out docEtag);
			var attachmentEtagParsed = Etag.TryParse(attachmentEtagStr, out attachmentEtag);

			if (docEtagParsed == false && attachmentEtagParsed == false)
			{
				return GetMessageWithObject(
					new
					{
						Error = "The query string variable 'docEtag' or 'attachmentEtag' must be set to a valid etag"
					}, HttpStatusCode.BadRequest);
			}

			Database.TransactionalStorage.Batch(accessor =>
			{
				if (docEtag != null)
				{
					accessor.Lists.RemoveAllBefore(Constants.RavenReplicationDocsTombstones, docEtag);
				}
				if (attachmentEtag != null)
				{
					accessor.Lists.RemoveAllBefore(Constants.RavenReplicationAttachmentsTombstones, attachmentEtag);
				}
			});

			return GetEmptyMessage();
		}

		[HttpPost]
		[Route("admin/replicationInfo")]
		[Route("databases/{databaseName}/admin/replicationInfo")]
		public async Task<HttpResponseMessage> ReplicationInfo()
		{
			var replicationDocument = await ReadJsonObjectAsync<ReplicationDocument>();

			if (replicationDocument == null || replicationDocument.Destinations == null || replicationDocument.Destinations.Count == 0)
			{
				return GetMessageWithObject(new
				{
					Error = "Invalid `ReplicationDocument` document supplied."
				}, HttpStatusCode.BadRequest);
			}

			var statuses = CheckDestinations(replicationDocument);

			return GetMessageWithObject(statuses);
		}

		[HttpPost]
		[Route("admin/replication/topology")]
		[Route("databases/{databaseName}/admin/replication/topology")]
		public Task<HttpResponseMessage> ReplicationTopology()
		{
			var replicationSchemaDiscoverer = new ReplicationTopologyDiscoverer(Database, new RavenJArray(), 10, Log);
			var node = replicationSchemaDiscoverer.Discover();
			var topology = node.Flatten();

			return GetMessageWithObjectAsTask(topology);
		}

		[HttpPost]
		[Route("admin/replication/topology/discover")]
		[Route("databases/{databaseName}/admin/replication/topology/discover")]
		public async Task<HttpResponseMessage> ReplicationTopologyDiscover()
		{
			var ttlAsString = GetQueryStringValue("ttl");

			int ttl;
			RavenJArray from;

			if (string.IsNullOrEmpty(ttlAsString))
			{
				ttl = 10;
				from = new RavenJArray();
			}
			else
			{
				ttl = int.Parse(ttlAsString);
				from = await ReadJsonArrayAsync();
			}

			var replicationSchemaDiscoverer = new ReplicationTopologyDiscoverer(Database, from, ttl, Log);
			var node = replicationSchemaDiscoverer.Discover();

			return GetMessageWithObject(node);
		}

		private ReplicationInfoStatus[] CheckDestinations(ReplicationDocument replicationDocument)
		{
			var results = new ReplicationInfoStatus[replicationDocument.Destinations.Count];

			Parallel.ForEach(replicationDocument.Destinations, (replicationDestination, state, i) =>
			{
				var url = replicationDestination.Url;

				if (!url.ToLower().Contains("/databases/"))
				{
					url += "/databases/" + replicationDestination.Database;
				}

				var result = new ReplicationInfoStatus
				{
					Url = url,
					Status = "Valid",
					Code = (int)HttpStatusCode.OK
				};

				results[i] = result;

				var ravenConnectionStringOptions = new RavenConnectionStringOptions
				{
					ApiKey = replicationDestination.ApiKey,
					DefaultDatabase = replicationDestination.Database,
				};
				if (string.IsNullOrEmpty(replicationDestination.Username) == false)
				{
					ravenConnectionStringOptions.Credentials = new NetworkCredential(replicationDestination.Username,
																					 replicationDestination.Password,
																					 replicationDestination.Domain ?? string.Empty);
				}
				var request = requestFactory.Create(url + "/replication/info", "POST", ravenConnectionStringOptions);
				try
				{
					request.ExecuteRequest();
				}
				catch (WebException e)
				{
					FillStatus(result, e);
				}
			});

			return results;
		}

		private void FillStatus(ReplicationInfoStatus replicationInfoStatus, WebException e)
		{
			if (e.GetBaseException() is WebException)
				e = (WebException)e.GetBaseException();

			var response = e.Response as HttpWebResponse;
			if (response == null)
			{
				replicationInfoStatus.Status = e.Message;
				replicationInfoStatus.Code = -1 * (int)e.Status;
				return;
			}

			switch (response.StatusCode)
			{
				case HttpStatusCode.BadRequest:
					string error = GetErrorStringFromException(e, response);
					replicationInfoStatus.Status = error.Contains("Could not figure out what to do")
														   ? "Replication Bundle not activated."
														   : error;
					replicationInfoStatus.Code = (int)response.StatusCode;
					break;
				case HttpStatusCode.PreconditionFailed:
					replicationInfoStatus.Status = "Could not authenticate using OAuth's API Key";
					replicationInfoStatus.Code = (int)response.StatusCode;
					break;
				case HttpStatusCode.Forbidden:
				case HttpStatusCode.Unauthorized:
					replicationInfoStatus.Status = "Could not authenticate using Windows Auth";
					replicationInfoStatus.Code = (int)response.StatusCode;
					break;
				default:
					replicationInfoStatus.Status = response.StatusDescription;
					replicationInfoStatus.Code = (int)response.StatusCode;
					break;
			}
		}


		private static string GetErrorStringFromException(WebException webException, HttpWebResponse response)
		{
			var s = webException.Data["original-value"] as string;
			if (s != null)
				return s;
			using (var streamReader = new StreamReader(response.GetResponseStreamWithHttpDecompression()))
			{
				return streamReader.ReadToEnd();
			}
		}
	}
}
