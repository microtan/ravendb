using System.Collections.Generic;
using System.Linq;
using Raven.Client;
using Raven.Client.Embedded;
using Raven.Client.Indexes;
using Raven.Client.Linq;
using Raven.Client.Linq.Indexing;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.MailingList
{
	public class HierarchyTests : RavenTest
	{
		[Fact]
		public void CanQueryByNavigationItemHierarchy()
		{
			using (var documentStore = NewDocumentStore())
			{
				documentStore.Initialize();

				new Navigation_ByNavigationId().Execute(documentStore);

				SeedNavigationDocument(documentStore);

				using (var session = documentStore.OpenSession())
				{
					Assert.NotNull(session.Query<Navigation_ByNavigationId.Result, Navigation_ByNavigationId>()
						.Customize(x => x.WaitForNonStaleResults())
						.Where(x => x.NavigationId == "4")
						.As<Navigation>()
						.FirstOrDefault());

					Assert.NotNull(session.Query<Navigation_ByNavigationId.Result, Navigation_ByNavigationId>()
						.Customize(x => x.WaitForNonStaleResults())
						.Where(x => x.NavigationId == "3")
						.As<Navigation>()
						.FirstOrDefault());

					Assert.NotNull(session.Query<Navigation_ByNavigationId.Result, Navigation_ByNavigationId>()
						.Customize(x => x.WaitForNonStaleResults())
						.Where(x => x.NavigationId == "2")
						.As<Navigation>()
						.FirstOrDefault());

					Assert.NotNull(session.Query<Navigation_ByNavigationId.Result, Navigation_ByNavigationId>()
						.Customize(x => x.WaitForNonStaleResults())
						.Where(x => x.NavigationId == "1")
						.As<Navigation>()
						.FirstOrDefault());
				}
			}
		}

		private void SeedNavigationDocument(IDocumentStore documentStore)
		{
			var level4 = new NavigationItem { Id = "4", Name = "Level4" };
			var level3 = new NavigationItem { Id = "3", Name = "Level3" };
			var level2 = new NavigationItem { Id = "2", Name = "Level2" };
			var level1 = new NavigationItem { Id = "1", Name = "Level1" };

			level3.NavigationItems.Add(level4);
			level2.NavigationItems.Add(level3);
			level1.NavigationItems.Add(level2);

			var navigation = new Navigation { Customer = "MyCompany", Id = "navigations/1" };
			navigation.NavigationItems.Add(level1);

			using (var session = documentStore.OpenSession())
			{
				session.Store(navigation);
				session.SaveChanges();
			}
		}
	}

	public class Navigation_ByNavigationId : AbstractIndexCreationTask<Navigation, Navigation_ByNavigationId.Result>
	{
		public class Result
		{
			public string NavigationId { get; set; }
		}

		public Navigation_ByNavigationId()
		{
			Map = navigations => from navigation in navigations
								 select new
								 {
									 NavigationId = new object[]
			                     	{
										navigation.Id,
										Recurse(navigation, x=>x.NavigationItems.AsEnumerable())
											.Select(x=>x.Id)
			                     	}
								 };

		}
	}

	public class Navigation
	{
		public Navigation()
		{
			NavigationItems = new List<NavigationItem>();
		}

		public string Id { get; set; }

		public string Customer { get; set; }

		public IList<NavigationItem> NavigationItems { get; set; }
	}

	public class NavigationItem
	{
		public NavigationItem()
		{
			NavigationItems = new List<NavigationItem>();
		}

		public string Id { get; set; }

		public string Name { get; set; }

		public IList<NavigationItem> NavigationItems { get; set; }
	}
}