﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Raven.Abstractions;
using Raven.Abstractions.Data;
using Raven.Abstractions.Exceptions;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Indexing;
using Raven.Abstractions.Logging;
using Raven.Database.Data;
using Raven.Database.Extensions;
using Raven.Database.Queries;
using Raven.Database.Server.WebApi.Attributes;
using Raven.Database.Storage;
using Raven.Json.Linq;

namespace Raven.Database.Server.Controllers
{
	public class IndexController : RavenDbApiController
	{
		[HttpGet]
		[Route("indexes")]
		[Route("databases/{databaseName}/indexes")]
		public HttpResponseMessage IndexesGet()
		{
			var namesOnlyString = GetQueryStringValue("namesOnly");
			bool namesOnly;
			RavenJArray indexes;
			if (bool.TryParse(namesOnlyString, out namesOnly) && namesOnly)
				indexes = Database.Indexes.GetIndexNames(GetStart(), GetPageSize(Database.Configuration.MaxPageSize));
			else
				indexes = Database.Indexes.GetIndexes(GetStart(), GetPageSize(Database.Configuration.MaxPageSize));

			return GetMessageWithObject(indexes);
		}

		[HttpGet]
		[Route("indexes/{*id}")]
		[Route("databases/{databaseName}/indexes/{*id}")]
		public HttpResponseMessage IndexGet(string id)
		{
            using (var cts = new CancellationTokenSource())
            using (cts.TimeoutAfter(DatabasesLandlord.SystemConfiguration.DatbaseOperationTimeout))
            {
                var index = id;
                if (string.IsNullOrEmpty(GetQueryStringValue("definition")) == false) 
                    return GetIndexDefinition(index);

                if (string.IsNullOrEmpty(GetQueryStringValue("source")) == false) 
                    return GetIndexSource(index);

                if (string.IsNullOrEmpty(GetQueryStringValue("debug")) == false) 
                    return DebugIndex(index);

                if (string.IsNullOrEmpty(GetQueryStringValue("explain")) == false) 
                    return GetExplanation(index);

                return GetIndexQueryResult(index, cts.Token);
            }
		}

		[HttpPut]
		[Route("indexes/{*id}")]
		[Route("databases/{databaseName}/indexes/{*id}")]
		public async Task<HttpResponseMessage> IndexPut(string id)
		{
			var index = id;
			var data = await ReadJsonObjectAsync<IndexDefinition>();
			if (data == null || (data.Map == null && (data.Maps == null || data.Maps.Count == 0)))
				return GetMessageWithString("Expected json document with 'Map' or 'Maps' property", HttpStatusCode.BadRequest);

			try
			{
				Database.Indexes.PutIndex(index, data);
				return GetMessageWithObject(new { Index = index }, HttpStatusCode.Created);
			}
			catch (Exception ex)
			{
				var compilationException = ex as IndexCompilationException;

				return GetMessageWithObject(new
				{
					ex.Message,
					IndexDefinitionProperty = compilationException != null ? compilationException.IndexDefinitionProperty : "",
					ProblematicText = compilationException != null ? compilationException.ProblematicText : "",
					Error = ex.ToString()
				}, HttpStatusCode.BadRequest);
			}
		}

		[HttpHead]
		[Route("indexes/{*id}")]
		[Route("databases/{databaseName}/indexes/{*id}")]
		public HttpResponseMessage IndexHead(string id)
		{
			var index = id;
			if (Database.IndexDefinitionStorage.IndexNames.Contains(index, StringComparer.OrdinalIgnoreCase) == false)
				return GetEmptyMessage(HttpStatusCode.NotFound);
			return GetEmptyMessage();
		}

