﻿// -----------------------------------------------------------------------
//  <copyright file="StronglyTypedRavenSettings.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.Caching;
using Raven.Abstractions.Data;
using Raven.Database.Config.Settings;

namespace Raven.Database.Config
{
	public class StronglyTypedRavenSettings
	{
		private readonly NameValueCollection settings;

		public ReplicationConfiguration Replication { get; private set; }

		public VoronConfiguration Voron { get; private set; }

		public PrefetcherConfiguration Prefetcher { get; private set; }

        public FileSystemConfiguration FileSystem { get; private set; }

		public EncryptionConfiguration Encryption { get; private set; }

		public StronglyTypedRavenSettings(NameValueCollection settings)
		{
			Replication = new ReplicationConfiguration();
			Voron = new VoronConfiguration();
			Prefetcher = new PrefetcherConfiguration();
            FileSystem = new FileSystemConfiguration();
			Encryption = new EncryptionConfiguration();
			
			this.settings = settings;
		}

		public void Setup(int defaultMaxNumberOfItemsToIndexInSingleBatch, int defaultInitialNumberOfItemsToIndexInSingleBatch)
		{

			PrefetchingDurationLimit = new IntegerSetting(settings[Constants.RavenPrefetchingDurationLimit], Constants.DefaultPrefetchingDurationLimit);

            BulkImportBatchTimeout = new TimeSpanSetting(settings[Constants.BulkImportBatchTimeout], TimeSpan.FromMilliseconds(Constants.BulkImportDefaultTimeoutInMs), TimeSpanArgumentType.FromParse);

			MaxConcurrentServerRequests = new IntegerSetting(settings[Constants.MaxConcurrentServerRequests], 512);

			MaxConcurrentMultiGetRequests = new IntegerSetting(settings[Constants.MaxConcurrentMultiGetRequests], 192);

			MemoryLimitForProcessing = new IntegerSetting(settings[Constants.MemoryLimitForProcessing] ?? settings[Constants.MemoryLimitForProcessing_BackwardCompatibility],
                // we allow 1 GB by default, or up to 75% of available memory on startup, if less than that is available
                Math.Min(1024, (int)(MemoryStatistics.AvailableMemory * 0.75))); 

			MaxPageSize =
				new IntegerSettingWithMin(settings["Raven/MaxPageSize"], 1024, 10);
			MemoryCacheLimitMegabytes =
				new IntegerSetting(settings["Raven/MemoryCacheLimitMegabytes"], GetDefaultMemoryCacheLimitMegabytes);
			MemoryCacheExpiration =
				new TimeSpanSetting(settings["Raven/MemoryCacheExpiration"], TimeSpan.FromMinutes(60),
				                    TimeSpanArgumentType.FromSeconds);
			MemoryCacheLimitPercentage =
				new IntegerSetting(settings["Raven/MemoryCacheLimitPercentage"], 0 /* auto size */);
			MemoryCacheLimitCheckInterval =
				new TimeSpanSetting(settings["Raven/MemoryCacheLimitCheckInterval"], MemoryCache.Default.PollingInterval,
				                    TimeSpanArgumentType.FromParse);

            PrewarmFacetsSyncronousWaitTime =
                new TimeSpanSetting(settings["Raven/PrewarmFacetsSyncronousWaitTime"], TimeSpan.FromSeconds(3),
                                    TimeSpanArgumentType.FromParse);

            PrewarmFacetsOnIndexingMaxAge =
                new TimeSpanSetting(settings["Raven/PrewarmFacetsOnIndexingMaxAge"], TimeSpan.FromMinutes(10),
                                    TimeSpanArgumentType.FromParse);
			
			
			MaxProcessingRunLatency =
				new TimeSpanSetting(settings["Raven/MaxProcessingRunLatency"] ?? settings["Raven/MaxIndexingRunLatency"], TimeSpan.FromMinutes(5),
				                    TimeSpanArgumentType.FromParse);
			MaxIndexWritesBeforeRecreate =
				new IntegerSetting(settings["Raven/MaxIndexWritesBeforeRecreate"], 256 * 1024);
			MaxIndexOutputsPerDocument = 
				new IntegerSetting(settings["Raven/MaxIndexOutputsPerDocument"], 15);

			MaxNumberOfItemsToProcessInSingleBatch =
				new IntegerSettingWithMin(settings["Raven/MaxNumberOfItemsToProcessInSingleBatch"] ?? settings["Raven/MaxNumberOfItemsToIndexInSingleBatch"],
				                          defaultMaxNumberOfItemsToIndexInSingleBatch, 128);
			AvailableMemoryForRaisingBatchSizeLimit =
				new IntegerSetting(settings["Raven/AvailableMemoryForRaisingBatchSizeLimit"] ?? settings["Raven/AvailableMemoryForRaisingIndexBatchSizeLimit"],
				                   Math.Min(768, MemoryStatistics.TotalPhysicalMemory/2));
			MaxNumberOfItemsToReduceInSingleBatch =
				new IntegerSettingWithMin(settings["Raven/MaxNumberOfItemsToReduceInSingleBatch"],
				                          defaultMaxNumberOfItemsToIndexInSingleBatch/2, 128);
			NumberOfItemsToExecuteReduceInSingleStep =
				new IntegerSetting(settings["Raven/NumberOfItemsToExecuteReduceInSingleStep"], 1024);
			MaxNumberOfParallelProcessingTasks =
				new IntegerSettingWithMin(settings["Raven/MaxNumberOfParallelProcessingTasks"] ?? settings["Raven/MaxNumberOfParallelIndexTasks"], Environment.ProcessorCount, 1);

			NewIndexInMemoryMaxMb =
				new MultipliedIntegerSetting(new IntegerSettingWithMin(settings["Raven/NewIndexInMemoryMaxMB"], 64, 1), 1024*1024);
			RunInMemory =
				new BooleanSetting(settings["Raven/RunInMemory"], false);
			CreateAutoIndexesForAdHocQueriesIfNeeded =
				new BooleanSetting(settings["Raven/CreateAutoIndexesForAdHocQueriesIfNeeded"], true);
			ResetIndexOnUncleanShutdown =
				new BooleanSetting(settings["Raven/ResetIndexOnUncleanShutdown"], false);
			DisableInMemoryIndexing =
				new BooleanSetting(settings["Raven/DisableInMemoryIndexing"], false);
			DataDir =
				new StringSetting(settings["Raven/DataDir"], @"~\Data");
			IndexStoragePath =
				new StringSetting(settings["Raven/IndexStoragePath"], (string)null);
			CountersDataDir =
				new StringSetting(settings["Raven/Counters/DataDir"], @"~\Data\Counters");
			
			HostName =
				new StringSetting(settings["Raven/HostName"], (string) null);
			Port =
				new StringSetting(settings["Raven/Port"], "*");
			HttpCompression =
				new BooleanSetting(settings["Raven/HttpCompression"], true);
			AccessControlAllowOrigin =
				new StringSetting(settings["Raven/AccessControlAllowOrigin"], (string) null);
			AccessControlMaxAge =
				new StringSetting(settings["Raven/AccessControlMaxAge"], "1728000" /* 20 days */);
			AccessControlAllowMethods =
				new StringSetting(settings["Raven/AccessControlAllowMethods"], "PUT,PATCH,GET,DELETE,POST");
			AccessControlRequestHeaders =
				new StringSetting(settings["Raven/AccessControlRequestHeaders"], (string) null);
			RedirectStudioUrl =
				new StringSetting(settings["Raven/RedirectStudioUrl"], (string) null);
			DisableDocumentPreFetching =
				new BooleanSetting(settings["Raven/DisableDocumentPreFetching"] ?? settings["Raven/DisableDocumentPreFetchingForIndexing"], false);
			MaxNumberOfItemsToPreFetch =
				new IntegerSettingWithMin(settings["Raven/MaxNumberOfItemsToPreFetch"]?? settings["Raven/MaxNumberOfItemsToPreFetchForIndexing"],
										  defaultMaxNumberOfItemsToIndexInSingleBatch, 128);
			WebDir =
				new StringSetting(settings["Raven/WebDir"], GetDefaultWebDir);
			PluginsDirectory =
				new StringSetting(settings["Raven/PluginsDirectory"], @"~\Plugins");
			CompiledIndexCacheDirectory =
				new StringSetting(settings["Raven/CompiledIndexCacheDirectory"], @"~\Raven\CompiledIndexCache");
			TaskScheduler =
				new StringSetting(settings["Raven/TaskScheduler"], (string) null);
			AllowLocalAccessWithoutAuthorization =
				new BooleanSetting(settings["Raven/AllowLocalAccessWithoutAuthorization"], false);
			MaxIndexCommitPointStoreTimeInterval =
				new TimeSpanSetting(settings["Raven/MaxIndexCommitPointStoreTimeInterval"], TimeSpan.FromMinutes(5),
				                    TimeSpanArgumentType.FromParse);
			MaxNumberOfStoredCommitPoints =
				new IntegerSetting(settings["Raven/MaxNumberOfStoredCommitPoints"], 5);
			MinIndexingTimeIntervalToStoreCommitPoint =
				new TimeSpanSetting(settings["Raven/MinIndexingTimeIntervalToStoreCommitPoint"], TimeSpan.FromMinutes(1),
				                    TimeSpanArgumentType.FromParse);
            
			TimeToWaitBeforeRunningIdleIndexes = new TimeSpanSetting(settings["Raven/TimeToWaitBeforeRunningIdleIndexes"], TimeSpan.FromMinutes(10), TimeSpanArgumentType.FromParse);

			DatbaseOperationTimeout = new TimeSpanSetting(settings["Raven/DatabaseOperationTimeout"], TimeSpan.FromMinutes(5), TimeSpanArgumentType.FromParse);
            
			TimeToWaitBeforeMarkingAutoIndexAsIdle = new TimeSpanSetting(settings["Raven/TimeToWaitBeforeMarkingAutoIndexAsIdle"], TimeSpan.FromHours(1), TimeSpanArgumentType.FromParse);

			TimeToWaitBeforeMarkingIdleIndexAsAbandoned = new TimeSpanSetting(settings["Raven/TimeToWaitBeforeMarkingIdleIndexAsAbandoned"], TimeSpan.FromHours(72), TimeSpanArgumentType.FromParse);

			TimeToWaitBeforeRunningAbandonedIndexes = new TimeSpanSetting(settings["Raven/TimeToWaitBeforeRunningAbandonedIndexes"], TimeSpan.FromHours(3), TimeSpanArgumentType.FromParse);

			DisableClusterDiscovery = new BooleanSetting(settings["Raven/DisableClusterDiscovery"], false);

			ServerName = new StringSetting(settings["Raven/ServerName"], (string)null);

			MaxStepsForScript = new IntegerSetting(settings["Raven/MaxStepsForScript"], 10*1000);
			AdditionalStepsForScriptBasedOnDocumentSize = new IntegerSetting(settings["Raven/AdditionalStepsForScriptBasedOnDocumentSize"], 5);

			MaxRecentTouchesToRemember = new IntegerSetting(settings["Raven/MaxRecentTouchesToRemember"], 1024);

			Prefetcher.FetchingDocumentsFromDiskTimeoutInSeconds = new IntegerSetting(settings["Raven/Prefetcher/FetchingDocumentsFromDiskTimeout"], 5);
			Prefetcher.MaximumSizeAllowedToFetchFromStorageInMb = new IntegerSetting(settings["Raven/Prefetcher/MaximumSizeAllowedToFetchFromStorage"], 256);

            Voron.MaxBufferPoolSize = new IntegerSetting(settings["Raven/Voron/MaxBufferPoolSize"], 4);
			Voron.InitialFileSize = new NullableIntegerSetting(settings["Raven/Voron/InitialFileSize"], (int?)null);
			Voron.MaxScratchBufferSize = new IntegerSetting(settings["Raven/Voron/MaxScratchBufferSize"], 512);
			Voron.AllowIncrementalBackups = new BooleanSetting(settings["Raven/Voron/AllowIncrementalBackups"], false);
			Voron.TempPath = new StringSetting(settings["Raven/Voron/TempPath"], (string) null);

			Replication.FetchingFromDiskTimeoutInSeconds = new IntegerSetting(settings["Raven/Replication/FetchingFromDiskTimeout"], 30);

            FileSystem.MaximumSynchronizationInterval = new TimeSpanSetting(settings["Raven/FileSystem/MaximumSynchronizationInterval"], TimeSpan.FromSeconds(60), TimeSpanArgumentType.FromParse);
			FileSystem.IndexStoragePath = new StringSetting(settings["Raven/FileSystem/IndexStoragePath"], (string)null);
			FileSystem.DataDir = new StringSetting(settings["Raven/FileSystem/DataDir"], @"~\Data\FileSystem");
			FileSystem.DefaultStorageTypeName = new StringSetting(settings["Raven/FileSystem/Storage"], InMemoryRavenConfiguration.VoronTypeName);

			Encryption.UseFips = new BooleanSetting(settings["Raven/Encryption/FIPS"], false);
			Encryption.EncryptionKeyBitsPreference = new IntegerSetting(settings[Constants.EncryptionKeyBitsPreferenceSetting], Constants.DefaultKeySizeToUseInActualEncryptionInBits);
			Encryption.UseSsl = new BooleanSetting(settings["Raven/UseSsl"], false);

			DefaultStorageTypeName = new StringSetting(settings["Raven/StorageTypeName"] ?? settings["Raven/StorageEngine"], InMemoryRavenConfiguration.VoronTypeName);

			FlushIndexToDiskSizeInMb = new IntegerSetting(settings["Raven/Indexing/FlushIndexToDiskSizeInMb"], 5);
			JournalsStoragePath = new StringSetting(settings["Raven/Esent/LogsPath"] ?? settings[Constants.RavenTxJournalPath], (string)null);
		}

