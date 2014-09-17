//-----------------------------------------------------------------------
// <copyright file="DeleteTriggers.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System.ComponentModel.Composition.Hosting;
using Raven.Client.Embedded;
using Raven.Json.Linq;
using Raven.Database;
using Raven.Database.Config;
using Raven.Tests.Common;
using Raven.Tests.Storage;
using Xunit;

namespace Raven.Tests.Triggers
{
	public class DeleteTriggers : RavenTest
	{
		private readonly EmbeddableDocumentStore store;
		private readonly DocumentDatabase db;

		public DeleteTriggers()
		{
			store = NewDocumentStore( catalog: (new TypeCatalog(typeof (CascadeDeleteTrigger))));
			db = store.SystemDatabase;
		}

		public override void Dispose()
		{
			store.Dispose();
			base.Dispose();
		}

		[Fact]
		public void CanCascadeDeletes()
		{
			db.Documents.Put("abc", null, RavenJObject.Parse("{name: 'a'}"), RavenJObject.Parse("{'Cascade-Delete': 'def'}"), null);
			db.Documents.Put("def", null, RavenJObject.Parse("{name: 'b'}"), new RavenJObject(), null);

			db.Documents.Delete("abc", null, null);

			Assert.Null(db.Documents.Get("def", null));
		}
		
	}
}
