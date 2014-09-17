﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Raven.Abstractions.Data;

namespace Raven.Database.Server.Controllers
{
	[RoutePrefix("")]
	public class TransactionController : RavenDbApiController
	{
		[HttpPost]
		[Route("transaction/rollback")]
		[Route("databases/{databaseName}/transaction/rollback")]
		public HttpResponseMessage Rollback()
		{
			var txId = GetQueryStringValue("tx");
			Database.Rollback(txId);
			return GetMessageWithObject(new { Rollbacked = txId });
		}

		[HttpGet]
		[Route("transaction/status")]
		[Route("databases/{databaseName}/transaction/status")]
		public HttpResponseMessage Status()
		{
			var txId = GetQueryStringValue("tx");
			return GetMessageWithObject(new { Exists = Database.HasTransaction(txId) });
		}

		[HttpPost]
		[Route("transaction/prepare")]
		[Route("databases/{databaseName}/transaction/prepare")]
		public async Task<HttpResponseMessage> Prepare()
		{
			var txId = GetQueryStringValue("tx");

			var resourceManagerIdStr = GetQueryStringValue("resourceManagerId");

			Guid resourceManagerId;
			if (Guid.TryParse(resourceManagerIdStr, out resourceManagerId))
			{
				var recoveryInformation = await Request.Content.ReadAsByteArrayAsync();
				if (recoveryInformation == null || recoveryInformation.Length == 0)
					throw new InvalidOperationException("Recovery information is mandatory if resourceManagerId is specified");

				Database.PrepareTransaction(txId, resourceManagerId, recoveryInformation);
			}
			else
			{
				Database.PrepareTransaction(txId);
			}

			return GetMessageWithObject(new { Prepared = txId });
		}

		[HttpPost]
		[Route("transaction/commit")]
		[Route("databases/{databaseName}/transaction/commit")]
		public HttpResponseMessage Commit()
		{
			var txId = GetQueryStringValue("tx");

			var clientVersion = GetHeader(Constants.RavenClientVersion);
			if (clientVersion == null // v1 clients do not send this header.
				|| clientVersion.StartsWith("2.0."))
			{
				Database.PrepareTransaction(txId);
			}

			Database.Commit(txId);
			return GetMessageWithObject(new { Committed = txId });
		}
	}
}
