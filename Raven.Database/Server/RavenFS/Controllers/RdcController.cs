﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Raven.Abstractions.Logging;
using Raven.Database.Server.RavenFS.Extensions;
using Raven.Database.Server.RavenFS.Storage;
using Raven.Database.Server.RavenFS.Synchronization;
using Raven.Database.Server.RavenFS.Synchronization.Rdc;
using Raven.Database.Server.RavenFS.Synchronization.Rdc.Wrapper;
using Raven.Abstractions.FileSystem;
using Raven.Abstractions.Data;
using Raven.Database.Server.RavenFS.Util;

namespace Raven.Database.Server.RavenFS.Controllers
{
	public class RdcController : RavenFsApiController
	{
		private static new readonly ILog Log = LogManager.GetCurrentClassLogger();

		[HttpGet]
        [Route("fs/{fileSystemName}/rdc/Signatures/{*id}")]
		public HttpResponseMessage Signatures(string id)
		{
            var canonicalFilename = FileHeader.Canonize(id);

			Log.Debug("Got signatures of a file '{0}' request", id);

            using (var signatureRepository = new StorageSignatureRepository(Storage, canonicalFilename))
			{
				var localRdcManager = new LocalRdcManager(signatureRepository, Storage, SigGenerator);
                var resultContent = localRdcManager.GetSignatureContentForReading(canonicalFilename);
                return StreamResult(canonicalFilename, resultContent);
			}
		}

		[HttpGet]
        [Route("fs/{fileSystemName}/rdc/Stats")]
		public HttpResponseMessage Stats()
		{
			using (var rdcVersionChecker = new RdcVersionChecker())
			{
				var rdcVersion = rdcVersionChecker.GetRdcVersion();

                var stats = new RdcStats
                {
                    CurrentVersion = rdcVersion.CurrentVersion,
                    MinimumCompatibleAppVersion = rdcVersion.MinimumCompatibleAppVersion
                };

                return GetMessageWithObject(stats)
                           .WithNoCache();
			}
		}

		[HttpGet]
        [Route("fs/{fileSystemName}/rdc/Manifest/{*id}")]
        public async Task<HttpResponseMessage> Manifest(string id)
		{
            var canonicalFilename = FileHeader.Canonize(id);

			FileAndPagesInformation fileAndPages = null;
			try
			{
                Storage.Batch(accessor => fileAndPages = accessor.GetFile(canonicalFilename, 0, 0));
			}
			catch (FileNotFoundException)
			{
				Log.Debug("Signature manifest for a file '{0}' was not found", id);
				return Request.CreateResponse(HttpStatusCode.NotFound);
			}

			long? fileLength = fileAndPages.TotalSize;

            using (var signatureRepository = new StorageSignatureRepository(Storage, canonicalFilename))
			{
				var rdcManager = new LocalRdcManager(signatureRepository, Storage, SigGenerator);
				var signatureManifest = await rdcManager.GetSignatureManifestAsync(
                                                                new DataInfo
					                                            {
                                                                    Name = canonicalFilename,
                                                                    LastModified = fileAndPages.Metadata.Value<DateTime>(Constants.LastModified)
								                                                       .ToUniversalTime()
					                                            });
				signatureManifest.FileLength = fileLength ?? 0;

				Log.Debug("Signature manifest for a file '{0}' was downloaded. Signatures count was {1}", id, signatureManifest.Signatures.Count);

                return GetMessageWithObject(signatureManifest)
                           .WithNoCache();
			}
		}
	}
}