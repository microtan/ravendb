using Raven.Tests.Common;

using Xunit;
using System.Linq;

namespace Raven.Tests.Bugs
{
	public class ComplexDynamicQuery : RavenTest
	{
		[Fact]
		public void UsingNestedCollections()
		{
			using(var store = NewDocumentStore())
			{
				using(var s = store.OpenSession())
				{
					s.Advanced
                        .DocumentQuery<User>()
						.Where("Widgets,Sprockets,Name:Sprock01")
						.ToList();
				}
			}
		}
	}
}
