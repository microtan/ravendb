﻿using System.Collections.Generic;
using System.Linq;
using Raven.Abstractions.Indexing;
using Raven.Client.Indexes;
using Raven.Tests.Helpers;
using Xunit;

namespace Raven.Tests.MailingList
{
    public class TestIndexAndTransforSubcollections : RavenTestBase
    {
        [Fact]
        public void CanTransformMultipleIndexResult()
        {
            using (var store = NewDocumentStore())                                                                        
            {
                new IndexSubCollection().Execute(store);
                new IndexSubCollectionResultTransformer().Execute(store);

                using (var session = store.OpenSession())
                {
                    session.Store(new ItemWithSubCollection
                    {
                        Name = "MyAggregate",
                        SubCollection = new[]
                        {
                            "SubItem1",
                            "SubItem2",
                            "SubItem3"
                        }
                    });
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var result = session.Query<IndexSubCollectionResult, IndexSubCollection>()
                        .Customize(customization => customization.WaitForNonStaleResultsAsOfNow()
                                .SetAllowMultipleIndexEntriesForSameDocumentToResultTransformer(true))
                        .TransformWith<IndexSubCollectionResultTransformer, IndexSubCollectionProjection>()
                        .ToList();

                    Assert.Equal(3, result.Count);
                    Assert.True(result.Any(x => x.TransformedSubItem == "transformed_SubItem1"));
                    Assert.True(result.Any(x => x.TransformedSubItem == "transformed_SubItem2"));
                    Assert.True(result.Any(x => x.TransformedSubItem == "transformed_SubItem3"));
                }
            }
        }
    }


    public class ItemWithSubCollection
    {
        public string Name { get; set; }
        public IList<string> SubCollection { get; set; }
    }

    public class IndexSubCollectionResult
    {
        public string Name { get; set; }
        public string SubItem { get; set; }
    }

    public class IndexSubCollectionProjection
    {
        public string TransformedSubItem { get; set; }
    }

    public class IndexSubCollection : AbstractIndexCreationTask<ItemWithSubCollection>
    {
        public IndexSubCollection()
        {
            Map = docs => from doc in docs
                          from subItem in doc.SubCollection
                          select new
                          {
                              doc.Name,
                              SubItem = subItem
                          };

            StoreAllFields(FieldStorage.Yes);
        }
    }

    public class IndexSubCollectionResultTransformer : AbstractTransformerCreationTask<IndexSubCollectionResult>
    {
        public IndexSubCollectionResultTransformer()
        {
            TransformResults = results => from result in results
                                          select new
                                          {
                                              TransformedSubItem = "transformed_" + result.SubItem
                                          };
        }
    }
}
