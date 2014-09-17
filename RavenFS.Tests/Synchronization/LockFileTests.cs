﻿using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using Raven.Database.Server.RavenFS.Extensions;
using Raven.Database.Server.RavenFS.Synchronization;
using Raven.Database.Server.RavenFS.Util;
using RavenFS.Tests.Synchronization.IO;
using Xunit;
using Raven.Json.Linq;
using Raven.Client.FileSystem;
using Raven.Abstractions.FileSystem;

namespace RavenFS.Tests.Synchronization
{
    public class LockFileTests : RavenFsTestBase
	{
		[Fact]
		public async Task Should_delete_sync_configuration_after_synchronization()
		{
            IAsyncFilesCommands destinationClient;
            IAsyncFilesCommands sourceClient;

			UploadFilesSynchronously(out sourceClient, out destinationClient);

			await sourceClient.Synchronization.StartAsync("test.bin", destinationClient);
            var config = await destinationClient.Configuration.GetKeyAsync<SynchronizationLock>(RavenFileNameHelper.SyncLockNameForFile("test.bin"));

			Assert.Null(config);
		}

		[Fact]
		public async Task Should_refuse_to_update_metadata_while_sync_configuration_exists()
		{
            IAsyncFilesCommands destinationClient;
            IAsyncFilesCommands sourceClient;

			UploadFilesSynchronously(out sourceClient, out destinationClient);

			await destinationClient.Configuration.SetKeyAsync(RavenFileNameHelper.SyncLockNameForFile("test.bin"), SynchronizationConfig(DateTime.UtcNow));

            var innerException = SyncTestUtils.ExecuteAndGetInnerException(async () => await destinationClient.UpdateMetadataAsync("test.bin", new RavenJObject()));

			Assert.IsType(typeof (SynchronizationException), innerException.GetBaseException());
            Assert.Equal(string.Format("File {0} is being synced", FileHeader.Canonize("test.bin")), innerException.GetBaseException().Message);
		}

		[Fact]
		public async Task Should_refuse_to_delete_file_while_sync_configuration_exists()
		{
            IAsyncFilesCommands destinationClient;
            IAsyncFilesCommands sourceClient;

			UploadFilesSynchronously(out sourceClient, out destinationClient);

			await destinationClient.Configuration.SetKeyAsync(RavenFileNameHelper.SyncLockNameForFile("test.bin"), SynchronizationConfig(DateTime.UtcNow));

			var innerException = SyncTestUtils.ExecuteAndGetInnerException(async () => await destinationClient.DeleteAsync("test.bin"));

			Assert.IsType(typeof (SynchronizationException), innerException.GetBaseException());
            Assert.Equal(string.Format("File {0} is being synced", FileHeader.Canonize("test.bin")), innerException.GetBaseException().Message);
		}

		[Fact]
		public async Task Should_refuse_to_rename_file_while_sync_configuration_exists()
		{
            IAsyncFilesCommands destinationClient;
            IAsyncFilesCommands sourceClient;

			UploadFilesSynchronously(out sourceClient, out destinationClient);

			await destinationClient.Configuration.SetKeyAsync(RavenFileNameHelper.SyncLockNameForFile("test.bin"), SynchronizationConfig(DateTime.UtcNow));

			var innerException =
				SyncTestUtils.ExecuteAndGetInnerException(async () => await destinationClient.RenameAsync("test.bin", "newname.bin"));

			Assert.IsType(typeof (SynchronizationException), innerException.GetBaseException());
            Assert.Equal(string.Format("File {0} is being synced", FileHeader.Canonize("test.bin")), innerException.GetBaseException().Message);
		}

		[Fact]
		public async Task Should_refuse_to_upload_file_while_sync_configuration_exists()
		{
            IAsyncFilesCommands destinationClient;
            IAsyncFilesCommands sourceClient;

			UploadFilesSynchronously(out sourceClient, out destinationClient);

			await destinationClient.Configuration.SetKeyAsync(RavenFileNameHelper.SyncLockNameForFile("test.bin"), SynchronizationConfig(DateTime.UtcNow));

			var innerException = SyncTestUtils.ExecuteAndGetInnerException(async () => await destinationClient.UploadAsync("test.bin", new MemoryStream()));

			Assert.IsType(typeof (SynchronizationException), innerException.GetBaseException());
            Assert.Equal(string.Format("File {0} is being synced", FileHeader.Canonize("test.bin")), innerException.GetBaseException().Message);
		}

