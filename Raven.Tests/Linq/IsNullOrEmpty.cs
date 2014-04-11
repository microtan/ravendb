﻿using System.Linq;

using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Linq
{
	public class IsNullOrEmpty : RavenTest
	{
		private class TestDoc
		{
			public string SomeProperty { get; set; }
		}

		[Fact]
		public void IsNullOrEmptyEqTrue()
		{
			using (var store = NewDocumentStore())
			{
				using (var session = store.OpenSession())
				{
					session.Store(new TestDoc { SomeProperty = "Has some content" });
					session.Store(new TestDoc { SomeProperty = "" });
					session.Store(new TestDoc { SomeProperty = null });
					session.SaveChanges();
				}

				using (var session = store.OpenSession())
				{
					Assert.Equal(2, session.Query<TestDoc>().Count(p => string.IsNullOrEmpty(p.SomeProperty)));
				}

				WaitForUserToContinueTheTest(store);
			}
		}

		[Fact]
		public void IsNullOrEmptyEqFalse()
		{
			using (var store = NewDocumentStore())
			{
				using (var session = store.OpenSession())
				{
					session.Store(new TestDoc { SomeProperty = "Has some content" });
					session.Store(new TestDoc { SomeProperty = "" });
					session.Store(new TestDoc { SomeProperty = null });
					session.SaveChanges();
				}

				using (var session = store.OpenSession())
				{
					Assert.Equal(1, session.Query<TestDoc>().Count(p => string.IsNullOrEmpty(p.SomeProperty) == false));
				}
			}
		}

		[Fact]
		public void IsNullOrEmptyNegated()
		{
			using (var store = NewDocumentStore())
			{
				using (var session = store.OpenSession())
				{
					session.Store(new TestDoc { SomeProperty = "Has some content" });
					session.Store(new TestDoc { SomeProperty = "" });
					session.Store(new TestDoc { SomeProperty = null });
					session.SaveChanges();
				}
				
				using (var session = store.OpenSession())
				{
					Assert.Equal(1, session.Query<TestDoc>().Count(p => !string.IsNullOrEmpty(p.SomeProperty)));
				}
			}
		}

		[Fact]
		public void WithAny()
		{
			using (var store = NewDocumentStore())
			{
				using (var session = store.OpenSession())
				{
					session.Store(new TestDoc { SomeProperty = "Has some content" });
					session.Store(new TestDoc { SomeProperty = "" });
					session.Store(new TestDoc { SomeProperty = null });
					session.SaveChanges();
				}

				using (var session = store.OpenSession())
				{
					Assert.Equal(1, session.Query<TestDoc>().Count(p => p.SomeProperty.Any()));
				}
			}
		}

		[Fact]
		public void WithAnyEqFalse()
		{
			using (var store = NewDocumentStore())
			{
				using (var session = store.OpenSession())
				{
					session.Store(new TestDoc { SomeProperty = "Has some content" });
					session.Store(new TestDoc { SomeProperty = "" });
					session.Store(new TestDoc { SomeProperty = null });
					session.SaveChanges();
				}

				using (var session = store.OpenSession())
				{
					Assert.Equal(2, session.Query<TestDoc>().Count(p => p.SomeProperty.Any() == false));
				}
			}
		}
	}
}