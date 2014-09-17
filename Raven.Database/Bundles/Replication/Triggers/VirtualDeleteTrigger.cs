//-----------------------------------------------------------------------
// <copyright file="VirtualDeleteTrigger.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System.ComponentModel.Composition;
using System.Threading;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Bundles.Replication.Impl;
using Raven.Database.Bundles.Replication.Impl;
using Raven.Database.Impl;
using Raven.Database.Plugins;
using Raven.Json.Linq;

namespace Raven.Bundles.Replication.Triggers
{
	/// <summary>
	/// We can't allow real deletes when using replication, because
	/// then we won't have any way to replicate the delete. Instead
	/// we allow the delete but don't do actual delete, we replace it 
	/// with a delete marker instead
	/// </summary>
	[ExportMetadata("Bundle", "Replication")]
	[ExportMetadata("Order", 10000)]
	[InheritedExport(typeof(AbstractDeleteTrigger))]
	public class VirtualDeleteTrigger : AbstractDeleteTrigger
	{
		readonly ThreadLocal<RavenJArray> deletedHistory = new ThreadLocal<RavenJArray>();

		public override void OnDelete(string key, TransactionInformation transactionInformation)
		{
			using (Database.DisableAllTriggersForCurrentThread())
			{
				var document = Database.Documents.Get(key, transactionInformation);

				if (document == null)
					return;

				if (document.IsConflictDocument() == false && HasConflict(document))
				{
					HandleConflictedDocument(document, transactionInformation);
					return;
				}

				HandleDocument(document);
			}
		}

		public override void AfterDelete(string key, TransactionInformation transactionInformation)
		{
			var metadata = new RavenJObject
			{
				{Constants.RavenDeleteMarker, true},
				{Constants.RavenReplicationHistory, deletedHistory.Value},
				{Constants.RavenReplicationSource, Database.TransactionalStorage.Id.ToString()},
				{Constants.RavenReplicationVersion, ReplicationHiLo.NextId(Database)}
			};
			deletedHistory.Value = null;

			Database.TransactionalStorage.Batch(accessor => 
				accessor.Lists.Set(Constants.RavenReplicationDocsTombstones, key, metadata, UuidType.Documents));
		}

		private void HandleConflictedDocument(JsonDocument document, TransactionInformation transactionInformation)
		{
			var conflicts = document.DataAsJson.Value<RavenJArray>("Conflicts");
			var currentSource = Database.TransactionalStorage.Id.ToString();

			foreach (var c in conflicts)
			{
				var conflict = Database.Documents.Get(c.Value<string>(), transactionInformation);
				var conflictSource = conflict.Metadata.Value<RavenJValue>(Constants.RavenReplicationSource).Value<string>();

				if (conflictSource != currentSource)
					continue;

				this.deletedHistory.Value = new RavenJArray
				{
					new RavenJObject
					{
						{ Constants.RavenReplicationVersion, conflict.Metadata[Constants.RavenReplicationVersion] },
						{ Constants.RavenReplicationSource, conflict.Metadata[Constants.RavenReplicationSource] }
					}
				};

				return;
			}
		}

		private void HandleDocument(JsonDocument document)
		{
			deletedHistory.Value = new RavenJArray(ReplicationData.GetHistory(document.Metadata))
			{
				new RavenJObject
				{
					{Constants.RavenReplicationVersion, document.Metadata[Constants.RavenReplicationVersion]},
					{Constants.RavenReplicationSource, document.Metadata[Constants.RavenReplicationSource]}
				}
			};
		}

		private bool HasConflict(JsonDocument document)
		{
			var conflict = document.Metadata.Value<RavenJValue>(Constants.RavenReplicationConflict);

			return conflict != null && conflict.Value<bool>() && document.DataAsJson.Value<RavenJArray>("Conflicts") != null;
		}
	}
}