		[HttpPost]
		[Route("indexes/{*id}")]
		[Route("databases/{databaseName}/indexes/{*id}")]
		public async Task<HttpResponseMessage >IndexPost(string id)
		{
			var index = id;
			if ("forceWriteToDisk".Equals(GetQueryStringValue("op"), StringComparison.InvariantCultureIgnoreCase))
			{
				Database.IndexStorage.ForceWriteToDisk(index);
				return GetEmptyMessage();
			}

			if ("lockModeChange".Equals(GetQueryStringValue("op"), StringComparison.InvariantCultureIgnoreCase))
				return HandleIndexLockModeChange(index);

			if ("true".Equals(GetQueryStringValue("postQuery"), StringComparison.InvariantCultureIgnoreCase))
			{
				var postedQuery = await ReadStringAsync();
				
				SetPostRequestQuery(postedQuery);

				return IndexGet(id);
			}

			return GetMessageWithString("Not idea how to handle a POST on " + index + " with op=" +
										(GetQueryStringValue("op") ?? "<no val specified>"));
		}

		[HttpReset]
		[Route("indexes/{*id}")]
		[Route("databases/{databaseName}/indexes/{*id}")]
		public HttpResponseMessage IndexReset(string id)
		{
			var index = id;
			Database.Indexes.ResetIndex(index);
			return GetMessageWithObject(new { Reset = index });
		}

		[HttpDelete]
		[Route("indexes/{*id}")]
		[Route("databases/{databaseName}/indexes/{*id}")]
		public HttpResponseMessage IndexDelete(string id)
		{
			var index = id;
			Database.Indexes.DeleteIndex(index);
			return GetEmptyMessage(HttpStatusCode.NoContent);
		}

		[HttpPost]
		[Route("indexes/set-priority/{*id}")]
		[Route("databases/{databaseName}/indexes/set-priority/{*id}")]
		public HttpResponseMessage SetPriority(string id)
		{
			var index = id;

			IndexingPriority indexingPriority;
			if (Enum.TryParse(GetQueryStringValue("priority"), out indexingPriority) == false)
			{
				return GetMessageWithObject(new
				{
					Error = "Could not parse priority value: " + GetQueryStringValue("priority")
				}, HttpStatusCode.BadRequest);
			}

			var instance = Database.IndexStorage.GetIndexInstance(index);
			Database.TransactionalStorage.Batch(accessor => accessor.Indexing.SetIndexPriority(instance.indexId, indexingPriority));

			return GetEmptyMessage();
		}

		private HttpResponseMessage GetIndexDefinition(string index)
		{
			var indexDefinition = Database.Indexes.GetIndexDefinition(index);
			if (indexDefinition == null)
				return GetEmptyMessage(HttpStatusCode.NotFound);

			indexDefinition.Fields = Database.Indexes.GetIndexFields(index);

			return GetMessageWithObject(new
			{
				Index = indexDefinition,
			});
		}

		private HttpResponseMessage GetIndexSource(string index)
		{
			var viewGenerator = Database.IndexDefinitionStorage.GetViewGenerator(index);
			if (viewGenerator == null)
				return GetEmptyMessage(HttpStatusCode.NotFound);

			return GetMessageWithObject(viewGenerator.SourceCode);
		}

		private HttpResponseMessage DebugIndex(string index)
		{
			switch (GetQueryStringValue("debug").ToLowerInvariant())
			{
				case "map":
					return GetIndexMappedResult(index);
				case "reduce":
					return GetIndexReducedResult(index);
				case "schedules":
					return GetIndexScheduledReduces(index);
				case "keys":
					return GetIndexKeysStats(index);
				case "entries":
					return GetIndexEntries(index);
				case "stats":
					return GetIndexStats(index);
				default:
					return GetMessageWithString("Unknown debug option " + GetQueryStringValue("debug"), HttpStatusCode.BadRequest);
			}
		}

