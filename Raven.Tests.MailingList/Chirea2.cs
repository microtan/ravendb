﻿// -----------------------------------------------------------------------
//  <copyright file="Chirea2.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Raven.Client.Indexes;
using Raven.Tests.Common;
using Xunit;

namespace Raven.Tests.MailingList
{
	public class Chirea2 : RavenTest
	{
		[Fact]
		public void MulitplyDecimal()
		{
			using (var store = NewDocumentStore())
			{
				var index = new Orders_Search();

				index.Execute(store);

				using (var session = store.OpenSession())
				{
					var orders = session.Query<Order>()
										.Customize(q => q.WaitForNonStaleResults())
										.ToList();

					foreach (var order in orders)
						session.Delete(order);

					session.SaveChanges();
				}

				using (var session = store.OpenSession())
				{
					session.Store(new Order
					{
						Products =
						{
							new Product
							{
								Price = 3.6m,
								Quantity = 5,
							},
							new Product
							{
								Price = 10.1m,
								Quantity = 2,
							},
						}
					});

					session.SaveChanges();
				}

				WaitForIndexing(store);
				Assert.Empty(store.DatabaseCommands.GetStatistics().Errors);
			}
		}

		public sealed class Product
		{
			public decimal Price
			{
				get;
				set;
			}

			public int Quantity
			{
				get;
				set;
			}
		}

		public sealed class Order
		{
			public IList<Product> Products
			{
				get;
				set;
			}

			public Order()
			{
				Products = new List<Product>();
			}
		}

		public sealed class Orders_Search : AbstractIndexCreationTask<Order>
		{
			public Orders_Search()
			{
				Map = orders => from o in orders
								select new
								{
									Total = o.Products.Sum(p => p.Price * p.Quantity),
								};
			}
		}
	}
}