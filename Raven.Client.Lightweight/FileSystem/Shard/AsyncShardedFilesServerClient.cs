﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Raven.Abstractions.Data;
using Raven.Json.Linq;
using Raven.Abstractions.FileSystem;
using Raven.Abstractions.Extensions;

namespace Raven.Client.FileSystem.Shard
{
	public class AsyncShardedFilesServerClient
	{
		protected readonly ShardStrategy Strategy;
        protected readonly IDictionary<string, IAsyncFilesCommands> Clients;

		public AsyncShardedFilesServerClient(ShardStrategy strategy)
		{
			Strategy = strategy;
			Clients = strategy.Shards;
		}

		public int NumberOfShards
		{
			get { return Clients.Count; }
		}

		#region Sharding support methods

        public IList<Tuple<string, IAsyncFilesCommands>> GetShardsToOperateOn(ShardRequestData resultionData)
		{
			var shardIds = Strategy.ShardResolutionStrategy.PotentialShardsFor(resultionData);

            IEnumerable<KeyValuePair<string, IAsyncFilesCommands>> cmds = Clients;

			if (shardIds == null)
			{
				return cmds.Select(x => Tuple.Create(x.Key, x.Value)).ToList();
			}

            var list = new List<Tuple<string, IAsyncFilesCommands>>();
			foreach (var shardId in shardIds)
			{
                IAsyncFilesCommands value;
				if (Clients.TryGetValue(shardId, out value) == false)
					throw new InvalidOperationException("Could not find shard id: " + shardId);

				list.Add(Tuple.Create(shardId, value));

			}
			return list;
		}

        protected IList<IAsyncFilesCommands> GetCommandsToOperateOn(ShardRequestData resultionData)
		{
			return GetShardsToOperateOn(resultionData).Select(x => x.Item2).ToList();
		}

		#endregion

		public async Task<FileSystemStats> StatsAsync()
		{
			var applyAsync =
				await
				Strategy.ShardAccessStrategy.ApplyAsync(Clients.Values.ToList(), new ShardRequestData(),
															 (client, i) => client.GetStatisticsAsync());

		    var activeSyncs = new List<SynchronizationDetails>();

		    foreach (var active in applyAsync.Where(x => x.ActiveSyncs != null).Select(x => x.ActiveSyncs))
		    {
		        if(active.Count > 0)
                    activeSyncs.AddRange(active);
		    }

            var pendingSyncs = new List<SynchronizationDetails>();

            foreach (var pending in applyAsync.Where(x => x.PendingSyncs != null).Select(x => x.PendingSyncs))
            {
                if (pending.Count > 0)
                    pendingSyncs.AddRange(pending);
            }

		    var metrics = applyAsync.Where(x => x.Metrics != null).Select(x => x.Metrics).ToList();

		    return new FileSystemStats
		    {
		        FileCount = applyAsync.Sum(x => x.FileCount),
		        Name = string.Join(";", applyAsync.Select(x => x.Name)),
		        ActiveSyncs = activeSyncs,
		        PendingSyncs = pendingSyncs,
		        Metrics = new FileSystemMetrics()
		        {
		            FilesWritesPerSecond = metrics.Sum(x => x.FilesWritesPerSecond),
		            RequestsPerSecond = metrics.Sum(x => x.RequestsPerSecond),
		            Requests = new MeterData()
		            {
		                Count = metrics.Sum(x => x.Requests.Count),
		                FifteenMinuteRate = metrics.Average(x => x.Requests.FifteenMinuteRate),
		                FiveMinuteRate = metrics.Average(x => x.Requests.FiveMinuteRate),
		                MeanRate = metrics.Average(x => x.Requests.MeanRate),
		                OneMinuteRate = metrics.Average(x => x.Requests.OneMinuteRate),
		            },
		            RequestsDuration = new HistogramData()
		            {
		                Counter = metrics.Sum(x => x.RequestsDuration.Counter),
		                Max = metrics.Max(x => x.RequestsDuration.Max),
		                Mean = metrics.Average(x => x.RequestsDuration.Mean),
		                Min = metrics.Min(x => x.RequestsDuration.Min),
		                Stdev = metrics.Average(x => x.RequestsDuration.Stdev),
		                Percentiles = new Dictionary<string, double>
		                {
		                    {"50%", metrics.Average(x => x.RequestsDuration.Percentiles["50%"])},
		                    {"75%", metrics.Average(x => x.RequestsDuration.Percentiles["75%"])},
		                    {"95%", metrics.Average(x => x.RequestsDuration.Percentiles["95%"])},
		                    {"99%", metrics.Average(x => x.RequestsDuration.Percentiles["99%"])},
		                    {"99.9%", metrics.Average(x => x.RequestsDuration.Percentiles["99.9%"])},
		                    {"99.99%", metrics.Average(x => x.RequestsDuration.Percentiles["99.99%"])},
		                }
		            }
		        }
		    };
		}

