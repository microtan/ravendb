﻿using System.Collections.Generic;
using System.Threading.Tasks;

using Raven.Abstractions.Replication;
using Raven.Client;
using Raven.Client.Document;
using Raven.Client.Shard;
using Raven.Server;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Issues
{
	public class RavenDB_579 : RavenTest
	{
		private new readonly RavenDbServer[] servers;
		private readonly ShardedDocumentStore documentStore;

		private readonly IList<string> shardNames = new List<string>
		{
			"1",
			"2",
			"3"
		}; 

		public class Person
		{
			public string Id { get; set; }
			public string FirstName { get; set; }
			public string LastName { get; set; }
			public string MiddleName { get; set; }
		}

		public RavenDB_579()
		{
			servers = new[]
			{
				GetNewServer(8079),
				GetNewServer(8078),
				GetNewServer(8077),
			};

			documentStore = new ShardedDocumentStore(new ShardStrategy(new Dictionary<string, IDocumentStore>
			{
				{shardNames[0], CreateDocumentStore(8079)},
				{shardNames[1], CreateDocumentStore(8078)},
				{shardNames[2], CreateDocumentStore(8077)}
			}));
			documentStore.Initialize();
		}


		private static IDocumentStore CreateDocumentStore(int port)
		{
			return new DocumentStore
			{
				Url = string.Format("http://localhost:{0}/", port),
				Conventions =
				{
					FailoverBehavior = FailoverBehavior.FailImmediately
				}
			};
		}

		[Fact]
		public void OneShardPerSessionStrategy()
		{
			using (var session = documentStore.OpenSession())
			{
				var sessionMetadata = ExtractSessionMetadataFromSession(session);

				var expectedShard = shardNames[sessionMetadata.GetHashCode() % shardNames.Count];

				var entity1 = new Person { Id = "1", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				session.Store(entity1);
				var entity2 = new Person { Id = "2", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				session.Store(entity2);
				session.SaveChanges();

				var entity3 = new Person { Id = "3", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				session.Store(entity3);
				var entity4 = new Person { Id = "4", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				session.Store(entity4);
				session.SaveChanges();

				Assert.Equal(expectedShard + "/1", entity1.Id);
				Assert.Equal(expectedShard + "/2", entity2.Id);
				Assert.Equal(expectedShard + "/3", entity3.Id);
				Assert.Equal(expectedShard + "/4", entity4.Id);
			}

			using (var session = documentStore.OpenSession())
			{
				var sessionMetadata = ExtractSessionMetadataFromSession(session);

				var expectedShard = shardNames[sessionMetadata.GetHashCode() % shardNames.Count];

				var entity1 = new Person { Id = "1", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				session.Store(entity1);
				var entity2 = new Person { Id = "2", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				session.Store(entity2);
				session.SaveChanges();

				Assert.Equal(expectedShard + "/1", entity1.Id);
				Assert.Equal(expectedShard + "/2", entity2.Id);
			}
		}

		[Fact]
		public async Task OneShardPerSessionStrategyAsync()
		{
			using (var session = documentStore.OpenAsyncSession())
			{
				var sessionMetadata = ExtractSessionMetadataFromSession(session);

				var expectedShard = shardNames[sessionMetadata.GetHashCode() % shardNames.Count];

				var entity1 = new Person { Id = "1", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				await session.StoreAsync(entity1);
				var entity2 = new Person { Id = "2", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				await session.StoreAsync(entity2);
				await session.SaveChangesAsync();

				var entity3 = new Person { Id = "3", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				await session.StoreAsync(entity3);
				var entity4 = new Person { Id = "4", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				await session.StoreAsync(entity4);
				await session.SaveChangesAsync();

				Assert.Equal(expectedShard + "/1", entity1.Id);
				Assert.Equal(expectedShard + "/2", entity2.Id);
				Assert.Equal(expectedShard + "/3", entity3.Id);
				Assert.Equal(expectedShard + "/4", entity4.Id);
			}

			using (var session = documentStore.OpenAsyncSession())
			{
				var sessionMetadata = ExtractSessionMetadataFromSession(session);

				var expectedShard = shardNames[sessionMetadata.GetHashCode() % shardNames.Count];

				var entity1 = new Person { Id = "1", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				await session.StoreAsync(entity1);
				var entity2 = new Person { Id = "2", FirstName = "William", MiddleName = "Edgard", LastName = "Smith" };
				await session.StoreAsync(entity2);
				await session.SaveChangesAsync();

				Assert.Equal(expectedShard + "/1", entity1.Id);
				Assert.Equal(expectedShard + "/2", entity2.Id);
			}
		}

		public override void Dispose()
		{
			documentStore.Dispose();
			foreach (var server in servers)
			{
				server.Dispose();
			}
			base.Dispose();
		}

		private ITransactionalDocumentSession ExtractSessionMetadataFromSession(object session)
		{
			return (ITransactionalDocumentSession) session;
		}
	}
}