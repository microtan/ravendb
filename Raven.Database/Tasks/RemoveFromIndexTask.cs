//-----------------------------------------------------------------------
// <copyright file="RemoveFromIndexTask.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using Raven.Abstractions.Data;
using Raven.Database.Indexing;
using System.Linq;
using Raven.Database.Storage;

namespace Raven.Database.Tasks
{
	public class RemoveFromIndexTask : DatabaseTask
	{
		public HashSet<string> Keys { get; set; }

        public override bool SeparateTasksByIndex
        {
            get { return true; }
        }

		public override string ToString()
		{
			return string.Format("Index: {0}, Keys: {1}", Index, string.Join(", ", Keys));
		}

		public RemoveFromIndexTask()
		{
			Keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		public override void Merge(DatabaseTask task)
		{
			var removeFromIndexTask = ((RemoveFromIndexTask)task);
			Keys.UnionWith(removeFromIndexTask.Keys);
		}

		public override void Execute(WorkContext context)
		{
			var keysToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				context.TransactionalStorage.Batch(accessor =>
				{
					keysToRemove = new HashSet<string>(Keys.Where(key=>FilterDocuments(context, accessor, key)));
					accessor.Indexing.TouchIndexEtag(Index);
				});
				if (keysToRemove.Count == 0)
					return;
				context.IndexStorage.RemoveFromIndex(Index, keysToRemove.ToArray(), context);
			}
			finally
			{
				context.MarkAsRemovedFromIndex(keysToRemove);
			}
		}

		/// <summary>
		/// We need to NOT remove documents that has been removed then added.
		/// We DO remove documents that would be filtered out because of an Entity Name changed, though.
		/// </summary>
		private bool FilterDocuments(WorkContext context, IStorageActionsAccessor accessor, string key)
		{
			var documentMetadataByKey = accessor.Documents.DocumentMetadataByKey(key, null);
			if (documentMetadataByKey == null)
				return true;
			var generator = context.IndexDefinitionStorage.GetViewGenerator(Index);
			if (generator == null)
				return false;

			if (generator.ForEntityNames.Count == 0)
				return false;// there is a new document and this index applies to it

			var entityName = documentMetadataByKey.Metadata.Value<string>(Constants.RavenEntityName);
			if (entityName == null)
				return true; // this document doesn't belong to this index any longer, need to remove it

			return generator.ForEntityNames.Contains(entityName) == false;
		}

		public override DatabaseTask Clone()
		{
			return new RemoveFromIndexTask
			{
				Keys = new HashSet<string>(Keys),
				Index = Index,
			};
		}
	}
}
