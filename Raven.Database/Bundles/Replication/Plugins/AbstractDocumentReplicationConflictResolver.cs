using System;
using System.ComponentModel.Composition;
using Raven.Abstractions.Data;
using Raven.Json.Linq;

namespace Raven.Bundles.Replication.Plugins
{
	[InheritedExport]
	public abstract class AbstractDocumentReplicationConflictResolver
	{
		public abstract bool TryResolve(string id, RavenJObject metadata, RavenJObject document, JsonDocument existingDoc,
		                                Func<string, JsonDocument> getDocument, out RavenJObject metadataToSave,
		                                out RavenJObject documentToSave);
	}
}