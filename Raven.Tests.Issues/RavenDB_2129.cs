﻿// -----------------------------------------------------------------------
//  <copyright file="RavenDB_2129.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Threading.Tasks;

using Raven.Abstractions;
using Raven.Abstractions.Data;
using Raven.Abstractions.Replication;
using Raven.Client.Connection;
using Raven.Client.Document;
using Raven.Client.Extensions;
using Raven.Tests.Common;
using Raven.Tests.Common.Dto;

using Xunit;

namespace Raven.Tests.Issues
{
	public class RavenDB_2129 : ReplicationBase
	{
		[Fact]
		public async Task ShouldWork()
		{
			using (var store1 = CreateStore(configureStore: store => store.Conventions.FailoverBehavior = FailoverBehavior.AllowReadsFromSecondaries))
			using (var store2 = CreateStore())
			{
				await store1.AsyncDatabaseCommands.GlobalAdmin.EnsureDatabaseExistsAsync("SomeDB");
				await store2.AsyncDatabaseCommands.GlobalAdmin.EnsureDatabaseExistsAsync("SomeDB");

				RunReplication(store1, store2, db: "SomeDB");

				SystemTime.UtcDateTime = () => DateTime.Now.AddMinutes(10); // this will force replication information update when session is opened

				using (var session = store1.OpenAsyncSession("SomeDB"))
				{
					await session.StoreAsync(new Person { Name = "Person1" });
					await session.SaveChangesAsync();
				}

				WaitForReplication(store2, "people/1", db: "SomeDB");

				StopDatabase(0);

				using (var session = store1.OpenAsyncSession("SomeDB"))
				{
					var person = await session.LoadAsync<Person>("people/1");
					Assert.Equal("Person1", person.Name);
				}
			}
		} 
	}
}