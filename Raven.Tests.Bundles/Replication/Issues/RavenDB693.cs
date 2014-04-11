﻿using Raven.Abstractions.Data;
using Raven.Client.Document;
using Raven.Client.Listeners;
using Raven.Json.Linq;
using Raven.Tests.Common;

using Xunit;
using System.Linq;

namespace Raven.Tests.Bundles.Replication.Issues
{
	public class RavenDB693 : ReplicationBase
	{
		 [Fact]
		 public void CanHandleConflictsOnClient()
		 {
			 var store1 = CreateStore();
			 var store2 = CreateStore();

			 store1.DatabaseCommands.Put("ayende", null, new RavenJObject
			 {
				 {"Name", "Ayende"}
			 }, new RavenJObject());

			 store1.DatabaseCommands.Put("marker", null, new RavenJObject(), new RavenJObject());

			 store2.DatabaseCommands.Put("ayende", null, new RavenJObject
			 {
				 {"Name", "Rahien"}
			 }, new RavenJObject());


			 TellFirstInstanceToReplicateToSecondInstance();
			 WaitForReplication(store2, "marker");

			 store2.RegisterListener(new ClientSideConflictResolution());

			 var jsonDocument = store2.DatabaseCommands.Get("ayende");

			 Assert.Equal("Ayende Rahien", jsonDocument.DataAsJson.Value<string>("Name"));
		 }

		 [Fact]
		 public void CanHandleConflictsOnClient_Async()
		 {
			 var store1 = CreateStore();
			 var store2 = CreateStore();

			 store1.DatabaseCommands.Put("ayende", null, new RavenJObject
			 {
				 {"Name", "Ayende"}
			 }, new RavenJObject());

			 store1.DatabaseCommands.Put("marker", null, new RavenJObject(), new RavenJObject());

			 store2.DatabaseCommands.Put("ayende", null, new RavenJObject
			 {
				 {"Name", "Rahien"}
			 }, new RavenJObject());


			 TellFirstInstanceToReplicateToSecondInstance();
			 WaitForReplication(store2, "marker");

			 ((DocumentStore)store2).RegisterListener(new ClientSideConflictResolution());

			 var jsonDocument = store2.AsyncDatabaseCommands.GetAsync("ayende").Result;

			 Assert.Equal("Ayende Rahien", jsonDocument.DataAsJson.Value<string>("Name"));
		 }
	}
}