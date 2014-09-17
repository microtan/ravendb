using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Raven.Client.FileSystem.Extensions;
using Raven.Database.Server.RavenFS.Extensions;
using Raven.Database.Server.RavenFS.Util;
using RavenFS.Tests.Synchronization.IO;
using Xunit;
using Raven.Json.Linq;
using Raven.Abstractions.FileSystem;
using Raven.Client.FileSystem.Connection;
using Raven.Abstractions.Data;

namespace RavenFS.Tests.Synchronization
{
	public class SynchronizationOfDestinationsTests : RavenFsTestBase
	{
		private const int AddtitionalServerInstancePortNumber = 19083;

		[Fact]
		public async void Should_synchronize_to_all_destinations()
		{
            var canonicalFilename = FileHeader.Canonize("test.bin");

			var sourceContent = SyncTestUtils.PrepareSourceStream(10000);
			sourceContent.Position = 0;

			var sourceClient = NewAsyncClient(0);

			var destination1Client = NewAsyncClient(1);
		    var destination2Client = NewAsyncClient(2);

			var destination1Content = new RandomlyModifiedStream(sourceContent, 0.01);
			sourceContent.Position = 0;
			var destination2Content = new RandomlyModifiedStream(sourceContent, 0.01);
			sourceContent.Position = 0;

            await destination1Client.UploadAsync("test.bin", destination1Content);
            await destination2Client.UploadAsync("test.bin", destination2Content);

			sourceContent.Position = 0;
            await sourceClient.UploadAsync("test.bin", sourceContent);
			sourceContent.Position = 0;

            sourceClient.Synchronization.SetDestinationsAsync(destination1Client.ToSynchronizationDestination(), destination2Client.ToSynchronizationDestination()).Wait();

			var destinationSyncResults = sourceClient.Synchronization.SynchronizeAsync().Result;

			// we expect conflicts after first attempt of synchronization
			Assert.Equal(2, destinationSyncResults.Length);
            Assert.Equal(string.Format("File {0} is conflicted", canonicalFilename), destinationSyncResults[0].Reports.ToArray()[0].Exception.Message);
            Assert.Equal(string.Format("File {0} is conflicted", canonicalFilename), destinationSyncResults[1].Reports.ToArray()[0].Exception.Message);

            await destination1Client.Synchronization.ResolveConflictAsync("test.bin", ConflictResolutionStrategy.RemoteVersion);
            await destination2Client.Synchronization.ResolveConflictAsync("test.bin", ConflictResolutionStrategy.RemoteVersion);

            destinationSyncResults = await sourceClient.Synchronization.SynchronizeAsync();

            var conflictItem = await destination1Client.Configuration.GetKeyAsync<ConflictItem>(RavenFileNameHelper.ConflictConfigNameForFile("test.bin"));
			Assert.Null(conflictItem);

            conflictItem = await destination2Client.Configuration.GetKeyAsync<ConflictItem>(RavenFileNameHelper.ConflictConfigNameForFile("test.bin"));
			Assert.Null(conflictItem);

			// check if reports match
			Assert.Equal(2, destinationSyncResults.Length);
			var result1 = destinationSyncResults[0].Reports.ToArray()[0];
			Assert.Equal(sourceContent.Length, result1.BytesCopied + result1.BytesTransfered);
			Assert.Equal(SynchronizationType.ContentUpdate, result1.Type);

			var result2 = destinationSyncResults[1].Reports.ToArray()[0];
			Assert.Equal(sourceContent.Length, result2.BytesCopied + result2.BytesTransfered);
			Assert.Equal(SynchronizationType.ContentUpdate, result2.Type);

			// check content of files
			string destination1Md5;
            using (var resultFileContent = await destination1Client.DownloadAsync("test.bin"))
			{
				destination1Md5 = resultFileContent.GetMD5Hash();
			}

			string destination2Md5;
            using (var resultFileContent = await destination2Client.DownloadAsync("test.bin"))
			{
				destination2Md5 = resultFileContent.GetMD5Hash();
			}

			sourceContent.Position = 0;
			var sourceMd5 = sourceContent.GetMD5Hash();

			Assert.Equal(sourceMd5, destination1Md5);
			Assert.Equal(sourceMd5, destination2Md5);
			Assert.Equal(destination1Md5, destination2Md5);
		}