		private HttpResponseMessage GetIndexMappedResult(string index)
		{
			var definition = Database.IndexDefinitionStorage.GetIndexDefinition(index);
			if (definition == null)
				return GetEmptyMessage(HttpStatusCode.NotFound);

			var key = GetQueryStringValue("key");
			if (string.IsNullOrEmpty(key))
			{
				List<string> keys = null;
				Database.TransactionalStorage.Batch(accessor =>
				{
					keys = accessor.MapReduce.GetKeysForIndexForDebug(definition.IndexId, GetStart(), GetPageSize(Database.Configuration.MaxPageSize))
						.ToList();
				});

				return GetMessageWithObject(new
				{
					Error = "Query string argument \'key\' is required",
					Keys = keys
				}, HttpStatusCode.BadRequest);
			}

			List<MappedResultInfo> mappedResult = null;
			Database.TransactionalStorage.Batch(accessor =>
			{
				mappedResult = accessor.MapReduce.GetMappedResultsForDebug(definition.IndexId, key, GetStart(), GetPageSize(Database.Configuration.MaxPageSize))
					.ToList();
			});
			return GetMessageWithObject(new
			{
				Error = "Query string argument \'key\' is required",
				Results = mappedResult
			}, HttpStatusCode.BadRequest);
		}

		private HttpResponseMessage GetExplanation(string index)
		{
			var dynamicIndex = index.StartsWith("dynamic/", StringComparison.OrdinalIgnoreCase) ||
							   index.Equals("dynamic", StringComparison.OrdinalIgnoreCase);

			if (dynamicIndex == false)
			{
				return GetMessageWithObject(new
				{
					Error = "Explain can only work on dynamic indexes"
				}, HttpStatusCode.BadRequest);
			}

			var indexQuery = GetIndexQuery(Database.Configuration.MaxPageSize);
			string entityName = null;
			if (index.StartsWith("dynamic/", StringComparison.OrdinalIgnoreCase))
				entityName = index.Substring("dynamic/".Length);

			var explanations = Database.ExplainDynamicIndexSelection(entityName, indexQuery);

			return GetMessageWithObject(explanations);
		}

		private HttpResponseMessage GetIndexQueryResult(string index, CancellationToken token)
		{
			Etag indexEtag;
			var msg = GetEmptyMessage();
			var queryResult = ExecuteQuery(index, out indexEtag, msg, token);

			if (queryResult == null)
				return msg;

			var includes = GetQueryStringValues("include") ?? new string[0];
			var loadedIds = new HashSet<string>(
				queryResult.Results
					.Where(x => x["@metadata"] != null)
					.Select(x => x["@metadata"].Value<string>("@id"))
					.Where(x => x != null)
				);
			var command = new AddIncludesCommand(Database, GetRequestTransaction(),
												 (etag, doc) => queryResult.Includes.Add(doc), includes, loadedIds);
			foreach (var result in queryResult.Results)
			{
				command.Execute(result);
			}
			command.AlsoInclude(queryResult.IdsToInclude);

			if (queryResult.NonAuthoritativeInformation)
				return GetEmptyMessage(HttpStatusCode.NonAuthoritativeInformation, indexEtag);
		    
			return GetMessageWithObject(queryResult, HttpStatusCode.OK, indexEtag);
		}

		private QueryResultWithIncludes ExecuteQuery(string index, out Etag indexEtag, HttpResponseMessage msg, CancellationToken token)
		{
			var indexQuery = GetIndexQuery(Database.Configuration.MaxPageSize);
			RewriteDateQueriesFromOldClients(indexQuery);

			var sp = Stopwatch.StartNew();
			var result = index.StartsWith("dynamic/", StringComparison.OrdinalIgnoreCase) || index.Equals("dynamic", StringComparison.OrdinalIgnoreCase) ?
				PerformQueryAgainstDynamicIndex(index, indexQuery, out indexEtag, msg, token) :
				PerformQueryAgainstExistingIndex(index, indexQuery, out indexEtag, msg, token);

			sp.Stop();
            Log.Debug(() =>
            {
                var sb = new StringBuilder();
                ReportQuery(sb, indexQuery, sp, result);
                return sb.ToString();
            });
			AddRequestTraceInfo(sb => ReportQuery(sb, indexQuery, sp, result));

			return result;
		}

