using System;
using Raven.Json.Linq;

namespace Raven.Abstractions.Data
{
	public static class Constants
	{
		static Constants()
		{
			InDatabaseKeyVerificationDocumentContents = new RavenJObject
			{
				{"Text", "The encryption is correct."}
			};
			InDatabaseKeyVerificationDocumentContents.EnsureCannotBeChangeAndEnableSnapshotting();
		}

		public const string RavenClientPrimaryServerUrl = "Raven-Client-Primary-Server-Url";
		public const string RavenClientPrimaryServerLastCheck = "Raven-Client-Primary-Server-LastCheck";
		public const string RavenForcePrimaryServerCheck = "Raven-Force-Primary-Server-Check";

		public const string RavenShardId = "Raven-Shard-Id";
		public const string RavenAuthenticatedUser = "Raven-Authenticated-User";
		public const string LastModified = "Last-Modified";
        public const string CreationDate = "Creation-Date";
		public const string RavenLastModified = "Raven-Last-Modified";
		public const string SystemDatabase = "<system>";
		public const string TemporaryScoreValue = "Temp-Index-Score";
		public const string RandomFieldName = "__random";
		public const string NullValueNotAnalyzed = "[[NULL_VALUE]]";
		public const string EmptyStringNotAnalyzed = "[[EMPTY_STRING]]";
		public const string NullValue = "NULL_VALUE";
		public const string EmptyString = "EMPTY_STRING";
		public const string DocumentIdFieldName = "__document_id";
		public const string ReduceKeyFieldName = "__reduce_key";
		public const string ReduceValueFieldName = "__reduced_val";
		public const string IntersectSeparator = " INTERSECT ";
		public const string RavenClrType = "Raven-Clr-Type";
		public const string RavenEntityName = "Raven-Entity-Name";
		public const string RavenReadOnly = "Raven-Read-Only";
		public const string AllFields = "__all_fields";
		// This is used to indicate that a document exists in an uncommitted transaction
		public const string RavenDocumentDoesNotExists = "Raven-Document-Does-Not-Exists";
		public const string Metadata = "@metadata";
		public const string NotForReplication = "Raven-Not-For-Replication";
		public const string RavenDeleteMarker = "Raven-Delete-Marker";
		public const string ActiveBundles = "Raven/ActiveBundles";
		public const string AllowBundlesChange = "Raven-Temp-Allow-Bundles-Change";
		public const string RavenAlerts = "Raven/Alerts";
		public const string RavenJavascriptFunctions = "Raven/Javascript/Functions";

		public const string MemoryLimitForProcessing_BackwardCompatibility = "Raven/MemoryLimitForIndexing";
		public const string MemoryLimitForProcessing = "Raven/MemoryLimitForProcessing";

		// Server
		public const string MaxConcurrentServerRequests = "Raven/MaxConcurrentServerRequests";
		public const string MaxConcurrentMultiGetRequests = "Raven/MaxConcurrentMultiGetRequests";

		// Indexing
		public const string RavenPrefetchingDurationLimit = "Raven/Prefetching/DurationLimit";
		public const int DefaultPrefetchingDurationLimit = 5000;
		public const string BulkImportBatchTimeout = "Raven/BulkImport/BatchTimeout";
		public const int BulkImportDefaultTimeoutInMs = 60000;

		//Paths
		public const string RavenDataDir = "Raven/DataDir";
		public const string RavenLogsPath = "Raven/Esent/LogsPath";
        public const string RavenTxJournalPath = "Raven/TransactionJouranlsPath";
		public const string RavenIndexPath = "Raven/IndexStoragePath";

		//Files
		public const int WindowsMaxPath = 260 - 30;
		public const int LinuxMaxPath = 4096;
		public const int LinuxMaxFileNameLength = WindowsMaxPath;
		public static readonly string[] WindowsReservedFileNames = { "con", "prn", "aux", "nul", "com1", "com2","com3", "com4", "com5", "com6", "com7", "com8", "com9",
																		"lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9", "clock$" };

		//Encryption
		public const string DontEncryptDocumentsStartingWith = "Raven/";
		public const string AlgorithmTypeSetting = "Raven/Encryption/Algorithm";
		public const string EncryptionKeySetting = "Raven/Encryption/Key";
		public const string EncryptionKeyBitsPreferenceSetting = "Raven/Encryption/KeyBitsPreference";
		public const string EncryptIndexes = "Raven/Encryption/EncryptIndexes";

