
using Raven.Abstractions.Data;
using Raven.Database.Data;
using Raven.Database.Json;
using Raven.Json.Linq;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Patching
{
	public class MetadataPatching : RavenTest
	{
		[Fact]
		public void ChangeRavenEntityName()
		{
			using (var store = NewDocumentStore())
			{
				store.SystemDatabase.Documents.Put("foos/1", null, RavenJObject.Parse("{'Something':'something'}"),
					RavenJObject.Parse("{'Raven-Entity-Name': 'Foos'}"), null);
				WaitForIndexing(store);
				var operation = store.DatabaseCommands.UpdateByIndex("Raven/DocumentsByEntityName",
					new IndexQuery(), new[]
					{
						new PatchRequest
						{
							Type = PatchCommandType.Modify,
							Name = "@metadata",
							Nested = new []
							{
								new PatchRequest
								{
									Type = PatchCommandType.Set,
									Name = "Raven-Entity-Name",
									Value = new RavenJValue("Bars")
								}
							}
						}
							
					}, false);

				operation.WaitForCompletion();

				var jsonDocument = store.SystemDatabase.Documents.Get("foos/1", null);
				Assert.Equal("Bars", jsonDocument.Metadata.Value<string>("Raven-Entity-Name"));
			}
		}
	}
}
