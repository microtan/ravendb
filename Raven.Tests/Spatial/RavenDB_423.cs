// -----------------------------------------------------------------------
//  <copyright file="RavenDB_423.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using Raven.Abstractions.Exceptions;
using Raven.Abstractions.Indexing;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Spatial
{
	public class RavenDB_423 : RavenTest
	{
		[Fact]
		public void WillThrowOnSpatialGenerateInTransformResults()
		{
			using(var store = NewDocumentStore())
			{
				Assert.Throws<IndexCompilationException>(() => store.DatabaseCommands.PutIndex("test", new IndexDefinition
				{
					Map = "from doc in docs select new {}",
					TransformResults = "from result in results select new { _= SpatialIndex.Generate(result.x, result.Y)}"
				}));
			}
		}

		[Fact]
		public void WillThrowOnCreateFieldInTransformResults()
		{
			using (var store = NewDocumentStore())
			{
                Assert.Throws<IndexCompilationException>(() => store.DatabaseCommands.PutIndex("test", new IndexDefinition
				{
					Map = "from doc in docs select new {}",
					TransformResults = "from result in results select new { _= CreateField(result.x, result.Y)}"
				}));
			}
		}
	}
}