		public const string InDatabaseKeyVerificationDocumentName = "Raven/Encryption/Verification";
		public static readonly RavenJObject InDatabaseKeyVerificationDocumentContents;

		public const int DefaultGeneratedEncryptionKeyLength = 256/8;
		public const int MinimumAcceptableEncryptionKeyLength = 64/8;

		public const int DefaultKeySizeToUseInActualEncryptionInBits = 128;
		public const int Rfc2898Iterations = 1000;

		public const int DefaultIndexFileBlockSize = 12*1024;

		public static readonly Type DefaultCryptoServiceProvider = typeof(System.Security.Cryptography.AesCryptoServiceProvider);

		//Quotas
		public const string DocsHardLimit = "Raven/Quotas/Documents/HardLimit";
		public const string DocsSoftLimit = "Raven/Quotas/Documents/SoftLimit";
		public const string SizeHardLimitInKB = "Raven/Quotas/Size/HardLimitInKB";
		public const string SizeSoftLimitInKB = "Raven/Quotas/Size/SoftMarginInKB";

		//Replications
		public const string RavenReplicationSource = "Raven-Replication-Source";
		public const string RavenReplicationVersion = "Raven-Replication-Version";
		public const string RavenReplicationHistory = "Raven-Replication-History";
		public const string RavenReplicationConflict = "Raven-Replication-Conflict";
		public const string RavenReplicationConflictDocument = "Raven-Replication-Conflict-Document";
		public const string RavenReplicationSourcesBasePath = "Raven/Replication/Sources";
		public const string RavenReplicationDestinations = "Raven/Replication/Destinations";
		public const string RavenReplicationDestinationsBasePath = "Raven/Replication/Destinations/";
		public const string RavenReplicationConfig = "Raven/Replication/Config";

		public const string RavenReplicationDocsTombstones = "Raven/Replication/Docs/Tombstones";

        [Obsolete("Use RavenFS instead.")]
		public const string RavenReplicationAttachmentsTombstones = "Raven/Replication/Attachments/Tombstones";

        //Periodic export
		public const string RavenPeriodicExportsDocsTombstones = "Raven/PeriodicExports/Docs/Tombstones";

        [Obsolete("Use RavenFS instead.")]
		public const string RavenPeriodicExportsAttachmentsTombstones = "Raven/PeriodicExports/Attachments/Tombstones";

		public const int ChangeHistoryLength = 50;

		//Spatial
		public const string DefaultSpatialFieldName = "__spatial";
		public const string SpatialShapeFieldName = "__spatialShape";
		public const double DefaultSpatialDistanceErrorPct = 0.025d;
		public const string DistanceFieldName = "__distance";
		/// <summary>
		/// The International Union of Geodesy and Geophysics says the Earth's mean radius in KM is:
		///
		/// [1] http://en.wikipedia.org/wiki/Earth_radius
		/// </summary>
		public const double EarthMeanRadiusKm = 6371.0087714;
		public const double MilesToKm = 1.60934;
		
		//Versioning
		public const string RavenCreateVersion = "Raven-Create-Version";

		public const string RavenClientVersion = "Raven-Client-Version";
        public const string RavenDefaultQueryTimeout = "Raven_Default_Query_Timeout";
		public const string NextPageStart = "Next-Page-Start";

#if DEBUG
		public const int EnterLockTimeout = 10000;
#endif
		/// <summary>
		/// if no encoding information in headers of incoming request, this encoding is assumed
		/// </summary>
		public const string DefaultRequestEncoding = "UTF-8";

	    public const string AssembliesDirectoryName = "Assemblies";

		public const string DocumentsByEntityNameIndex = "Raven/DocumentsByEntityName";
		
		//Counters
		public const byte GroupSeperator = 29;
		public const char GroupSeperatorChar = (char)GroupSeperator;
		public const string GroupSeperatorString = "\u001D";

        public const string MetadataEtagField = "ETag";

		public const string TempUploadsDirectoryName = "RavenTempUploads";

		public const string DataCouldNotBeDecrypted = "<data could not be decrypted>";

	}
}