﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Client.Document;
using Raven.Client.Indexes;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.ResultsTransformer
{
    public class TransformerParametersToResultTransformer : RavenTest
    {
        public class Product
        {
            public string Id { get; set; }
            public string Name { get; set; }
			public string CategoryId { get; set; }
        }

		public class Category
		{
			public string Id { get; set; }
			public string Name { get; set; }
		}

        public class ProductWithParameter: AbstractTransformerCreationTask<Product>
        {
            public class Result
            {
                public string ProductId { get; set; }
                public string ProductName { get; set; }
                public string Input { get; set; }
            }
            public ProductWithParameter()
            {
                TransformResults = docs => from product in docs
                                             select new
                                             {
                                                 ProductId = product.Id,
                                                 ProductName = product.Name,
                                                 Input = Parameter("input")
                                             };
            }
        }

		public class ProductWithParametersAndInclude : AbstractTransformerCreationTask<Product>
		{
			public class Result
			{
				public string ProductId { get; set; }
				public string ProductName { get; set; }
				public string CategoryId { get; set; }
			}
			public ProductWithParametersAndInclude()
			{
				TransformResults = docs => from product in docs
										   let _ = Include(product.CategoryId)
										   select new
										   {
											   ProductId = product.Id,
											   ProductName = product.Name,
											   product.CategoryId,
										   };
			}
		}

        [Fact]
        public void CanUseResultsTransformerWithQueryOnLoad()
        {
            using (var store = NewRemoteDocumentStore())
            {
                new ProductWithParameter().Execute(store);
                using (var session = store.OpenSession())
                {
                    session.Store(new Product() { Id="products/1", Name = "Irrelevant"});
                    session.SaveChanges();
                }
                using (var session = store.OpenSession())
                {
                    var result = session.Load<ProductWithParameter, ProductWithParameter.Result>("products/1", 
                        configure => configure.AddTransformerParameter("input", "Foo"));
                    Assert.Equal("Foo", result.Input);
                }
            }
            
        }


        [Fact]
        public void CanUseResultsTransformerWithQueryOnLoadWithRemoteClient()
        {
            using (var store = NewRemoteDocumentStore())
            {
                new ProductWithParameter().Execute(store);
                using (var session = store.OpenSession())
                {
                    session.Store(new Product() { Id = "products/1", Name = "Irrelevant" });
                    session.SaveChanges();
                }
                using (var session = store.OpenSession())
                {
                    var result = session.Load<ProductWithParameter, ProductWithParameter.Result>("products/1", 
                        configure => configure.AddTransformerParameter("input", "Foo"));
                    Assert.Equal("Foo", result.Input);
                }
            }

        }

        [Fact]
        public void CanUseResultsTransformerWithQueryWithRemoteDatabase()
        {
            using (var store = NewRemoteDocumentStore())
            {
                new ProductWithParameter().Execute(store);
                using (var session = store.OpenSession())
                {
                    session.Store(new Product() { Name = "Irrelevant" });
                    session.SaveChanges();
                }
                using (var session = store.OpenSession())
                {
                    var result = session.Query<Product>()
                                .Customize(x => x.WaitForNonStaleResults())
                                .TransformWith<ProductWithParameter, ProductWithParameter.Result>()
                                .AddTransformerParameter("input", "Foo")
                                .Single();

                    Assert.Equal("Foo", result.Input);

                }
            }
        }

		[Fact]
		public void CanUseResultTransformerToLoadValueOnNonStoreFieldUsingQuery()
		{
			using (var store = NewRemoteDocumentStore())
			{
				new ProductWithParameter().Execute(store);
				using (var session = store.OpenSession())
				{
					session.Store(new Product() { Name = "Irrelevant" });
					session.SaveChanges();
				}
				using (var session = store.OpenSession())
				{
					var result = session.Query<Product>()
								.Customize(x => x.WaitForNonStaleResults())
								.TransformWith<ProductWithParameter, ProductWithParameter.Result>()
								.AddTransformerParameter("input", "Foo")
								.Single();

					Assert.Equal("Irrelevant", result.ProductName);

				}
			}
		}

        [Fact]
        public void CanUseResultsTransformerWithQuery()
        {
            using (var store = NewDocumentStore())
            {
               new ProductWithParameter().Execute(store);
                using (var session = store.OpenSession())
                {
                    session.Store(new Product() { Name = "Irrelevant" });
                    session.SaveChanges();
                }
                using (var session = store.OpenSession())
                {
                    var result = session.Query<Product>()
                                .Customize(x=> x.WaitForNonStaleResults())
                                .TransformWith<ProductWithParameter, ProductWithParameter.Result>()
                                .AddTransformerParameter("input", "Foo")
                                .Single();

                    Assert.Equal("Foo", result.Input);

                }
            }
        }

		[Fact]
		public void CanUseResultsTransformerWithInclude()
		{
			using (var store = NewDocumentStore())
			{
				new ProductWithParametersAndInclude().Execute(store);
				using (var session = store.OpenSession())
				{
					session.Store(new Product { Name = "Irrelevant", CategoryId = "Category/1"});
					session.Store(new Category{Id = "Category/1", Name = "don't know"});
					session.SaveChanges();
				}
				using (var session = store.OpenSession())
				{
					var result = session.Query<Product>()
								.Customize(x => x.WaitForNonStaleResults())
								.TransformWith<ProductWithParametersAndInclude, ProductWithParametersAndInclude.Result>()
								.Single();
					Assert.Equal(1, session.Advanced.NumberOfRequests);
					Assert.NotNull(result);
					var category = session.Load<Category>(result.CategoryId);
					Assert.Equal(1, session.Advanced.NumberOfRequests);
					Assert.NotNull(category);
				}
			}
		}
    }
}
