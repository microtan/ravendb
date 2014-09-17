//-----------------------------------------------------------------------
// <copyright file="PendingTransactionRecovery.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.IsolatedStorage;
using System.Transactions;
using Raven.Abstractions.Logging;
using Raven.Client.Connection;
using Raven.Abstractions.Extensions;

namespace Raven.Client.Document.DTC
{
	public class PendingTransactionRecovery
	{
		private readonly DocumentStore documentStore;
		private static readonly ILog logger = LogManager.GetCurrentClassLogger();

		public PendingTransactionRecovery(DocumentStore documentStore)
		{
			this.documentStore = documentStore;
		}

		public void Execute(Guid myResourceManagerId, IDatabaseCommands commands)
		{
			var resourceManagersRequiringRecovery = new HashSet<Guid>();
			var filesToDelete = new List<string>();
			using (var ctx = documentStore.TransactionRecoveryStorage.Create())
			{
				foreach (var file in ctx.GetFileNames("*.recovery-information"))
				{
				    var txId = string.Empty;
					try
					{
						Stream stream;
						try
						{
							stream = ctx.OpenRead(file);
						}
						catch (Exception e)
						{
							logger.WarnException(
								"Could not open recovery information: " + file +
								", this is expected if it is an active transaction / held by another server", e);
							continue;
						}
						using (stream)
						using (var reader = new BinaryReader(stream))
						{
							var resourceManagerId = new Guid(reader.ReadString());

							if (myResourceManagerId != resourceManagerId)
								continue; // it doesn't belong to us, ignore
							filesToDelete.Add(file);
							txId = reader.ReadString();

							var db = reader.ReadString();

							var dbCmds = string.IsNullOrEmpty(db) == false
											 ? commands.ForDatabase(db)
											 : commands.ForSystemDatabase();

							TransactionManager.Reenlist(resourceManagerId, stream.ReadData(), new InternalEnlistment(dbCmds, txId));
							resourceManagersRequiringRecovery.Add(resourceManagerId);
							logger.Info("Recovered transaction {0}", txId);
						}
					}
					catch (Exception e)
					{
						logger.WarnException("Could not re-enlist in DTC transaction for tx: " + txId, e);
					}
				}

				foreach (var rm in resourceManagersRequiringRecovery)
				{
					try
					{
						TransactionManager.RecoveryComplete(rm);
					}
					catch (Exception e)
					{
						logger.WarnException("Could not properly complete recovery of resource manager: " + rm, e);
					}
				}

				var errors = new List<Exception>();
				foreach (var file in filesToDelete)
				{
					try
					{
						ctx.DeleteFile(file);
					}
					catch (Exception e)
					{
						errors.Add(e);
					}
				}
				if (errors.Count > 0)
					throw new AggregateException(errors);
			}
		}

		public class InternalEnlistment : IEnlistmentNotification
		{
			private readonly IDatabaseCommands database;
			private readonly string txId;

			public InternalEnlistment(IDatabaseCommands database, string txId)
			{
				this.database = database;
				this.txId = txId;
			}

			public void Prepare(PreparingEnlistment preparingEnlistment)
			{
				// shouldn't be called, already 
				// prepared, otherwise we won't have this issue
				preparingEnlistment.Prepared();
			}

			public void Commit(Enlistment enlistment)
			{
				database.Commit(txId);
				enlistment.Done();
			}

			public void Rollback(Enlistment enlistment)
			{
				database.Rollback(txId);
				enlistment.Done();
			}

			public void InDoubt(Enlistment enlistment)
			{
				database.Rollback(txId);
				enlistment.Done();
			}
		}
	}
}