		public Task DeleteAsync(string filename)
		{
			var client = TryGetClintFromFileName(filename);
			return client.DeleteAsync(filename);
		}

		public Task RenameAsync(string filename, string rename)
		{
			var client = TryGetClintFromFileName(filename);
			return client.RenameAsync(filename, rename);
		}

		public async Task<FileHeader[]> BrowseAsync(int pageSize = 25, ShardPagingInfo pagingInfo = null)
		{
			if (pagingInfo == null)
				pagingInfo = new ShardPagingInfo(Clients.Count);

			var indexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			if (indexes == null)
			{
				var lastPage = pagingInfo.GetLastPageNumber();
				if (pagingInfo.CurrentPage - lastPage > 10)
					throw new InvalidOperationException("Not Enough info in order to calculate requested page in a timely fation, last page info is for page #" + lastPage + ", please go to a closer page");

				var originalPage = pagingInfo.CurrentPage;
				pagingInfo.CurrentPage = lastPage;
				while (pagingInfo.CurrentPage < originalPage)
				{
					await BrowseAsync(pageSize, pagingInfo);
					pagingInfo.CurrentPage++;
				}

				indexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			}

			var results = new List<FileHeader>();

			var applyAsync = await Strategy.ShardAccessStrategy.ApplyAsync(Clients.Values.ToList(), 
                                                                                new ShardRequestData(),
															                    (client, i) => client.BrowseAsync(indexes[i], pageSize));
			var originalIndexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			while (results.Count < pageSize)
			{
				var item = GetSmallest(applyAsync, indexes, originalIndexes);
				if (item == null)
					break;

				results.Add(item);
			}

			pagingInfo.SetPagingInfo(indexes);
            
			return results.ToArray();
		}

		public async Task<string[]> GetSearchFieldsAsync(int pageSize = 25, ShardPagingInfo pagingInfo = null)
		{
			if (pagingInfo == null)
				pagingInfo = new ShardPagingInfo(Clients.Count);

			var indexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			if (indexes == null)
			{
				var lastPage = pagingInfo.GetLastPageNumber();
				if (pagingInfo.CurrentPage - lastPage > 10)
					throw new InvalidOperationException("Not Enough info in order to calculate requested page in a timely fation, last page info is for page #" + lastPage + ", please go to a closer page");

				var originalPage = pagingInfo.CurrentPage;
				pagingInfo.CurrentPage = lastPage;
				while (pagingInfo.CurrentPage < originalPage)
				{
					await GetSearchFieldsAsync(pageSize, pagingInfo);
					pagingInfo.CurrentPage++;
				}

				indexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			}

			var results = new List<string>();

			var applyAsync =
			   await
			   Strategy.ShardAccessStrategy.ApplyAsync(Clients.Values.ToList(), new ShardRequestData(),
															(client, i) => client.GetSearchFieldsAsync(indexes[i], pageSize));

			var originalIndexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			while (results.Count < pageSize)
			{
				var item = GetSmallest(applyAsync, indexes, originalIndexes);
				if (item == null)
					break;

				results.Add(item);
			}

			pagingInfo.SetPagingInfo(indexes);

			return results.ToArray();
		}

