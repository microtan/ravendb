﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Raven.Database.Server.RavenFS.Extensions;
using Raven.Abstractions.Logging;
using Raven.Database.Server.RavenFS.Util;
using Raven.Imports.Newtonsoft.Json;
using Raven.Json.Linq;
using Raven.Abstractions.Extensions;
using System.Web.Http.ModelBinding;
using System.Text.RegularExpressions;
using Raven.Abstractions.FileSystem.Notifications;
using Raven.Abstractions.FileSystem;

namespace Raven.Database.Server.RavenFS.Controllers
{
	public class ConfigController : RavenFsApiController
	{
		private static new readonly ILog Log = LogManager.GetCurrentClassLogger();

		[HttpGet]
		[Route("fs/{fileSystemName}/config")]
        public HttpResponseMessage Get()
		{
			string[] names = null;
			Storage.Batch(accessor => { names = accessor.GetConfigNames(Paging.Start, Paging.PageSize).ToArray(); });

            return this.GetMessageWithObject(names)
                       .WithNoCache();
		}

		[HttpGet]
        [Route("fs/{fileSystemName}/config")]
        public HttpResponseMessage Get(string name)
		{
			try
			{
                RavenJObject config = null;
                Storage.Batch(accessor => { config = accessor.GetConfig(name); });
                
                return this.GetMessageWithObject(config, HttpStatusCode.OK)
                           .WithNoCache();
			}
			catch (FileNotFoundException)
			{
                return this.GetEmptyMessage(HttpStatusCode.NotFound)
                           .WithNoCache();
			}
		}

        [HttpGet]
        [Route("fs/{fileSystemName}/config/non-generated")]
        public HttpResponseMessage NonGeneratedConfigNames()
        {
            IEnumerable<string> configs = null;
            Storage.Batch(accessor => { configs = accessor.GetConfigNames(Paging.Start, Paging.PageSize).ToList(); });
            
            var searchPattern = new Regex("^(sync|deleteOp|raven\\/synchronization\\/sources|conflicted|renameOp)", RegexOptions.IgnoreCase);
            configs = configs.Where((c) => !searchPattern.IsMatch(c)).AsEnumerable();

            return this.GetMessageWithObject(configs)
                       .WithNoCache();
        }

		[HttpGet]
        [Route("fs/{fileSystemName}/config/search")]
        public HttpResponseMessage ConfigNamesStartingWith(string prefix)
		{
			if (prefix == null)
				prefix = "";
			ConfigurationSearchResults results = null;
			Storage.Batch(accessor =>
			{
				int totalResults;
				var names = accessor.GetConfigNamesStartingWithPrefix(prefix, Paging.Start, Paging.PageSize,
																	  out totalResults);

				results = new ConfigurationSearchResults
				{
					ConfigNames = names,
					PageSize = Paging.PageSize,
					Start = Paging.Start,
					TotalCount = totalResults
				};
			});

            return this.GetMessageWithObject(results)
                       .WithNoCache();
		}

		[HttpPut]
        [Route("fs/{fileSystemName}/config")]
		public async Task<HttpResponseMessage> Put(string name)
		{
            var json = await ReadJsonAsync();

            ConcurrencyAwareExecutor.Execute(() => Storage.Batch(accessor => accessor.SetConfig(name, json)), ConcurrencyResponseException);

            Publisher.Publish(new ConfigurationChangeNotification { Name = name, Action = ConfigurationChangeAction.Set });

            Log.Debug("Config '{0}' was inserted", name);

            return this.GetMessageWithObject(json, HttpStatusCode.Created)
                       .WithNoCache();
		}

		[HttpDelete]
        [Route("fs/{fileSystemName}/config")]
        public HttpResponseMessage Delete(string name)
		{
			ConcurrencyAwareExecutor.Execute(() => Storage.Batch(accessor => accessor.DeleteConfig(name)),
											 ConcurrencyResponseException);

			Publisher.Publish(new ConfigurationChangeNotification { Name = name, Action = ConfigurationChangeAction.Delete });

			Log.Debug("Config '{0}' was deleted", name);

            return GetEmptyMessage(HttpStatusCode.NoContent);
		}
	}
}