		[Fact]
		public async void Should_refuse_to_synchronize_file_while_sync_configuration_exists()
		{
            IAsyncFilesCommands destinationClient;
            IAsyncFilesCommands sourceClient;

			UploadFilesSynchronously(out sourceClient, out destinationClient);

			await destinationClient.Configuration.SetKeyAsync(RavenFileNameHelper.SyncLockNameForFile("test.bin"), SynchronizationConfig(DateTime.UtcNow));

			var synchronizationReport = SyncTestUtils.ResolveConflictAndSynchronize(sourceClient, destinationClient, "test.bin");

            Assert.Equal(string.Format("File {0} is being synced", FileHeader.Canonize("test.bin")), synchronizationReport.Exception.Message);
		}

		[Fact]
		public void Should_successfully_update_metadata_if_last_synchronization_timeout_exceeded()
		{
            IAsyncFilesCommands destinationClient;
            IAsyncFilesCommands sourceClient;

			UploadFilesSynchronously(out sourceClient, out destinationClient);

            ZeroTimeoutTest(destinationClient, () => destinationClient.UpdateMetadataAsync("test.bin", new RavenJObject()).Wait());
		}

		[Fact]
		public void Should_successfully_delete_file_if_last_synchronization_timeout_exceeded()
		{
            IAsyncFilesCommands destinationClient;
            IAsyncFilesCommands sourceClient;

			UploadFilesSynchronously(out sourceClient, out destinationClient);

			ZeroTimeoutTest(destinationClient, () => destinationClient.DeleteAsync("test.bin").Wait());
		}

		[Fact]
		public void Should_successfully_rename_file_if_last_synchronization_timeout_exceeded()
		{
            IAsyncFilesCommands destinationClient;
            IAsyncFilesCommands sourceClient;

			UploadFilesSynchronously(out sourceClient, out destinationClient);

			ZeroTimeoutTest(destinationClient, () => destinationClient.RenameAsync("test.bin", "newname.bin").Wait());
		}

		[Fact]
		public void Should_successfully_upload_file_if_last_synchronization_timeout_exceeded()
		{
            IAsyncFilesCommands destinationClient;
            IAsyncFilesCommands sourceClient;

			UploadFilesSynchronously(out sourceClient, out destinationClient);

			ZeroTimeoutTest(destinationClient, () => destinationClient.UploadAsync("test.bin", new MemoryStream()).Wait());
		}

		[Fact]
		public async void Should_successfully_synchronize_if_last_synchronization_timeout_exceeded()
		{
            IAsyncFilesCommands destinationClient;
            IAsyncFilesCommands sourceClient;

			UploadFilesSynchronously(out sourceClient, out destinationClient);

            await destinationClient.Configuration.SetKeyAsync(SynchronizationConstants.RavenSynchronizationLockTimeout, TimeSpan.FromSeconds(0));

			Assert.DoesNotThrow(() => SyncTestUtils.ResolveConflictAndSynchronize(sourceClient, destinationClient, "test.bin"));
		}

        private void UploadFilesSynchronously(out IAsyncFilesCommands sourceClient,
                                              out IAsyncFilesCommands destinationClient, string fileName = "test.bin")
		{
			sourceClient = NewAsyncClient(1);
			destinationClient = NewAsyncClient(0);

			var sourceContent = new RandomlyModifiedStream(new RandomStream(10, 1), 0.01);
			var destinationContent = new RandomlyModifiedStream(new RandomStream(10, 1), 0.01);

			destinationClient.UploadAsync(fileName, destinationContent).Wait();
			sourceClient.UploadAsync(fileName, sourceContent).Wait();
		}

        public static SynchronizationLock SynchronizationConfig(DateTime fileLockedDate)
		{
            return new SynchronizationLock { FileLockedAt = fileLockedDate };
		}

        private static void ZeroTimeoutTest(IAsyncFilesCommands destinationClient, Action action)
		{
			destinationClient.Configuration.SetKeyAsync(RavenFileNameHelper.SyncLockNameForFile("test.bin"), SynchronizationConfig(DateTime.MinValue)).Wait();

            destinationClient.Configuration.SetKeyAsync(SynchronizationConstants.RavenSynchronizationLockTimeout, TimeSpan.FromSeconds(0) ).Wait();

			Assert.DoesNotThrow(() => action());
		}
	}
}