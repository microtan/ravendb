using System.Linq;
using Raven.Abstractions.Data;
using Raven.Abstractions.Indexing;
using Raven.Client.Linq;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Bugs
{
	public class Distinct : RavenTest
	{
		[Fact]
		public void CanQueryForDistinctItems()
		{
			using (var store = NewDocumentStore())
			{
				using (var s = store.OpenSession())
				{
					s.Store(new { Name = "ayende" });
					s.Store(new { Name = "ayende" });
					s.Store(new { Name = "rahien" });
					s.SaveChanges();
				}

				store.SystemDatabase.Indexes.PutIndex("test", new IndexDefinition
				{
					Map = "from doc in docs select new { doc.Name }",
					Stores = { { "Name", FieldStorage.Yes } }
				});

				using (var s = store.OpenSession())
				{
                    var objects = s.Advanced.DocumentQuery<dynamic>("test")
						.WaitForNonStaleResults()
						.SelectFields<dynamic>("Name")
						.OrderBy("Name")
						.Distinct()
						.OrderBy("Name")
						.ToList();

					Assert.Equal(2, objects.Count);
					Assert.Equal("ayende", objects[0].Name);
					Assert.Equal("rahien", objects[1].Name);
				}
			}
		}

		[Fact]
		public void CanQueryForDistinctItemsUsingLinq()
		{
			using (var store = NewDocumentStore())
			{
				using (var s = store.OpenSession())
				{
					s.Store(new { Name = "ayende" });
					s.Store(new { Name = "ayende" });
					s.Store(new { Name = "rahien" });
					s.SaveChanges();
				}

				store.SystemDatabase.Indexes.PutIndex("test", new IndexDefinition
				{
					Map = "from doc in docs select new { doc.Name }",
					Stores = { { "Name", FieldStorage.Yes } }
				});

				using (var s = store.OpenSession())
				{
					var objects = s.Query<User>("test")
						.Customize(x => x.WaitForNonStaleResults())
						.Select(o => new {o.Name }).OrderBy(o => o.Name)
						.Select(o => new { o.Name })
						.Distinct()
						.ToList();

					Assert.Equal(2, objects.Count);
					Assert.Equal("ayende", objects[0].Name);
					Assert.Equal("rahien", objects[1].Name);
				}
			}
		}
		[Fact]
		public void CanQueryForDistinctItemsUsingLinq_WithPaging()
		{
			using (var store = NewDocumentStore())
			{
				using (var s = store.OpenSession())
				{
					s.Store(new { Name = "ayende" });
					s.Store(new { Name = "ayende" });
					s.Store(new { Name = "rahien" });
					s.SaveChanges();
				}

				store.SystemDatabase.Indexes.PutIndex("test", new IndexDefinition
				{
					Map = "from doc in docs select new { doc.Name }",
					Stores = { { "Name", FieldStorage.Yes } }
				});

				using (var s = store.OpenSession())
				{
					var objects = s.Query<User>("test")
						.Customize(x => x.WaitForNonStaleResults())
						.OrderBy(x => x.Name)
						.Select(o => new { o.Name })
						.Distinct()
						.OrderBy(o => o.Name)
						.Skip(1)
						.ToList();

					Assert.Equal(1, objects.Count);
					Assert.Equal("rahien", objects[0].Name);
				}
			}
		}
		[Fact]
		public void CanQueryForDistinctItemsAndProperlyPage()
		{
			using (var store = NewDocumentStore())
			{
				using (var s = store.OpenSession())
				{
					s.Store(new { Name = "ayende" });
					s.Store(new { Name = "ayende" });
					s.Store(new { Name = "rahien" });
					s.SaveChanges();
				}

				store.SystemDatabase.Indexes.PutIndex("test", new IndexDefinition
				{
					Map = "from doc in docs select new { doc.Name }",
					Stores = { { "Name", FieldStorage.Yes } }
				});

				using (var s = store.OpenSession())
				{
                    var objects = s.Advanced.DocumentQuery<dynamic>("test")
						.WaitForNonStaleResults()
						.OrderBy("Name")
						.Skip(1)
						.OrderBy("Name")
						.SelectFields<dynamic>("Name")
						.Distinct()
						.ToList();

					Assert.Equal(1, objects.Count);
					Assert.Equal("rahien", objects[0].Name);
				}
			}
		}
	}
}
