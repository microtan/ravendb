//-----------------------------------------------------------------------
// <copyright file="Documents.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using Raven.Abstractions.Data;
using Raven.Json.Linq;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Storage
{
	public class Documents : RavenTest
	{
		[Fact]
		public void CanAddAndRead()
		{
			using (var tx = NewTransactionalStorage())
			{
				tx.Batch(mutator => mutator.Documents.AddDocument("Ayende", null, RavenJObject.FromObject(new { Name = "Rahien" }), new RavenJObject()));

				RavenJObject document = null;
				tx.Batch(viewer =>
				{
					document = viewer.Documents.DocumentByKey("Ayende", null).DataAsJson;
				});

				Assert.Equal("Rahien", document.Value<string>("Name"));
			}
		}

		[Fact]
		public void CanUpdateDocumentThenReadIt()
		{
			using (var tx = NewTransactionalStorage())
			{
				tx.Batch(mutator => mutator.Documents.AddDocument("Ayende", null, RavenJObject.FromObject(new { Name = "Rahien" }), new RavenJObject()));

				tx.Batch(mutator => mutator.Documents.AddDocument("Ayende", null, RavenJObject.FromObject(new { Name = "Oren" }), new RavenJObject()));

				tx.Batch(x => Assert.Equal(1, x.Documents.GetDocumentsCount()));

				RavenJObject document = null;
				tx.Batch(viewer =>
				{
					document = viewer.Documents.DocumentByKey("Ayende", null).DataAsJson;
				});

				Assert.Equal("Oren", document.Value<string>("Name"));
			}
		}


		[Fact]
		public void CanUpdateDocumentThenReadItWhenThereAreManyDocs()
		{
			using (var tx = NewTransactionalStorage())
			{
				for (int i = 0; i < 11; i++)
				{
					tx.Batch(mutator => mutator.Documents.AddDocument("docs/"+i, null, RavenJObject.FromObject(new { Name = "Rahien" }), new RavenJObject()));
					
				}

				tx.Batch(mutator => mutator.Documents.AddDocument("docs/0", null, RavenJObject.FromObject(new { Name = "Oren" }), new RavenJObject()));

				tx.Batch(x => Assert.Equal(11, x.Documents.GetDocumentsCount()));

				RavenJObject document = null;
				tx.Batch(viewer =>
				{
					document = viewer.Documents.DocumentByKey("docs/0", null).DataAsJson;
				});

				Assert.Equal("Oren", document.Value<string>("Name"));
			}
		}

		[Fact]
		public void CanAddAndReadFileAfterReopen()
		{
			var dataDir = NewDataPath();

			using (var tx = NewTransactionalStorage(dataDir: dataDir, runInMemory: false))
			{
				tx.Batch(mutator => mutator.Documents.AddDocument("Ayende", null, RavenJObject.FromObject(new { Name = "Rahien" }), new RavenJObject()));
			}

			using (var tx = NewTransactionalStorage(dataDir: dataDir, runInMemory: false))
			{
				RavenJObject document = null;
				tx.Batch(viewer =>
				{
					document = viewer.Documents.DocumentByKey("Ayende", null).DataAsJson;
				});

				Assert.Equal("Rahien", document.Value<string>("Name"));
			}
		}

		[Fact]
		public void CanDeleteFile()
		{
			var dataDir = NewDataPath();

			using (var tx = NewTransactionalStorage(dataDir: dataDir))
			{
				tx.Batch(mutator => mutator.Documents.AddDocument("Ayende", null, RavenJObject.FromObject(new { Name = "Rahien" }), new RavenJObject()));
				RavenJObject metadata;
				Etag deletedETag;
				tx.Batch(mutator => mutator.Documents.DeleteDocument("Ayende", null, out metadata, out deletedETag));
			}

			using (var tx = NewTransactionalStorage(dataDir: dataDir))
			{
				tx.Batch(viewer => Assert.Null(viewer.Documents.DocumentByKey("Ayende", null)));

			}
		}

		[Fact]
		public void CanCountDocuments()
		{
			var dataDir = NewDataPath();

			using (var tx = NewTransactionalStorage(dataDir: dataDir))
			{
				tx.Batch(mutator => mutator.Documents.AddDocument("Ayende", null, RavenJObject.FromObject(new { Name = "Rahien" }), new RavenJObject()));
				tx.Batch(accessor => Assert.Equal(1, accessor.Documents.GetDocumentsCount()));
				RavenJObject metadata;
				Etag tag;
				tx.Batch(mutator => mutator.Documents.DeleteDocument("Ayende", null, out metadata, out tag));

				tx.Batch(accessor => Assert.Equal(0, accessor.Documents.GetDocumentsCount()));

			}

			using (var tx = NewTransactionalStorage(dataDir: dataDir))
			{
				tx.Batch(viewer => Assert.Null(viewer.Documents.DocumentByKey("Ayende", null)));
				tx.Batch(accessor => Assert.Equal(0, accessor.Documents.GetDocumentsCount()));
			}
		}
	}
}