		[Fact]
		public async Task Should_not_synchronize_file_back_to_source_if_origins_from_source()
		{
			var sourceClient = NewAsyncClient(0);
			var destinationClient = NewAsyncClient(1);

			await sourceClient.UploadAsync("test.bin", new RandomStream(1024));

            await sourceClient.Synchronization.SetDestinationsAsync(destinationClient.ToSynchronizationDestination());

			// synchronize from source to destination
			var destinationSyncResults = await sourceClient.Synchronization.SynchronizeAsync();

			Assert.Equal(1, destinationSyncResults[0].Reports.Count());
			Assert.Equal(SynchronizationType.ContentUpdate, destinationSyncResults[0].Reports.ToArray()[0].Type);

            await destinationClient.Synchronization.SetDestinationsAsync(sourceClient.ToSynchronizationDestination());

			// synchronize from destination to source
			var sourceSyncResults = await destinationClient.Synchronization.SynchronizeAsync();

			Assert.Equal(1, sourceSyncResults.Count());
			Assert.Null(sourceSyncResults[0].Reports);
		}

		[Fact]
		public async Task Synchronization_should_upload_all_missing_files()
		{
			var sourceClient = NewAsyncClient(0);
			var destinationClient = NewAsyncClient(1);

			var source1Content = new RandomStream(10000);

			await sourceClient.UploadAsync("test1.bin", source1Content);

			var source2Content = new RandomStream(10000);

			await sourceClient.UploadAsync("test2.bin", source2Content);

            await sourceClient.Synchronization.SetDestinationsAsync(destinationClient.ToSynchronizationDestination());
			await sourceClient.Synchronization.SynchronizeAsync();

			var destinationFiles = await destinationClient.SearchOnDirectoryAsync("/");
			Assert.Equal(2, destinationFiles.FileCount);
            Assert.Equal(2, destinationFiles.Files.Count);
			Assert.NotEqual(destinationFiles.Files[0].Name, destinationFiles.Files[1].Name);
			Assert.True(destinationFiles.Files[0].Name == "test1.bin" || destinationFiles.Files[0].Name == "test2.bin");
			Assert.True(destinationFiles.Files[1].Name == "test1.bin" || destinationFiles.Files[1].Name == "test2.bin");
		}

		[Fact]
		public async Task Make_sure_that_locks_are_released_after_synchronization_when_two_files_synchronized_simultaneously()
		{            
			var sourceClient = NewAsyncClient(0);
			var destinationClient = NewAsyncClient(1);

			var source1Content = new RandomStream(10000);

			await sourceClient.UploadAsync("test1.bin", source1Content);

			var source2Content = new RandomStream(10000);

			await sourceClient.UploadAsync("test2.bin", source2Content);

            await sourceClient.Synchronization.SetDestinationsAsync(destinationClient.ToSynchronizationDestination());

			await sourceClient.Synchronization.SynchronizeAsync();

			var configs = await destinationClient.Configuration.GetKeyNamesAsync();

            Assert.DoesNotContain(RavenFileNameHelper.SyncLockNameForFile("test1.bin"), configs);
			Assert.DoesNotContain(RavenFileNameHelper.SyncLockNameForFile("test2.bin"), configs);

			// also make sure that results exist
            Assert.Contains(RavenFileNameHelper.SyncResultNameForFile("test1.bin"), configs);
            Assert.Contains(RavenFileNameHelper.SyncResultNameForFile("test2.bin"), configs);
		}

		[Fact]
		public async Task Source_should_save_configuration_record_after_synchronization()
		{
			var sourceClient = NewAsyncClient(0);
			var sourceContent = new RandomStream(10000);

			var destinationClient = NewAsyncClient(1);

			await sourceClient.UploadAsync("test.bin", sourceContent);

            await sourceClient.Synchronization.SetDestinationsAsync(destinationClient.ToSynchronizationDestination());
			await sourceClient.Synchronization.SynchronizeAsync();

		    var fullDstUrl = destinationClient.ToSynchronizationDestination().Url;

            var synchronizationDetails = sourceClient.Configuration.GetKeyAsync<SynchronizationDetails>(RavenFileNameHelper.SyncNameForFile("test.bin", fullDstUrl)).Result;

			Assert.Equal(FileHeader.Canonize("test.bin"), synchronizationDetails.FileName);
            Assert.Equal(fullDstUrl, synchronizationDetails.DestinationUrl);
			Assert.NotEqual(Guid.Empty, synchronizationDetails.FileETag);
			Assert.Equal(SynchronizationType.ContentUpdate, synchronizationDetails.Type);
		}

