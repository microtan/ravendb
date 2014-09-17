﻿using System;
using System.Globalization;
using System.Linq;
using Raven.Abstractions.Data;
using Raven.Client.Shard;
using Raven.Json.Linq;

namespace Raven.Client.Document.Batches
{
	public class LazySuggestOperation : ILazyOperation
	{
		private readonly string index;
		private readonly SuggestionQuery suggestionQuery;

		public LazySuggestOperation(string index, SuggestionQuery suggestionQuery)
		{
			this.index = index;
			this.suggestionQuery = suggestionQuery;
		}

		public GetRequest CreateRequest()
		{
			var query = string.Format(
				"term={0}&field={1}&max={2}",
				suggestionQuery.Term,
				suggestionQuery.Field,
				suggestionQuery.MaxSuggestions);

			if (suggestionQuery.Accuracy.HasValue)
				query += "&accuracy=" + suggestionQuery.Accuracy.Value.ToString(CultureInfo.InvariantCulture);

			if (suggestionQuery.Distance.HasValue)
				query += "&distance=" + suggestionQuery.Distance;

			return new GetRequest
			{
				Url = "/suggest/" + index,
				Query = query
			};
		}

		public object Result { get; private set; }
		public QueryResult QueryResult { get; set; }
		public bool RequiresRetry { get; private set; }
		public void HandleResponse(GetResponse response)
		{
			if (response.Status != 200 && response.Status != 304)
			{
				throw new InvalidOperationException("Got an unexpected response code for the request: " + response.Status + "\r\n" +
													response.Result);
			}

			var result = (RavenJObject)response.Result;
			Result = new SuggestionQueryResult
			{
				Suggestions = ((RavenJArray)result["Suggestions"]).Select(x => x.Value<string>()).ToArray(),
			};
		}

		public void HandleResponses(GetResponse[] responses, ShardStrategy shardStrategy)
		{
			var result = new SuggestionQueryResult
			{
				Suggestions = (from item in responses
							   let data = (RavenJObject)item.Result
							   from suggestion in (RavenJArray)data["Suggestions"]
							   select suggestion.Value<string>())
							  .Distinct()
							  .ToArray()
			};

			Result = result;
		}

        public IDisposable EnterContext()
		{
			return null;
		}
	}
}