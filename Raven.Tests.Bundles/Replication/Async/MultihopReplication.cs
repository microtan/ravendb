﻿using System.Threading.Tasks;
using Raven.Abstractions.Replication;
using Raven.Client;
using Raven.Client.Document;
using Raven.Client.Extensions;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Bundles.Replication.Async
{
	public class MultihopReplication : ReplicationBase
	{
		IDocumentStore store1;
		IDocumentStore store2;
		IDocumentStore store3;

		string tracerId;

		[Fact]
		public async Task Can_run_replication_through_multiple_instances()
		{
			store1 = CreateStore();
			store2 = CreateStore();
			store3 = CreateStore();

			tracerId = await WriteTracer(store1);

			WaitForDocument<object>(store1, tracerId);

			RunReplication(store1, store2);
			WaitForDocument<object>(store2, tracerId);

			RunReplication(store2, store3, TransitiveReplicationOptions.Replicate);
			WaitForDocument<object>(store3, tracerId);
		}

		[Fact]
		public async Task When_source_is_reset_can_replicate_back()
		{
			await Can_run_replication_through_multiple_instances();

		    var store1DatabaseName = ((DocumentStore)store1).DefaultDatabase;

			store1 = ResetDatabase(0, databaseName: store1DatabaseName);

			RunReplication(store3, store1, TransitiveReplicationOptions.Replicate);

			WaitForDocument<object>(store1, tracerId);
		}

		private async Task<string> WriteTracer(IDocumentStore store1)
		{
			var targetStore = store1;
			using(var session = targetStore.OpenAsyncSession())
			{
				var tracer = new {};
				await session.StoreAsync(tracer);
				await session.SaveChangesAsync();
				return session.Advanced.GetDocumentId(tracer);
			}
		}
	}
}