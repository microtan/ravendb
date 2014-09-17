﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Raven.Database.Server.RavenFS.Extensions;
using Raven.Database.Server.RavenFS.Storage;
using Raven.Database.Server.RavenFS.Util;
using RavenFS.Tests.Synchronization;
using RavenFS.Tests.Synchronization.IO;
using Xunit;
using Raven.Json.Linq;
using Raven.Abstractions.FileSystem;

namespace RavenFS.Tests
{
    public class StorageOperationsTests : RavenFsTestBase
	{
		[Fact]
		public async Task Can_force_storage_cleanup_from_client()
		{
			var client = NewAsyncClient();
			await client.UploadAsync("toDelete.bin", new MemoryStream(new byte[] {1, 2, 3, 4, 5}));

			await client.DeleteAsync("toDelete.bin");

			await client.Storage.CleanUpAsync();

			var configNames = await client.Configuration.GetKeyNamesAsync();

			Assert.DoesNotContain(
				RavenFileNameHelper.DeleteOperationConfigNameForFile(RavenFileNameHelper.DeletingFileName("toDelete.bin")),
				configNames);
		}

		[Fact]
		public void Should_create_apropriate_config_after_indicating_file_to_delete()
		{
            var filename = FileHeader.Canonize("toDelete.bin");

			var client = NewAsyncClient();
			var rfs = GetRavenFileSystem();

            client.UploadAsync(filename, new MemoryStream(new byte[] { 1, 2, 3, 4, 5 })).Wait();

            rfs.StorageOperationsTask.IndicateFileToDelete(filename);

			DeleteFileOperation deleteFile = null;
			rfs.Storage.Batch(accessor =>
                deleteFile = accessor.GetConfigurationValue<DeleteFileOperation>(RavenFileNameHelper.DeleteOperationConfigNameForFile(RavenFileNameHelper.DeletingFileName(filename))));

            Assert.Equal(RavenFileNameHelper.DeletingFileName(filename), deleteFile.CurrentFileName);
            Assert.Equal(filename, deleteFile.OriginalFileName);
		}

		[Fact]
		public async void Should_remove_file_deletion_config_after_storage_cleanup()
		{
			var client = NewAsyncClient();
			var rfs = GetRavenFileSystem();

			await client.UploadAsync("toDelete.bin", new MemoryStream(new byte[] {1, 2, 3, 4, 5}));

			rfs.StorageOperationsTask.IndicateFileToDelete("toDelete.bin");

			await rfs.StorageOperationsTask.CleanupDeletedFilesAsync();

			IEnumerable<string> configNames = null;
			rfs.Storage.Batch(accessor => configNames = accessor.GetConfigNames(0, 10).ToArray());

			Assert.DoesNotContain(RavenFileNameHelper.DeleteOperationConfigNameForFile(RavenFileNameHelper.DeletingFileName("toDelete.bin")), configNames);
		}

		[Fact]
		public async Task Should_update_indexes_after_storage_cleanup()
		{
			var client = NewAsyncClient();
			var rfs = GetRavenFileSystem();

            await client.UploadAsync("toDelete.bin", new MemoryStream(new byte[] { 1, 2, 3, 4, 5 }));

			rfs.StorageOperationsTask.IndicateFileToDelete(FileHeader.Canonize("toDelete.bin"));

            await rfs.StorageOperationsTask.CleanupDeletedFilesAsync();

            var searchResults = await client.SearchOnDirectoryAsync("/");

			Assert.Equal(0, searchResults.FileCount);
			Assert.Equal(0, searchResults.Files.Count());
		}

		[Fact]
		public async void Should_remove_deleting_file_and_its_pages_after_storage_cleanup()
		{
			const int numberOfPages = 10;

			var client = NewAsyncClient();
			var rfs = GetRavenFileSystem();

			var bytes = new byte[numberOfPages*StorageConstants.MaxPageSize];
			new Random().NextBytes(bytes);

			await client.UploadAsync("toDelete.bin", new MemoryStream(bytes));


			rfs.StorageOperationsTask.IndicateFileToDelete(FileHeader.Canonize("toDelete.bin"));

			await rfs.StorageOperationsTask.CleanupDeletedFilesAsync();

			Assert.Throws(typeof (FileNotFoundException),
			              () => rfs.Storage.Batch(accessor => accessor.GetFile(RavenFileNameHelper.DeletingFileName("toDelete.bin"), 0, 10)));

			for (var i = 1; i <= numberOfPages; i++)
			{
				var pageId = 0;
				var i1 = i;
				rfs.Storage.Batch(accessor => pageId = accessor.ReadPage(i1, null));
				Assert.Equal(-1, pageId); // if page does not exist we return -1
			}
		}

		[Fact]
		public void Should_not_perform_file_delete_if_it_is_being_synced()
        {
            var filename = FileHeader.Canonize("file.bin");

			var client = NewAsyncClient();
			var rfs = GetRavenFileSystem();

			client.UploadAsync("file.bin", new MemoryStream(new byte[] {1, 2, 3, 4, 5})).Wait();

            rfs.StorageOperationsTask.IndicateFileToDelete(filename);

			rfs.Storage.Batch( accessor =>
                    accessor.SetConfigurationValue(RavenFileNameHelper.SyncLockNameForFile(filename), 
                                                   LockFileTests.SynchronizationConfig(DateTime.UtcNow)));

			rfs.StorageOperationsTask.CleanupDeletedFilesAsync().Wait();

			DeleteFileOperation deleteFile = null;

			rfs.Storage.Batch(accessor =>
                deleteFile = accessor.GetConfigurationValue<DeleteFileOperation>(RavenFileNameHelper.DeleteOperationConfigNameForFile(RavenFileNameHelper.DeletingFileName(filename))));

            Assert.Equal(RavenFileNameHelper.DeletingFileName(filename), deleteFile.CurrentFileName);
            Assert.Equal(filename, deleteFile.OriginalFileName);
		}