		private string GetDefaultWebDir()
		{
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Raven/WebUI");
		}

		private int GetDefaultMemoryCacheLimitMegabytes()
		{
			var cacheSizeMaxSetting = new IntegerSetting(settings["Raven/Esent/CacheSizeMax"], 1024);

			// we need to leave ( a lot ) of room for other things as well, so we min the cache size
			var val = (MemoryStatistics.TotalPhysicalMemory/2) -
			          // reduce the unmanaged cache size from the default min
									cacheSizeMaxSetting.Value;

			if (val < 0)
				return 128; // if machine has less than 1024 MB, then only use 128 MB 

			return val;
		}

		public IntegerSetting MemoryLimitForProcessing { get; private set; }

		public IntegerSetting MaxConcurrentServerRequests { get; private set; }

		public IntegerSetting MaxConcurrentMultiGetRequests { get; private set; }

		public IntegerSetting PrefetchingDurationLimit { get; private set; }

		public TimeSpanSetting BulkImportBatchTimeout { get; private set; }

		public IntegerSettingWithMin MaxPageSize { get; private set; }

		public IntegerSetting MemoryCacheLimitMegabytes { get; private set; }

		public TimeSpanSetting MemoryCacheExpiration { get; private set; }

		public IntegerSetting MemoryCacheLimitPercentage { get; private set; }