		public async Task<SearchResults> SearchAsync(string query, string[] sortFields = null, int pageSize = 25, ShardPagingInfo pagingInfo = null)
		{
			if (pagingInfo == null)
				pagingInfo = new ShardPagingInfo(Clients.Count);

			var indexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			if (indexes == null)
			{
				var lastPage = pagingInfo.GetLastPageNumber();
				if (pagingInfo.CurrentPage - lastPage > 10)
					throw new InvalidOperationException("Not Enough info in order to calculate requested page in a timely fation, last page info is for page #" + lastPage + ", please go to a closer page");

				var originalPage = pagingInfo.CurrentPage;
				pagingInfo.CurrentPage = lastPage;
				while (pagingInfo.CurrentPage < originalPage)
				{
					await SearchAsync(query, sortFields, pageSize, pagingInfo);
					pagingInfo.CurrentPage++;
				}

				indexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			}

			var result = new SearchResults();

			var applyAsync = await Strategy.ShardAccessStrategy.ApplyAsync(
                                                            Clients.Values.ToList(), new ShardRequestData(),
															(client, i) => client.SearchAsync(query, sortFields, indexes[i], pageSize));

			var originalIndexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			while (result.FileCount < pageSize)
			{
				var item = GetSmallest(applyAsync, indexes, originalIndexes, sortFields);
				if (item == null)
					break;

				var files = new List<FileHeader>();
				if (result.Files != null)
					files.AddRange(result.Files);
				if (item.Files != null)
					files.AddRange(item.Files);

				result.FileCount++;
                result.Files = files;
				result.PageSize = pageSize;
				result.Start = 0; //TODO: update start
			}

			pagingInfo.SetPagingInfo(indexes);

            result.Files = result.Files.Where(info => info != null).ToList();
			result.FileCount = result.Files.Count;
			return result;
		}

        public Task<RavenJObject> GetMetadataForAsync(string filename)
		{
			var client = TryGetClintFromFileName(filename);
			return client.GetMetadataForAsync(filename);
		}


        public async Task<Stream> DownloadAsync(string filename, Reference<RavenJObject> metadataRef = null, long? from = null, long? to = null)
        {
            var client = TryGetClintFromFileName(filename);
            return await client.DownloadAsync(filename, metadataRef, from, to);
        }

        public Task<string> UploadAsync(string filename, Stream source, long? size = null, Action<string, long> progress = null)
		{
            return UploadAsync(filename, new RavenJObject(), source, size, null);
		}

        public async Task<string> UploadAsync(string filename, RavenJObject metadata, Stream source, long? size = null, Action<string, long> progress = null)
		{
            var resolutionResult = Strategy.ShardResolutionStrategy.GetShardIdForUpload(filename, metadata);

            var client = TryGetClient(resolutionResult.ShardId);

            await client.UploadAsync(resolutionResult.NewFileName, source, metadata, size, progress);

            return resolutionResult.NewFileName;
		}

        public Task UpdateMetadataAsync(string filename, RavenJObject metadata)
		{
			var client = TryGetClintFromFileName(filename);

			return client.UpdateMetadataAsync(filename, metadata);
		}

