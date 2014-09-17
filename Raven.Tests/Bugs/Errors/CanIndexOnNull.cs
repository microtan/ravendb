using System;
using Raven.Abstractions.Indexing;
using Raven.Json.Linq;
using Raven.Database.Indexing;
using Raven.Tests.Common;

using Xunit;
using System.Linq;

namespace Raven.Tests.Bugs.Errors
{
	public class CanIndexOnNull : RavenTest
	{
		[Fact]
		public void CanIndexOnMissingProps()
		{
			using(var store = NewDocumentStore())
			{
				store.DatabaseCommands.PutIndex("test",
				                                new IndexDefinition
				                                {
													Map = "from doc in docs select new { doc.Type, doc.Houses.Wheels} "
				                                });

				for (int i = 0; i < 50; i++)
				{
					store.DatabaseCommands.Put("item/" + i, null,
					                           new RavenJObject {{"Type", "Car"}}, new RavenJObject());
				}


				using(var s = store.OpenSession())
				{
                    s.Advanced.DocumentQuery<dynamic>("test")
						.WaitForNonStaleResults()
						.WhereGreaterThan("Wheels_Range", 4)
						.ToArray();
					
				}

				Assert.Empty(store.SystemDatabase.Statistics.Errors);
			}
		}
	}
}