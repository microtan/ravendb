﻿using Raven.Abstractions.Data;
using Raven.Json.Linq;
using System.Collections.Specialized;
using System.IO;
using Xunit;

namespace RavenFS.Tests.Bugs
{
    public class UpdatingMetadata : RavenFsTestBase
	{
		[Fact]
		public async void CanUpdateMetadata()
		{
			var client = NewAsyncClient(); 
			var ms = new MemoryStream();
			var streamWriter = new StreamWriter(ms);
			var expected = new string('a', 1024);
			streamWriter.Write(expected);
			streamWriter.Flush();
			ms.Position = 0;

            await client.UploadAsync("abc.txt", ms, new RavenJObject
			                                        {
				                                        {"test", "1"}
			                                        });

            await client.UpdateMetadataAsync("abc.txt", new RavenJObject
			                                                {
				                                                {"test", "2"}
			                                                });

			var metadataFor = await client.GetMetadataForAsync("abc.txt");


			Assert.Equal("2", metadataFor["test"]);
		}

		 
        [Fact]
        public async void PreserveSystemKeysWhenUpdatingMetadata()
        {
            var client = NewAsyncClient();
            var ms = new MemoryStream();
            var streamWriter = new StreamWriter(ms);
            var expected = new string('a', 1024);
            streamWriter.Write(expected);
            streamWriter.Flush();
            ms.Position = 0;

            await client.UploadAsync("abc.txt", ms, new RavenJObject
			                                        {
				                                        {"Test", "1"},
			                                        });
            
            await client.UpdateMetadataAsync("abc.txt", new RavenJObject
			                                                {
				                                                {"Test", "2"}
			                                                });

            var metadata = await client.GetMetadataForAsync("abc.txt");

            Assert.True(metadata.ContainsKey(Constants.LastModified));
            Assert.True(metadata.ContainsKey("RavenFS-Size"));
            Assert.True(metadata.ContainsKey(Constants.MetadataEtagField));
            Assert.True(metadata.ContainsKey("Content-MD5"));
            Assert.True(metadata.ContainsKey("Content-Length"));
        }		 
	}
}