		public async Task<string[]> GetFoldersAsync(string @from = null, int pageSize = 25, ShardPagingInfo pagingInfo = null)
		{
			if (pagingInfo == null)
				pagingInfo = new ShardPagingInfo(Clients.Count);

			var indexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			if (indexes == null)
			{
				var lastPage = pagingInfo.GetLastPageNumber();
				if (pagingInfo.CurrentPage - lastPage > 10)
					throw new InvalidOperationException("Not Enough info in order to calculate requested page in a timely fation, last page info is for page #" + lastPage + ", please go to a closer page");

				var originalPage = pagingInfo.CurrentPage;
				pagingInfo.CurrentPage = lastPage;
				while (pagingInfo.CurrentPage < originalPage)
				{
					await GetFoldersAsync(from, pageSize, pagingInfo);
					pagingInfo.CurrentPage++;
				}

				indexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			}

			var results = new List<string>();

			var applyAsync =
			   await
			   Strategy.ShardAccessStrategy.ApplyAsync(Clients.Values.ToList(), new ShardRequestData(),
															(client, i) => client.GetDirectoriesAsync(from, indexes[i], pageSize));

			var originalIndexes = pagingInfo.GetPagingInfo(pagingInfo.CurrentPage);
			while (results.Count < pageSize)
			{
				var item = GetSmallest(applyAsync, indexes, originalIndexes);
				if (item == null)
					break;

				results.Add(item);
			}

			pagingInfo.SetPagingInfo(indexes);

			return results.ToArray();
		}

		public Task<SearchResults> GetFilesAsync(string folder, FilesSortOptions options = FilesSortOptions.Default, string fileNameSearchPattern = "", int pageSize = 25, ShardPagingInfo pagingInfo = null)
		{
			var folderQueryPart = GetFolderQueryPart(folder);

			if (string.IsNullOrEmpty(fileNameSearchPattern) == false && fileNameSearchPattern.Contains("*") == false &&
				fileNameSearchPattern.Contains("?") == false)
			{
				fileNameSearchPattern = fileNameSearchPattern + "*";
			}
			var fileNameQueryPart = GetFileNameQueryPart(fileNameSearchPattern);

			return SearchAsync(folderQueryPart + fileNameQueryPart, GetSortFields(options), pageSize, pagingInfo);
		}

		#region private Methods
		private FileHeader GetSmallest(FileHeader[][] applyAsync, int[] indexes, int[] originalIndexes)
		{
			FileHeader smallest = null;
			var smallestIndex = -1;
			for (var i = 0; i < applyAsync.Length; i++)
			{

				var pos = indexes[i] - originalIndexes[i];
				if (pos >= applyAsync[i].Length)
					continue;

				var current = applyAsync[i][pos];
				if (smallest == null ||
                    string.Compare(current.FullPath, smallest.FullPath, StringComparison.InvariantCultureIgnoreCase) < 0)
				{
					smallest = current;
					smallestIndex = i;
				}
			}

			if (smallestIndex != -1)
				indexes[smallestIndex]++;

			return smallest;
		}

		private SearchResults GetSmallest(SearchResults[] searchResults, int[] indexes, int[] originalIndexes, string[] sortFields)
		{
			FileHeader smallest = null;
			var smallestIndex = -1;
			for (var i = 0; i < searchResults.Length; i++)
			{

				var pos = indexes[i] - originalIndexes[i];
				if (pos >= searchResults[i].FileCount)
					continue;

				var current = searchResults[i].Files[pos];
				if (smallest != null && CompareFileInfos(current, smallest, sortFields) >= 0)
					continue;

				smallest = current;
				smallestIndex = i;
			}

			if (smallestIndex != -1)
				indexes[smallestIndex]++;

			return new SearchResults
			{
				FileCount = 1,
				Files = new List<FileHeader> { smallest }
			};
		}

