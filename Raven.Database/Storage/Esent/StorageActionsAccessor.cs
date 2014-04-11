//-----------------------------------------------------------------------
// <copyright file="StorageActionsAccessor.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Isam.Esent.Interop;
using Raven.Abstractions;
using Raven.Abstractions.Data;
using Raven.Database.Storage;
using Raven.Database.Tasks;
using Raven.Storage.Esent.StorageActions;

namespace Raven.Storage.Esent
{
	[CLSCompliant(false)]
	public class StorageActionsAccessor : IStorageActionsAccessor
	{
		private readonly DocumentStorageActions inner;
		public DocumentStorageActions Inner
		{
			get { return inner; }
		}

		public event Action OnDispose;

		private readonly DateTime createdAt = SystemTime.UtcNow;
		[CLSCompliant(false)]
		public StorageActionsAccessor(DocumentStorageActions inner)
		{
			this.inner = inner;
		}

		public IDocumentStorageActions Documents
		{
			get { return inner; }
		}

		public IQueueStorageActions Queue
		{
			get { return inner; }
		}

		public ITasksStorageActions Tasks
		{
			get { return inner; }
		}

		public IStalenessStorageActions Staleness
		{
			get { return inner; }
		}

		public IListsStorageActions Lists
		{
			get { return inner;  }
		}

		public IAttachmentsStorageActions Attachments
		{
			get { return inner; }
		}

		public IIndexingStorageActions Indexing
		{
			get { return inner; }
		}

		public IGeneralStorageActions General
		{
			get { return inner; }
		}

		public IMappedResultsStorageAction MapReduce
		{
			get { return inner; }
		}

        public bool IsNested { get; set; }

	    public event Action OnStorageCommit
		{
			add { inner.OnStorageCommit += value; }
			remove { inner.OnStorageCommit -= value; }
		}

		public bool IsWriteConflict(Exception exception)
		{
			var esentErrorException = exception as EsentErrorException;
			if (esentErrorException == null)
				return false;
			switch (esentErrorException.Error)
			{
				case JET_err.WriteConflict:
				case JET_err.SessionWriteConflict:
				case JET_err.WriteConflictPrimaryIndex:
				case JET_err.KeyDuplicate:
					return true;
				default:
					return false;
			}
		}
		private readonly List<DatabaseTask> tasks = new List<DatabaseTask>();

		public T GetTask<T>(Func<T, bool> predicate, T newTask) where T : DatabaseTask
		{
			T task = tasks.OfType<T>().FirstOrDefault(predicate);
			if (task == null)
			{
				tasks.Add(newTask);
				return newTask;
			}
			return task;
		}

		private Action<JsonDocument[]> afterCommitAction;
		private List<JsonDocument> docsForCommit;
		public void AfterStorageCommitBeforeWorkNotifications(JsonDocument doc, Action<JsonDocument[]> afterCommit)
		{
			afterCommitAction = afterCommit;
			if(docsForCommit == null)
			{
				docsForCommit = new List<JsonDocument>();
				inner.OnStorageCommit += () => afterCommitAction(docsForCommit.ToArray());
			}
			docsForCommit.Add(doc);
		}

		[DebuggerHidden, DebuggerNonUserCode, DebuggerStepThrough]
		public void SaveAllTasks()
		{
			foreach (var task in tasks)
			{
				Tasks.AddTask(task, createdAt);
			}
		}

		public void Dispose()
	    {
			var onDispose = OnDispose;
			if (onDispose != null)
				onDispose();
	    }
	}
}