	    private static void ReportQuery(StringBuilder sb, IndexQuery indexQuery, Stopwatch sp, QueryResultWithIncludes result)
	    {
	        sb.Append("\tQuery: ")
	            .Append(indexQuery.Query)
	            .AppendLine();
	        sb.Append("\t").AppendFormat("Time: {0:#,#;;0} ms", sp.ElapsedMilliseconds).AppendLine();

	        if (result == null)
	            return;

	        sb.Append("\tIndex: ")
	            .AppendLine(result.IndexName);
	        sb.Append("\t").AppendFormat("Results: {0:#,#;;0} returned out of {1:#,#;;0} total.", result.Results.Count, result.TotalResults).AppendLine();
	    }

	    private QueryResultWithIncludes PerformQueryAgainstExistingIndex(string index, IndexQuery indexQuery, out Etag indexEtag, HttpResponseMessage msg, CancellationToken token)
		{
			indexEtag = Database.Indexes.GetIndexEtag(index, null, indexQuery.ResultsTransformer);
			if (MatchEtag(indexEtag))
			{
				Database.IndexStorage.MarkCachedQuery(index);
				msg.StatusCode = HttpStatusCode.NotModified;
				return null;
			}

			var queryResult = Database.Queries.Query(index, indexQuery, token);
			indexEtag = Database.Indexes.GetIndexEtag(index, queryResult.ResultEtag, indexQuery.ResultsTransformer);
			return queryResult;
		}

		private QueryResultWithIncludes PerformQueryAgainstDynamicIndex(string index, IndexQuery indexQuery, out Etag indexEtag, HttpResponseMessage msg, CancellationToken token)
		{
			string entityName;
			var dynamicIndexName = GetDynamicIndexName(index, indexQuery, out entityName);

			if (dynamicIndexName != null && Database.IndexStorage.HasIndex(dynamicIndexName))
			{
				indexEtag = Database.Indexes.GetIndexEtag(dynamicIndexName, null, indexQuery.ResultsTransformer);
				if (MatchEtag(indexEtag))
				{
					Database.IndexStorage.MarkCachedQuery(dynamicIndexName);
					msg.StatusCode = HttpStatusCode.NotModified;
					return null;
				}
			}

			if (dynamicIndexName == null && // would have to create a dynamic index
				Database.Configuration.CreateAutoIndexesForAdHocQueriesIfNeeded == false) // but it is disabled
			{
				indexEtag = Etag.InvalidEtag;
				var explanations = Database.ExplainDynamicIndexSelection(entityName, indexQuery);

				msg.StatusCode = HttpStatusCode.BadRequest;
				
				var target = entityName == null ? "all documents" : entityName + " documents";

				msg.Content = JsonContent(RavenJToken.FromObject(
					new
					{
						Error =
							"Executing the query " + indexQuery.Query + " on " + target +
							" require creation of temporary index, and it has been explicitly disabled.",
						Explanations = explanations
					}));
				return null;
			}

			var queryResult = Database.ExecuteDynamicQuery(entityName, indexQuery, token);

			// have to check here because we might be getting the index etag just 
			// as we make a switch from temp to auto, and we need to refresh the etag
			// if that is the case. This can also happen when the optimizer
			// decided to switch indexes for a query.
			indexEtag = (dynamicIndexName == null || queryResult.IndexName == dynamicIndexName)
							? Database.Indexes.GetIndexEtag(queryResult.IndexName, queryResult.ResultEtag, indexQuery.ResultsTransformer)
							: Etag.InvalidEtag;

			return queryResult;
		}

		private string GetDynamicIndexName(string index, IndexQuery indexQuery, out string entityName)
		{
			entityName = null;
			if (index.StartsWith("dynamic/", StringComparison.OrdinalIgnoreCase))
				entityName = index.Substring("dynamic/".Length);

			var dynamicIndexName = Database.FindDynamicIndexName(entityName, indexQuery);
			return dynamicIndexName;
		}

