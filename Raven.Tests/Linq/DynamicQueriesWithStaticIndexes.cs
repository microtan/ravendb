﻿using System.Collections.Generic;
using Raven.Abstractions.Indexing;
using Raven.Client;
using Raven.Tests.Common;

using Xunit;
using Raven.Client.Linq;
using System.Linq;

namespace Raven.Tests.Linq
{

	public class DynamicQueriesWithStaticIndexes : RavenTest
	{

		[Fact]
		public void DynamicQueryWillInterpretFieldNamesProperly()
		{

			using (var store = NewDocumentStore())
			{

				using (var session = store.OpenSession())
				{

					var foo = new Foo()
					{

						SomeProperty = "Some Data",
						Bar = new Bar() { SomeDictionary = new Dictionary<string, string>() { { "KeyOne", "ValueOne" }, { "KeyTwo", "ValueTwo" } } }

					};

					session.Store(foo);

					foo = new Foo()
					{

						SomeProperty = "Some More Data",

					};

					session.Store(foo);

					foo = new Foo()
					{

						SomeProperty = "Some Even More Data",
						Bar = new Bar() { SomeDictionary = new Dictionary<string, string>() { { "KeyThree", "ValueThree" } } }

					};

					session.Store(foo);

					foo = new Foo()
					{

						SomeProperty = "Some Even More Data",
						Bar = new Bar() { SomeOtherDictionary = new Dictionary<string, string>() { { "KeyFour", "ValueFour" } } }

					};

					session.Store(foo);

					session.SaveChanges();

					store.DatabaseCommands.PutIndex("Foos/TestDynamicQueries", new IndexDefinition()
					{
						Map = @"from doc in docs.Foos
                                from docBarSomeOtherDictionaryItem in ((IEnumerable<dynamic>)doc.Bar.SomeOtherDictionary).DefaultIfEmpty()
                                from docBarSomeDictionaryItem in ((IEnumerable<dynamic>)doc.Bar.SomeDictionary).DefaultIfEmpty()
                                select new
                                {
                                    Bar_SomeOtherDictionary_Value = docBarSomeOtherDictionaryItem.Value,
                                    Bar_SomeOtherDictionary_Key = docBarSomeOtherDictionaryItem.Key,
                                    Bar_SomeDictionary_Value = docBarSomeDictionaryItem.Value,
                                    Bar_SomeDictionary_Key = docBarSomeDictionaryItem.Key,
                                    Bar = doc.Bar
                                }"
					}, true);

					RavenQueryStatistics stats;

					var result = session.Query<Foo>("Foos/TestDynamicQueries")
						.Where(x =>
							x.Bar.SomeDictionary.Any(y => y.Key == "KeyOne" && y.Value == "ValueOne") ||
								x.Bar.SomeOtherDictionary.Any(y => y.Key == "KeyFour" && y.Value == "ValueFour") ||
									x.Bar == null)
										.Customize(x => x.WaitForNonStaleResults())
											.Statistics(out stats).ToList();

					Assert.Equal(result.Count, 3);

				}

			}

		}

	}

	public class Foo
	{

		public string SomeProperty { get; set; }

		public Bar Bar { get; set; }

	}

	public class Bar
	{

		public Dictionary<string, string> SomeDictionary { get; set; }
		public Dictionary<string, string> SomeOtherDictionary { get; set; }

	}

}