		public TimeSpanSetting MemoryCacheLimitCheckInterval { get; private set; }

		public TimeSpanSetting MaxProcessingRunLatency { get; private set; }

        public TimeSpanSetting PrewarmFacetsOnIndexingMaxAge { get; private set; }

        public TimeSpanSetting PrewarmFacetsSyncronousWaitTime { get; private set; }

        public IntegerSettingWithMin MaxNumberOfItemsToProcessInSingleBatch { get; private set; }

		public IntegerSetting AvailableMemoryForRaisingBatchSizeLimit { get; private set; }

		public IntegerSettingWithMin MaxNumberOfItemsToReduceInSingleBatch { get; private set; }

		public IntegerSetting NumberOfItemsToExecuteReduceInSingleStep { get; private set; }

		public IntegerSettingWithMin MaxNumberOfParallelProcessingTasks { get; private set; }

		public MultipliedIntegerSetting NewIndexInMemoryMaxMb { get; private set; }

		public BooleanSetting RunInMemory { get; private set; }

		public BooleanSetting CreateAutoIndexesForAdHocQueriesIfNeeded { get; private set; }

		public BooleanSetting ResetIndexOnUncleanShutdown { get; private set; }

		public BooleanSetting DisableInMemoryIndexing { get; private set; }

		public StringSetting DataDir { get; private set; }

