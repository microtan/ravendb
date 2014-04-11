﻿// -----------------------------------------------------------------------
//  <copyright file="RavenDB_743.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System.Linq;
using Raven.Client.Indexes;
using Raven.Tests.Common;

using Xunit;
using Xunit.Extensions;

namespace Raven.Tests.Issues
{
	public class RavenDB_743 : RavenTest
	{
		public class User
		{
			public string Id { get; set; }
			public string Country { get; set; }
			public int Items { get; set; }
		}

		public class UsersByCountry : AbstractIndexCreationTask<User, UsersByCountry.Result>
		{
			public class Result
			{
				public string Country { get; set; }
				public int Count { get; set; }
			}

			public UsersByCountry()
			{
				Map = users =>
				      from user in users
				      select new {user.Country, Count = 1};
				Reduce = results =>
				         from result in results
				         group result by result.Country
				         into g
				         select new
				         {
					         Country = g.Key,
					         Count = g.Sum(x => x.Count)
				         };
			}
		}

		public class ItemsByCountry : AbstractIndexCreationTask<User, UsersByCountry.Result>
		{
			public class Result
			{
				public string Country { get; set; }
				public int Count { get; set; }
			}

			public ItemsByCountry()
			{
				Map = users =>
					  from user in users
					  select new { user.Country, Count = user.Items };
				Reduce = results =>
						 from result in results
						 group result by result.Country
							 into g
							 select new
							 {
								 Country = g.Key,
								 Count = g.Sum(x => x.Count)
							 };
			}
		}

		[Theory]
        [PropertyData("Storages")]
        public void MixedCaseReduce(string requestedStorage)
		{
			using (var store = NewDocumentStore(requestedStorage: requestedStorage))
			{
				using (var session = store.OpenSession())
				{
					session.Store(new User { Country = "Israel" });
					session.Store(new User { Country = "ISRAEL" });
					session.Store(new User { Country = "israel" });
					session.SaveChanges();
				}

				new UsersByCountry().Execute(store);

				using (var session = store.OpenSession())
				{
					var r = session.Query<UsersByCountry.Result, UsersByCountry>()
						   .Customize(x => x.WaitForNonStaleResults())
						   .ToList();
					Assert.Equal(3, r.Count);
					Assert.Equal(1, r[0].Count);
					Assert.Equal(1, r[1].Count);
					Assert.Equal(1, r[2].Count);
				}
			}
		}

        [Theory]
        [PropertyData("Storages")]
		public void MixedCaseReduce_Complex(string requestedStorage)
		{
			using (var store = NewDocumentStore(requestedStorage: requestedStorage))
			{
				using (var session = store.OpenSession())
				{
					session.Store(new User { Id = "orders/5057", Country = "ISRAEL", Items = 7 });
					session.Store(new User { Id = "orders/5046", Country = "ISRAEL", Items = 3 });
					session.Store(new User { Id = "orders/4627", Country = "israel", Items = 4 });

					session.SaveChanges();
				}

				new ItemsByCountry().Execute(store);

				using (var session = store.OpenSession())
				{
					var r = session.Query<ItemsByCountry.Result, ItemsByCountry>()
						   .Customize(x => x.WaitForNonStaleResults())
						   .ToList()
						   .OrderBy(x => x.Country)
						   .ToList();
					Assert.Equal(2, r.Count);
					Assert.Equal(4, r[0].Count);
					Assert.Equal(10, r[1].Count);
				}
			}
		}
	}
}