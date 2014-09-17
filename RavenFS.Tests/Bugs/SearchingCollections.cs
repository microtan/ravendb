﻿// -----------------------------------------------------------------------
//  <copyright file="SearchingCollections.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System.IO;
using System.Threading.Tasks;
using Raven.Client.FileSystem;
using Raven.Json.Linq;
using Xunit;

namespace RavenFS.Tests.Bugs
{
	public class SearchingCollections : RavenFsTestBase
	{
		[Fact]
		public async Task CanSearch()
		{
			using (var store = NewStore())
			{
				using (var session = store.OpenAsyncSession())
				{
					var ms = new MemoryStream();
					var metadata = new RavenJObject
					{
						{
							"Collections", new RavenJArray
							{
								"collections/1",
								"collections/2",
								"collections/3",
							}
						}
					};
					session.RegisterUpload("abc.txt", ms, metadata);
					await session.SaveChangesAsync();
				}

				using (var session = store.OpenAsyncSession())
				{
					var fileHeaders = await session.Query().ContainsAny("Collections", new[] {"collections/1"}).ToListAsync();
					Assert.NotEmpty(fileHeaders);
				}
			}

		}
	}
}