		public StringSetting IndexStoragePath { get; private set; }

		public StringSetting CountersDataDir { get; private set; }
		
		public StringSetting HostName { get; private set; }

		public StringSetting Port { get; private set; }

		public BooleanSetting HttpCompression { get; private set; }

		public StringSetting AccessControlAllowOrigin { get; private set; }

		public StringSetting AccessControlMaxAge { get; private set; }

		public StringSetting AccessControlAllowMethods { get; private set; }

		public StringSetting AccessControlRequestHeaders { get; private set; }

		public StringSetting RedirectStudioUrl { get; private set; }

		public BooleanSetting DisableDocumentPreFetching { get; private set; }

		public IntegerSettingWithMin MaxNumberOfItemsToPreFetch { get; private set; }

		public StringSetting WebDir { get; private set; }

		public BooleanSetting DisableClusterDiscovery { get; private set; }

		public StringSetting ServerName { get; private set; }

		public StringSetting PluginsDirectory { get; private set; }

		public StringSetting CompiledIndexCacheDirectory { get; private set; }

		public StringSetting TaskScheduler { get; private set; }

		public BooleanSetting AllowLocalAccessWithoutAuthorization { get; private set; }

		public TimeSpanSetting MaxIndexCommitPointStoreTimeInterval { get; private set; }