		static Regex oldDateTimeFormat = new Regex(@"(\:|\[|{|TO\s) \s* (\d{17})", RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

		private void RewriteDateQueriesFromOldClients(IndexQuery indexQuery)
		{
			var clientVersion = GetQueryStringValue("Raven-Client-Version");
			if (string.IsNullOrEmpty(clientVersion) == false) // new client
				return;

			var matches = oldDateTimeFormat.Matches(indexQuery.Query);
			if (matches.Count == 0)
				return;
			var builder = new StringBuilder(indexQuery.Query);
			for (int i = matches.Count - 1; i >= 0; i--) // working in reverse so as to avoid invalidating previous indexes
			{
				var dateTimeString = matches[i].Groups[2].Value;

				DateTime time;
				if (DateTime.TryParseExact(dateTimeString, "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture, DateTimeStyles.None, out time) == false)
					continue;

				builder.Remove(matches[i].Groups[2].Index, matches[i].Groups[2].Length);
				var newDateTimeFormat = time.ToString(Default.DateTimeFormatsToWrite);
				builder.Insert(matches[i].Groups[2].Index, newDateTimeFormat);
			}
			indexQuery.Query = builder.ToString();
		}

		private HttpResponseMessage HandleIndexLockModeChange(string index)
		{
			var lockModeStr = GetQueryStringValue("mode");

			IndexLockMode indexLockMode;
			if (Enum.TryParse(lockModeStr, out indexLockMode) == false)
				return GetMessageWithString("Cannot understand index lock mode: " + lockModeStr, HttpStatusCode.BadRequest);

			var indexDefinition = Database.IndexDefinitionStorage.GetIndexDefinition(index);
			if (indexDefinition == null)
				return GetMessageWithString("Cannot find index : " + index, HttpStatusCode.NotFound);
			
			var definition = indexDefinition.Clone();
			definition.LockMode = indexLockMode;
			Database.IndexDefinitionStorage.UpdateIndexDefinitionWithoutUpdatingCompiledIndex(definition);

			return GetEmptyMessage();
		}

		private HttpResponseMessage GetIndexReducedResult(string index)
		{
			var definition = Database.IndexDefinitionStorage.GetIndexDefinition(index);
			if (definition == null)
				return GetEmptyMessage(HttpStatusCode.NotFound);
			
			var key = GetQueryStringValue("key");
			if (string.IsNullOrEmpty(key))
				return GetMessageWithString("Query string argument 'key' is required", HttpStatusCode.BadRequest);

			int level;
			if (int.TryParse(GetQueryStringValue("level"), out level) == false || (level != 1 && level != 2))
				return GetMessageWithString("Query string argument 'level' is required and must be 1 or 2",
					HttpStatusCode.BadRequest);

			List<MappedResultInfo> mappedResult = null;
			Database.TransactionalStorage.Batch(accessor =>
			{
				mappedResult = accessor.MapReduce.GetReducedResultsForDebug(definition.IndexId, key, level, GetStart(), GetPageSize(Database.Configuration.MaxPageSize))
					.ToList();
			});

			return GetMessageWithObject(new
			{
				mappedResult.Count,
				Results = mappedResult
			});
		}

		private HttpResponseMessage GetIndexScheduledReduces(string index)
		{
			List<ScheduledReductionDebugInfo> mappedResult = null;
			Database.TransactionalStorage.Batch(accessor =>
			{
				var instance = Database.IndexStorage.GetIndexInstance(index);
				mappedResult = accessor.MapReduce.GetScheduledReductionForDebug(instance.indexId, GetStart(), GetPageSize(Database.Configuration.MaxPageSize))
					.ToList();
			});

			return GetMessageWithObject(new
			{
				mappedResult.Count,
				Results = mappedResult
			});
		}

		private HttpResponseMessage GetIndexKeysStats(string index)
		{
			var definition = Database.IndexDefinitionStorage.GetIndexDefinition(index);
			if (definition == null)
			{
				return GetEmptyMessage(HttpStatusCode.NotFound);
			}

			List<ReduceKeyAndCount> keys = null;
			Database.TransactionalStorage.Batch(accessor =>
			{
				keys = accessor.MapReduce.GetKeysStats(definition.IndexId,
						 GetStart(),
						 GetPageSize(Database.Configuration.MaxPageSize))
					.ToList();
			});

			return GetMessageWithObject(new
			{
				keys.Count,
				Results = keys
			});
		}

		private HttpResponseMessage GetIndexEntries(string index)
		{
			var indexQuery = GetIndexQuery(Database.Configuration.MaxPageSize);
			var totalResults = new Reference<int>();

			var isDynamic = index.StartsWith("dynamic/", StringComparison.OrdinalIgnoreCase)
							|| index.Equals("dynamic", StringComparison.OrdinalIgnoreCase);

			if (isDynamic)
				return GetIndexEntriesForDynamicIndex(index, indexQuery, totalResults);

			return GetIndexEntriesForExistingIndex(index, indexQuery, totalResults);
		}

		private HttpResponseMessage GetIndexEntriesForDynamicIndex(string index, IndexQuery indexQuery, Reference<int> totalResults)
		{
			string entityName;
			var dynamicIndexName = GetDynamicIndexName(index, indexQuery, out entityName);

			if (dynamicIndexName == null)
				return GetEmptyMessage(HttpStatusCode.NotFound);

			return GetIndexEntriesForExistingIndex(dynamicIndexName, indexQuery, totalResults);
		}

		private HttpResponseMessage GetIndexEntriesForExistingIndex(string index, IndexQuery indexQuery, Reference<int> totalResults)
		{
			var results = Database
					.IndexStorage
					.IndexEntires(index, indexQuery, Database.IndexQueryTriggers, totalResults)
					.ToArray();

			Tuple<DateTime, Etag> indexTimestamp = null;
			bool isIndexStale = false;

			var definition = Database.IndexDefinitionStorage.GetIndexDefinition(index);

			Database.TransactionalStorage.Batch(
				accessor =>
				{
					isIndexStale = accessor.Staleness.IsIndexStale(definition.IndexId, indexQuery.Cutoff, indexQuery.CutoffEtag);
					if (isIndexStale == false && indexQuery.Cutoff == null && indexQuery.CutoffEtag == null)
					{
						var indexInstance = Database.IndexStorage.GetIndexInstance(index);
						isIndexStale = isIndexStale || (indexInstance != null && indexInstance.IsMapIndexingInProgress);
					}

					indexTimestamp = accessor.Staleness.IndexLastUpdatedAt(definition.IndexId);
				});
			var indexEtag = Database.Indexes.GetIndexEtag(index, null, indexQuery.ResultsTransformer);

			return GetMessageWithObject(
				new
				{
					Count = results.Length,
					Results = results,
					Includes = new string[0],
					IndexTimestamp = indexTimestamp.Item1,
					IndexEtag = indexTimestamp.Item2,
					TotalResults = totalResults.Value,
					SkippedResults = 0,
					NonAuthoritativeInformation = false,
					ResultEtag = indexEtag,
					IsStale = isIndexStale,
					IndexName = index,
					LastQueryTime = Database.IndexStorage.GetLastQueryTime(index)
				}, HttpStatusCode.OK, indexEtag);
		}

		private HttpResponseMessage GetIndexStats(string index)
		{
			IndexStats stats = null;
			var instance = Database.IndexStorage.GetIndexInstance(index);
			Database.TransactionalStorage.Batch(accessor =>
			{
				stats = accessor.Indexing.GetIndexStats(instance.indexId);
			});

			if (stats == null)
				return GetEmptyMessage(HttpStatusCode.NotFound);

			stats.LastQueryTimestamp = Database.IndexStorage.GetLastQueryTime(instance.indexId);
			stats.Performance = Database.IndexStorage.GetIndexingPerformance(instance.indexId);

			return GetMessageWithObject(stats);
		}
	}
}
