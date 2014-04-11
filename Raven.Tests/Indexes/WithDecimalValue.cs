using System.Linq;
using Raven.Client.Indexes;
using Raven.Tests.Common;
using Raven.Tests.Common.Util;

using Xunit;

namespace Raven.Tests.Indexes
{
	public class WithDecimalValue : RavenTest
	{
		public class Item
		{
			public decimal Value { get; set; }
		}

		public class Dec : AbstractIndexCreationTask<Item>
		{
			public Dec()
			{
				Map = items => from item in items
				               select new {A = item.Value*0.83M};
			}
		}

		[Fact]
		public void CanCreate()
		{
			using(var store = NewDocumentStore())
			{
				new Dec().Execute(store);
			}
		}

		[Fact]
		public void IgnoresLocale()
		{
			using (new TemporaryCulture("de"))
			{
				using (var store = NewDocumentStore())
				{
					new Dec().Execute(store);
				}
			}
		}
	}
}