		[Fact]
		public async Task Source_should_delete_configuration_record_if_destination_confirm_that_file_is_safe()
		{
			var sourceClient = NewAsyncClient(0);
			var sourceContent = new RandomStream(10000);

            var destinationClient = (IAsyncFilesCommandsImpl)NewAsyncClient(1);

			await sourceClient.UploadAsync("test.bin", sourceContent);


            await sourceClient.Synchronization.SetDestinationsAsync(destinationClient.ToSynchronizationDestination());

			await sourceClient.Synchronization.SynchronizeAsync();

			// start synchronization again to force confirmation by source
			await sourceClient.Synchronization.SynchronizeAsync();

            var shouldBeNull = await sourceClient.Configuration.GetKeyAsync<SynchronizationDetails>(RavenFileNameHelper.SyncNameForFile("test.bin", destinationClient.ServerUrl));

			Assert.Null(shouldBeNull);
		}

		[Fact]
		public async Task File_should_be_in_pending_queue_if_no_synchronization_requests_available()
		{
			var sourceContent = new RandomStream(1);
            var sourceClient = NewAsyncClient(0);

            await sourceClient.Configuration.SetKeyAsync(SynchronizationConstants.RavenSynchronizationLimit, 1);

            var destinationClient = (IAsyncFilesCommandsImpl) NewAsyncClient(1);

			await sourceClient.UploadAsync("test.bin", sourceContent);
			await sourceClient.UploadAsync("test2.bin", sourceContent);

            await sourceClient.Synchronization.SetDestinationsAsync(destinationClient.ToSynchronizationDestination());
			await sourceClient.Synchronization.SynchronizeAsync();

			var pendingSynchronizations = await sourceClient.Synchronization.GetPendingAsync();

			Assert.Equal(1, pendingSynchronizations.TotalCount);
            Assert.Contains(destinationClient.ServerUrl, pendingSynchronizations.Items[0].DestinationUrl);
		}

		[Fact]
		public async Task Should_change_metadata_on_all_destinations()
		{
			var sourceClient = NewAsyncClient(0);

			var destination1Client = NewAsyncClient(1);
		    var destination2Client = NewAsyncClient(2);

			var sourceContent = new MemoryStream();
			var streamWriter = new StreamWriter(sourceContent);
			var expected = new string('a', 1024*1024*10);
			streamWriter.Write(expected);
			streamWriter.Flush();
			sourceContent.Position = 0;

			await sourceClient.UploadAsync("test.txt", sourceContent);

            await sourceClient.Synchronization.SetDestinationsAsync(destination1Client.ToSynchronizationDestination(), destination2Client.ToSynchronizationDestination());

			// push file to all destinations
			await sourceClient.Synchronization.SynchronizeAsync();

			// prevent pushing files after metadata update
			await sourceClient.Configuration.DeleteKeyAsync(SynchronizationConstants.RavenSynchronizationDestinations);

            await sourceClient.UpdateMetadataAsync("test.txt", new RavenJObject { { "value", "shouldBeSynchronized" } });

			// add destinations again
            await sourceClient.Synchronization.SetDestinationsAsync(destination1Client.ToSynchronizationDestination(), destination2Client.ToSynchronizationDestination());

			// should synchronize metadata
			var destinationSyncResults = sourceClient.Synchronization.SynchronizeAsync().Result;

			foreach (var destinationSyncResult in destinationSyncResults)
			{
				foreach (var report in destinationSyncResult.Reports)
				{
					Assert.Null(report.Exception);
					Assert.Equal(SynchronizationType.MetadataUpdate, report.Type);
				}
			}

            var metadata1 = await destination1Client.GetMetadataForAsync("test.txt");
            var metadata2 = await destination2Client.GetMetadataForAsync("test.txt");

            Assert.Equal("shouldBeSynchronized", metadata1.Value<string>("value"));
			Assert.Equal("shouldBeSynchronized", metadata2.Value<string>("value"));
		}

