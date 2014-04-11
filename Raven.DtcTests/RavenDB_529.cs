﻿using System;
using System.Transactions;
using Raven.Abstractions;
using Raven.Json.Linq;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Issues
{
	public class RavenDB_529 : RavenTest
	{
		[Fact]
		public void ReadsWillRespectTxLockExpiry()
		{
            using (var store = NewDocumentStore(requestedStorage: "esent"))
			{
                EnsureDtcIsSupported(store);

				using(var session = store.OpenSession())
				{
					session.Store(new{Name = "test"}, "test");
					session.SaveChanges();
				}
				using(new TransactionScope())
				{
					using (var session = store.OpenSession())
					{
						session.Store(new { Name = "another-test" }, "test");
						session.SaveChanges();
					}	

					using(new TransactionScope(TransactionScopeOption.Suppress))
					{
						Assert.True(
							store.DatabaseCommands.Get("test").NonAuthoritativeInformation.Value
							);

						SystemTime.UtcDateTime = () => DateTime.Today.AddDays(15);
						Assert.Null(
							store.DatabaseCommands.Get("test").NonAuthoritativeInformation
							);
					}
				}
			}
		}
	}
}