		public TimeSpanSetting MinIndexingTimeIntervalToStoreCommitPoint { get; private set; }

		public IntegerSetting MaxNumberOfStoredCommitPoints { get; private set; }
        public TimeSpanSetting TimeToWaitBeforeRunningIdleIndexes { get; private set; }

	    public TimeSpanSetting TimeToWaitBeforeMarkingAutoIndexAsIdle { get; private set; }

		public TimeSpanSetting TimeToWaitBeforeMarkingIdleIndexAsAbandoned { get; private set; }

		public TimeSpanSetting TimeToWaitBeforeRunningAbandonedIndexes { get; private set; }
		
		public IntegerSetting MaxStepsForScript { get; private set; }

		public IntegerSetting AdditionalStepsForScriptBasedOnDocumentSize { get; private set; }

		public IntegerSetting MaxIndexWritesBeforeRecreate { get; private set; }

		public IntegerSetting MaxIndexOutputsPerDocument { get; private set; }
    
		public TimeSpanSetting DatbaseOperationTimeout { get; private set; }

		public IntegerSetting MaxRecentTouchesToRemember { get; private set; }

		public StringSetting DefaultStorageTypeName { get; private set; }

		public IntegerSetting FlushIndexToDiskSizeInMb { get; set; }

		public StringSetting JournalsStoragePath { get; private set; }

		public class VoronConfiguration
		{
			public IntegerSetting MaxBufferPoolSize { get; set; }

			public NullableIntegerSetting InitialFileSize { get; set; }

			public IntegerSetting MaxScratchBufferSize { get; set; }

			public BooleanSetting AllowIncrementalBackups { get; set; }

			public StringSetting TempPath { get; set; }
		}

		public class PrefetcherConfiguration
		{
		public IntegerSetting FetchingDocumentsFromDiskTimeoutInSeconds { get; set; }

			public IntegerSetting MaximumSizeAllowedToFetchFromStorageInMb { get; set; }
	}

		public class ReplicationConfiguration
		{
			public IntegerSetting FetchingFromDiskTimeoutInSeconds { get; set; }
}

        public class FileSystemConfiguration
        {
            public TimeSpanSetting MaximumSynchronizationInterval { get; set; }

			public StringSetting DataDir { get; set; }

			public StringSetting IndexStoragePath { get; set; }

			public StringSetting DefaultStorageTypeName { get; set; }
        }

		public class EncryptionConfiguration
		{
			public BooleanSetting UseFips { get; set; }

			public IntegerSetting EncryptionKeyBitsPreference { get; set; }

			public BooleanSetting UseSsl { get; set; }
		}
	}

	
}
