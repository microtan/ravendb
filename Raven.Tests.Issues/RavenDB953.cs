﻿// -----------------------------------------------------------------------
//  <copyright file="RavenDB953.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System.Linq;
using Raven.Abstractions.Indexing;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Issues
{
	public class RavenDB953 : RavenTest
	{
		[Fact]
		public void PutTransformerAsyncSucceedsWhenExistingDefinitionHasError()
		{
			using (var store = NewRemoteDocumentStore())
			{
				// put an index containing an intentional mistake (calling LoadDocument is not permitted in TransformResults)
				store.DatabaseCommands.PutTransformer("test",
																new TransformerDefinition()
																{
																	Name = "test",
																	TransformResults = "from result in results select new {Doc = LoadDocument(result.Id)}"
																});


				using (var s = store.OpenSession())
				{
					var entity = new { Id = "MyId" };
					s.Store(entity);
					s.SaveChanges();

					try
					{
                        s.Advanced.DocumentQuery<dynamic>("test")
							.WaitForNonStaleResultsAsOfNow()
							.Where("Id:MyId")
							.ToList();
					}
					catch
					{
						// ignore - we know it will fail
					}
				}

				// now try to put the correct index definition
				store.AsyncDatabaseCommands.PutTransformerAsync("test",
								new TransformerDefinition()
								{
									Name = "test",
									TransformResults = "from result in results select new {Doc = LoadDocument(result.Id)}"
								}).Wait();


			}
		}

	}
}