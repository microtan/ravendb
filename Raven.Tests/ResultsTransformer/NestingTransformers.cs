﻿// -----------------------------------------------------------------------
//  <copyright file="NestingTransformers.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System.Linq;
using Raven.Client.Indexes;
using Raven.Json.Linq;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.ResultsTransformer
{
	public class NestingTransformers : RavenTest
	{
		public class Product
		{
			public string Id { get; set; }
			public string Name { get; set; }
		}

		public class ProductTransformer : AbstractTransformerCreationTask<Product>
		{
			public class Result
			{
				public string Name { get; set; }
			}
			public ProductTransformer()
			{
				TransformResults = products => from doc in products
											 select new
											 {
												Name = doc.Name.ToUpper()
											 };
			}
		}

		public class ProductTransformer2 : AbstractTransformerCreationTask<Product>
		{
			public class Result
			{
				public string Name { get; set; }
			}
			public ProductTransformer2()
			{
				TransformResults = products => from doc in products
											   select new
											   {
												   Name = doc.Name.Reverse()
											   };
			}
		}

		public class CallAnotherTransformerPerItem : AbstractTransformerCreationTask<Product>
		{
			public class Result
			{
				public Product Product { get; set; }
				public dynamic AnotherResult { get; set; }
			}

			public CallAnotherTransformerPerItem()
			{
				TransformResults = products => from doc in products
											   select new
											   {
												   Product = doc,
												   AnotherResult = TransformWith(Query("transformer").Value<string>(), doc)
											   };
			}
		}

		public class CallAnotherTransformerPerAllItems : AbstractTransformerCreationTask<Product>
		{
			public CallAnotherTransformerPerAllItems()
			{
				TransformResults = products => from doc in products
					from result in TransformWith(Query("transformer").Value<string>(), doc)
					select result;
			}
		}

		public class CallMultipleTransformerPerAllItems : AbstractTransformerCreationTask<Product>
		{
			public CallMultipleTransformerPerAllItems()
			{
				TransformResults = products => from doc in products
											   from result in TransformWith(Query("transformers").Value<string>().Split(';'), doc)
											   select result;
			}
		}


		[Fact]
		public void CanCallMultipleTransformers()
		{
			using (var store = NewRemoteDocumentStore())
			{
				new ProductTransformer().Execute(store);
				new ProductTransformer2().Execute(store);
				new CallMultipleTransformerPerAllItems().Execute(store);
				using (var session = store.OpenSession())
				{
					session.Store(new Product() { Id = "products/1", Name = "Irrelevant" });
					session.SaveChanges();
				}
				using (var session = store.OpenSession())
				{
					var result = session.Load<CallMultipleTransformerPerAllItems, ProductTransformer.Result>("products/1",
						configure => configure.AddQueryParam("transformers", "ProductTransformer;ProductTransformer2" ));
					Assert.Equal("TNAVELERRI", result.Name);
				}
			}
		}

		[Fact]
		public void CanCallTransformerPerItem()
		{
			using (var store = NewRemoteDocumentStore())
			{
				new ProductTransformer().Execute(store);
				new CallAnotherTransformerPerItem().Execute(store);
				using (var session = store.OpenSession())
				{
					session.Store(new Product() { Id = "products/1", Name = "Irrelevant" });
					session.SaveChanges();
				}
				using (var session = store.OpenSession())
				{
					var result = session.Load<CallAnotherTransformerPerItem, CallAnotherTransformerPerItem.Result>("products/1",
						configure => configure.AddQueryParam("transformer", "ProductTransformer"));
					Assert.Equal("IRRELEVANT", (string)result.AnotherResult[0].Name);
				}
			}

		}

		[Fact]
		public void CanCallTransformerAllItem()
		{
			using (var store = NewRemoteDocumentStore())
			{
				new ProductTransformer().Execute(store);
				new CallAnotherTransformerPerAllItems().Execute(store);
				using (var session = store.OpenSession())
				{
					session.Store(new Product() { Id = "products/1", Name = "Irrelevant" });
					session.SaveChanges();
				}
				using (var session = store.OpenSession())
				{
					var result = session.Load<CallAnotherTransformerPerAllItems, ProductTransformer.Result>("products/1",
						configure => configure.AddQueryParam("transformer", "ProductTransformer"));
					Assert.Equal("IRRELEVANT", result.Name);
				}
			}

		}

	}
}