		private int CompareFileInfos(FileHeader current, FileHeader smallest, string[] sortFields)
		{
			if (sortFields == null || sortFields.Length == 0)
			{
                return string.Compare(current.FullPath, smallest.FullPath, StringComparison.InvariantCultureIgnoreCase);
			}
			foreach (var sortField in sortFields)
			{
				var field = sortField;
				var multiplay = 1; //for asending decending
				if (sortField.StartsWith("-"))
				{
					field = sortField.TrimStart(new[] { '-' });
					multiplay = -1;
				}

				if (field.Equals("__size", StringComparison.InvariantCultureIgnoreCase))
				{
					var currentItem = current.TotalSize;
					var smallestItem = smallest.TotalSize;

					if (currentItem == null && smallestItem == null)
						continue;

					if (currentItem == null)
						return 1 * multiplay;

					if (smallestItem == null)
						return -1 * multiplay;


					var compare = (long)(currentItem - smallestItem);
					if (compare != 0)
						return Math.Sign(compare) * multiplay;
				}
				else
				{
                    var currentItem = current.Metadata.Value<string>(field);
                    var smallestItem = smallest.Metadata.Value<string>(field);
                    
					var compare = string.Compare(currentItem, smallestItem, StringComparison.InvariantCultureIgnoreCase);
					if (compare != 0)
						return compare * multiplay;
				}
			}

			return 0;
		}

		private string GetSmallest(string[][] applyAsync, int[] indexes, int[] originalIndexes)
		{
			string smallest = null;
			var smallestIndex = -1;
			for (var i = 0; i < applyAsync.Length; i++)
			{

				var pos = indexes[i] - originalIndexes[i];
				if (pos >= applyAsync[i].Length)
					continue;

				var current = applyAsync[i][pos];
				if (smallest != null && string.Compare(current, smallest, StringComparison.InvariantCultureIgnoreCase) >= 0)
					continue;

				smallest = current;
				smallestIndex = i;
			}

			if (smallestIndex != -1)
				indexes[smallestIndex]++;

			return smallest;
		}

        private IAsyncFilesCommands TryGetClintFromFileName(string filename)
		{
			var clientId = Strategy.ShardResolutionStrategy.GetShardIdFromFileName(filename);
			var client = TryGetClient(clientId);
			return client;
		}

        private IAsyncFilesCommands TryGetClient(string clientId)
		{
			try
			{
				return Clients[clientId];
			}
			catch (Exception)
			{
				throw new FileNotFoundException("Count not find shard client with the id:" + clientId);
			}
		}

		private static string GetFolderQueryPart(string folder)
		{
			if (folder == null) throw new ArgumentNullException("folder");
			if (folder.StartsWith("/") == false)
				throw new ArgumentException("folder must starts with a /", "folder");

			int level;
			if (folder == "/")
				level = 1;
			else
				level = folder.Count(ch => ch == '/') + 1;

            var folderQueryPart = "__directoryName:" + folder + " AND __level:" + level;
			return folderQueryPart;
		}

		private static string GetFileNameQueryPart(string fileNameSearchPattern)
		{
			if (string.IsNullOrEmpty(fileNameSearchPattern))
				return "";

			if (fileNameSearchPattern.StartsWith("*") || (fileNameSearchPattern.StartsWith("?")))
				return " AND __rfileName:" + Reverse(fileNameSearchPattern);

			return " AND __fileName:" + fileNameSearchPattern;
		}

		private static string Reverse(string value)
		{
			var characters = value.ToCharArray();
			Array.Reverse(characters);

			return new string(characters);
		}

		private static string[] GetSortFields(FilesSortOptions options)
		{
			string sort = null;
			switch (options & ~FilesSortOptions.Desc)
			{
				case FilesSortOptions.Name:
					sort = "__key";
					break;
				case FilesSortOptions.Size:
					sort = "__size";
					break;
				case FilesSortOptions.LastModified:
					sort = "__modified";
					break;
			}

			if (options.HasFlag(FilesSortOptions.Desc))
			{
				if (string.IsNullOrEmpty(sort))
					throw new ArgumentException("options");
				sort = "-" + sort;
			}

			var sortFields = string.IsNullOrEmpty(sort) ? null : new[] { sort };
			return sortFields;
		}
		#endregion
	}
}