		[Fact]
		public async Task Should_rename_file_on_all_destinations()
		{
			var sourceClient = NewAsyncClient(0);

			var destination1Client = NewAsyncClient(1);
		    var destination2Client = NewAsyncClient(2);

			// upload file to all servers
			await sourceClient.UploadAsync("test.bin", new RandomStream(10));
            await sourceClient.Synchronization.SetDestinationsAsync(destination1Client.ToSynchronizationDestination(), destination2Client.ToSynchronizationDestination());
			await sourceClient.Synchronization.SynchronizeAsync();

			await sourceClient.Configuration.DeleteKeyAsync(SynchronizationConstants.RavenSynchronizationDestinations);


			// delete file on source
			await sourceClient.RenameAsync("test.bin", "rename.bin");

			// set up destinations
            await sourceClient.Synchronization.SetDestinationsAsync(destination1Client.ToSynchronizationDestination(), destination2Client.ToSynchronizationDestination());

			var destinationSyncResults = sourceClient.Synchronization.SynchronizeAsync().Result;

			foreach (var destinationSyncResult in destinationSyncResults)
			{
				foreach (var report in destinationSyncResult.Reports)
				{
					Assert.Null(report.Exception);
					Assert.Equal(SynchronizationType.Rename, report.Type);
				}
			}

			Assert.Null(destination1Client.GetMetadataForAsync("test.bin").Result);
			Assert.Null(destination2Client.GetMetadataForAsync("test.bin").Result);
			Assert.NotNull(destination1Client.GetMetadataForAsync("rename.bin").Result);
			Assert.NotNull(destination2Client.GetMetadataForAsync("rename.bin").Result);
		}

		[Fact]
		public async Task Should_delete_file_on_all_destinations()
		{
			var sourceClient = NewAsyncClient(0);

			var destination1Client = NewAsyncClient(1);
		    var destination2Client = NewAsyncClient(2);

			// upload file to first server and synchronize to others
			await sourceClient.UploadAsync("test.bin", new RandomStream(10));
			await sourceClient.Synchronization.StartAsync("test.bin", destination1Client);
			await sourceClient.Synchronization.StartAsync("test.bin", destination2Client);

			// delete file on source
			await sourceClient.DeleteAsync("test.bin");

			// set up destinations
            await sourceClient.Synchronization.SetDestinationsAsync(destination1Client.ToSynchronizationDestination(), destination2Client.ToSynchronizationDestination());

			var destinationSyncResults = await sourceClient.Synchronization.SynchronizeAsync();

			foreach (var destinationSyncResult in destinationSyncResults)
			{
				foreach (var report in destinationSyncResult.Reports)
				{
					Assert.Null(report.Exception);
					Assert.Equal(SynchronizationType.Delete, report.Type);
				}
			}

			Assert.Null(await destination1Client.GetMetadataForAsync("test.bin"));
			Assert.Null(await destination1Client.GetMetadataForAsync("test.bin"));
		}

		[Fact]
		public async Task Should_confirm_that_file_is_safe()
		{
			var sourceContent = new RandomStream(1024*1024);

			var sourceClient = NewAsyncClient(1);
			var destinationClient = NewAsyncClient(0);

			await sourceClient.UploadAsync("test.bin", sourceContent);

            await sourceClient.Synchronization.SetDestinationsAsync(destinationClient.ToSynchronizationDestination());
			await sourceClient.Synchronization.SynchronizeAsync();

            var metadata = await sourceClient.GetMetadataForAsync("test.bin");

			var confirmations = await destinationClient.Synchronization.GetConfirmationForFilesAsync(
                                    new List<Tuple<string, Guid>>
				                    {
					                    new Tuple<string, Guid>("test.bin", metadata.Value<Guid>(Constants.MetadataEtagField))
				                    });

			var synchronizationConfirmations = confirmations as SynchronizationConfirmation[] ?? confirmations.ToArray();

			Assert.Equal(1, synchronizationConfirmations.Count());
			Assert.Equal(FileStatus.Safe, synchronizationConfirmations.ToArray()[0].Status);
			Assert.Equal(FileHeader.Canonize("test.bin"), synchronizationConfirmations.ToArray()[0].FileName);
		}

		[Fact]
		public void Should_say_that_file_status_is_unknown_if_there_is_different_etag()
		{
			var sourceContent = new RandomStream(1024*1024);

			var sourceClient = NewAsyncClient(1);
			var destinationClient = NewAsyncClient(0);

			sourceClient.UploadAsync("test.bin", sourceContent).Wait();

            sourceClient.Synchronization.SetDestinationsAsync(destinationClient.ToSynchronizationDestination()).Wait();

			sourceClient.Synchronization.SynchronizeAsync().Wait();

			var differentETag = Guid.NewGuid();

			var confirmations = destinationClient.Synchronization.GetConfirmationForFilesAsync(
                                                        new List<Tuple<string, Guid>> 
                                                        {
                                                            new Tuple<string, Guid>("test.bin", differentETag)
                                                        }).Result.ToList();

			Assert.Equal(1, confirmations.Count());
			Assert.Equal(FileStatus.Unknown, confirmations.ToArray()[0].Status);
			Assert.Equal(FileHeader.Canonize("test.bin"), confirmations.ToArray()[0].FileName);
		}