		[Fact]
		public void Should_not_delete_downloading_file_if_synchronization_retry_is_being_performed()
		{
			const string fileName = "file.bin";
			var downloadingFileName = RavenFileNameHelper.DownloadingFileName(fileName);

			var client = NewAsyncClient();
			var rfs = GetRavenFileSystem();

			client.UploadAsync(fileName, new RandomStream(1)).Wait();

			client.UploadAsync(downloadingFileName, new RandomStream(1)).Wait();

			rfs.StorageOperationsTask.IndicateFileToDelete(downloadingFileName);

			rfs.Storage.Batch(accessor =>
				accessor.SetConfigurationValue(RavenFileNameHelper.SyncLockNameForFile(fileName), LockFileTests.SynchronizationConfig(DateTime.UtcNow)));

			rfs.StorageOperationsTask.CleanupDeletedFilesAsync().Wait();

			DeleteFileOperation deleteFile = null;
            rfs.Storage.Batch(accessor => 
                deleteFile = accessor.GetConfigurationValue<DeleteFileOperation>(RavenFileNameHelper.DeleteOperationConfigNameForFile(RavenFileNameHelper.DeletingFileName(downloadingFileName))));

			Assert.Equal(RavenFileNameHelper.DeletingFileName(downloadingFileName), deleteFile.CurrentFileName);
			Assert.Equal(downloadingFileName, deleteFile.OriginalFileName);
		}

        [Fact]
        public async void Upload_before_performing_cleanup_do_a_rename_by_adding_version_number()
        {
            var client = NewAsyncClient();
            var rfs = GetRavenFileSystem();

            await client.UploadAsync("file.bin", new RandomStream(1));

            // this upload should indicate old file to delete
            await client.UploadAsync("file.bin", new RandomStream(1));

            // upload again - note that actual file delete was not performed yet
            await client.UploadAsync("file.bin", new RandomStream(1));

            List<string> configNames = null;
            rfs.Storage.Batch(
                accessor =>
                configNames =
                accessor.GetConfigNames(0, 10).ToArray().Where(x => x.StartsWith(RavenFileNameHelper.DeleteOperationConfigPrefix)).ToList());

            Assert.Equal(2, configNames.Count());

            foreach (var configName in configNames)
            {
                Assert.True(RavenFileNameHelper.DeleteOperationConfigPrefix + RavenFileNameHelper.DeletingFileName("file.bin") == configName ||
                            RavenFileNameHelper.DeleteOperationConfigPrefix + RavenFileNameHelper.DeletingFileName("file.bin1") == configName); // 1 indicate delete version
            }
        }

		[Fact]
		public void Should_resume_to_rename_file_if_appropriate_config_exists()
		{
			var client = NewAsyncClient();
			var rfs = GetRavenFileSystem();

            string fileName = FileHeader.Canonize("file.bin");
			string rename = FileHeader.Canonize("renamed.bin");

			client.UploadAsync(fileName, new RandomStream(1)).Wait();

			// create config to say to the server that rename operation performed last time were not finished
			var renameOpConfig = RavenFileNameHelper.RenameOperationConfigNameForFile(fileName);
            var renameOperation =  new RenameFileOperation
				                        {
					                        Name = fileName,
					                        Rename = rename,
                                            MetadataAfterOperation = new RavenJObject().WithETag(Guid.Empty)
				                        };

            rfs.Storage.Batch(accessor => accessor.SetConfigurationValue(renameOpConfig, renameOperation));

			rfs.StorageOperationsTask.ResumeFileRenamingAsync().Wait();

			IEnumerable<string> configNames = null;
			rfs.Storage.Batch(accessor => configNames = accessor.GetConfigNames(0, 10).ToArray());

			Assert.DoesNotContain(renameOpConfig, configNames);

			var renamedMetadata = client.GetMetadataForAsync(rename).Result;

			Assert.NotNull(renamedMetadata);

			var results = client.SearchOnDirectoryAsync("/").Result; // make sure that indexes are updated

			Assert.Equal(1, results.FileCount);
			Assert.Equal(rename, results.Files[0].FullPath);
		}

		[Fact]
		public async Task Should_resume_file_renaming_from_client()
		{
			var client = NewAsyncClient();
			var rfs = GetRavenFileSystem();

			string fileName = FileHeader.Canonize("file.bin");
			string rename = FileHeader.Canonize("renamed.bin");

			await client.UploadAsync(fileName, new RandomStream(1));

			// create config to say to the server that rename operation performed last time were not finished
			var renameOpConfig = RavenFileNameHelper.RenameOperationConfigNameForFile(fileName);
            var renameOperation = new RenameFileOperation
				                    {
					                    Name = fileName,
					                    Rename = rename,
                                        MetadataAfterOperation = new RavenJObject().WithETag(Guid.Empty)
				                    };

            rfs.Storage.Batch(accessor => accessor.SetConfigurationValue(renameOpConfig, renameOperation ));

			await client.Storage.RetryRenamingAsync();

			IEnumerable<string> configNames = await client.Configuration.GetKeyNamesAsync();

			Assert.DoesNotContain(renameOpConfig, configNames);

			var renamedMetadata = await client.GetMetadataForAsync(rename);

			Assert.NotNull(renamedMetadata);
		}
	}
}