		[Fact]
		public void Should_report_that_file_state_is_unknown_if_file_doesnt_exist()
		{
			var destinationClient = NewAsyncClient(0);

			var confirmations = destinationClient.Synchronization.GetConfirmationForFilesAsync(new List<Tuple<string, Guid>>
					                                                    {
						                                                    new Tuple<string, Guid>(
							                                                    "test.bin", Guid.Empty)
					                                                    }).Result.ToList();

			Assert.Equal(1, confirmations.Count());
			Assert.Equal(FileStatus.Unknown, confirmations.ToArray()[0].Status);
            Assert.Equal(FileHeader.Canonize("test.bin"), confirmations.ToArray()[0].FileName);
		}

		[Fact]
		public async Task Should_report_that_file_is_broken_if_last_synchronization_set_exception()
		{
			var destinationClient = NewAsyncClient(0);

			var sampleGuid = Guid.NewGuid();

			var failureSynchronization = new SynchronizationReport("test.bin", sampleGuid, SynchronizationType.Unknown)
				                             {
                                                 Exception = new Exception("There was an exception in last synchronization.")
                                             };

			await destinationClient.Configuration.SetKeyAsync(RavenFileNameHelper.SyncResultNameForFile("test.bin"), failureSynchronization);

            var confirmations = await destinationClient.Synchronization
                                                       .GetConfirmationForFilesAsync(new List<Tuple<string, Guid>>
					                                                    {
						                                                    new Tuple<string, Guid>(
							                                                    "test.bin", sampleGuid)
					                                                    });
			Assert.Equal(1, confirmations.Count());
			Assert.Equal(FileStatus.Broken, confirmations[0].Status);
            Assert.Equal(FileHeader.Canonize("test.bin"), confirmations[0].FileName);
		}

		[Fact]
		public async Task Should_not_synchronize_if_file_is_conflicted_on_destination()
		{
			var sourceClient = NewAsyncClient(0);
			var destinationClient = NewAsyncClient(1);

            await destinationClient.UploadAsync("file.bin", new RandomStream(10), new RavenJObject { { SynchronizationConstants.RavenSynchronizationConflict, new RavenJValue(true) } });
			
            await sourceClient.UploadAsync("file.bin", new RandomStream(10));

            await sourceClient.Synchronization.SetDestinationsAsync(destinationClient.ToSynchronizationDestination());

			var destinationSyncResults = await sourceClient.Synchronization.SynchronizeAsync();

			Assert.Null(destinationSyncResults[0].Reports);
		}

		[Fact]
		public async Task Should_not_synchronize_if_file_is_conflicted_on_source()
		{
			var sourceClient = NewAsyncClient(0);
			var destinationClient = NewAsyncClient(1);

            await sourceClient.UploadAsync("file.bin", new RandomStream(10), new RavenJObject { { SynchronizationConstants.RavenSynchronizationConflict, new RavenJValue(true) } });

            await sourceClient.Synchronization.SetDestinationsAsync(destinationClient.ToSynchronizationDestination());

			var destinationSyncResults = await sourceClient.Synchronization.SynchronizeAsync();

			Assert.Null(destinationSyncResults[0].Reports);
		}

		[Fact]
		public void Should_not_fail_if_no_destinations_given()
		{
			var sourceClient = NewAsyncClient(0);

			IEnumerable<DestinationSyncResult> results = null;

			Assert.DoesNotThrow(() => results = sourceClient.Synchronization.SynchronizeAsync().Result);
			Assert.Equal(0, results.Count());
		}

		[Fact]
		public async void Should_not_fail_if_there_is_no_file_to_synchronize()
		{
			var sourceClient = NewAsyncClient(0);
			var destinationClient = NewAsyncClient(1);

            await sourceClient.Synchronization.SetDestinationsAsync(destinationClient.ToSynchronizationDestination());

			IEnumerable<DestinationSyncResult> results = null;

			Assert.DoesNotThrow(() => results = sourceClient.Synchronization.SynchronizeAsync().Result);

			Assert.Null(results.ToArray()[0].Reports);
		}
	}
}