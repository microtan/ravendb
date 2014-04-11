﻿// -----------------------------------------------------------------------
//  <copyright file="QueueStorageActions.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using Raven.Abstractions.Util.Streams;
using Raven.Database.Util.Streams;

namespace Raven.Database.Storage.Voron.StorageActions
{
	using System;
	using System.Collections.Generic;

	using Raven.Abstractions.Data;
	using Raven.Abstractions.Extensions;
	using Raven.Database.Impl;
	using Raven.Database.Storage.Voron.Impl;
	using Raven.Json.Linq;

	using global::Voron;
	using global::Voron.Impl;

	public class QueueStorageActions : StorageActionsBase, IQueueStorageActions
	{
		private readonly TableStorage tableStorage;

		private readonly Reference<WriteBatch> writeBatch;

		private readonly IUuidGenerator generator;

        public QueueStorageActions(TableStorage tableStorage, IUuidGenerator generator, Reference<SnapshotReader> snapshot, Reference<WriteBatch> writeBatch, IBufferPool bufferPool)
			: base(snapshot, bufferPool)
		{
			this.tableStorage = tableStorage;
			this.writeBatch = writeBatch;
			this.generator = generator;
		}

		public void EnqueueToQueue(string name, byte[] data)
		{
			var queuesByName = tableStorage.Queues.GetIndex(Tables.Queues.Indices.ByName);
			var queuesData = tableStorage.Queues.GetIndex(Tables.Queues.Indices.Data);

			var id = generator.CreateSequentialUuid(UuidType.Queue);
			var key = CreateKey(name, id);

			tableStorage.Queues.Add(writeBatch.Value, key, new RavenJObject
			{
				{"name", name},
				{"id", id.ToByteArray()},
				{"reads", 0}
			}, 0);

			queuesData.Add(writeBatch.Value, key, data, 0);
			queuesByName.MultiAdd(writeBatch.Value, CreateKey(name), key);
		}

		public IEnumerable<Tuple<byte[], object>> PeekFromQueue(string name)
		{
			var queuesByName = tableStorage.Queues.GetIndex(Tables.Queues.Indices.ByName);

			using (var iterator = queuesByName.MultiRead(Snapshot, CreateKey(name)))
			{
				if (!iterator.Seek(Slice.BeforeAllKeys))
					yield break;

				do
				{
					var key = iterator.CurrentKey;

					ushort version;
					var value = LoadJson(tableStorage.Queues, key, writeBatch.Value, out version);

					if (value == null)
						yield break;

					var reads = value.Value<int>("reads");
					if (reads > 5) // read too much, probably poison message, remove it
					{
						DeleteQueue(key, value.Value<string>("name"));
						continue;
					}

					value["reads"] = reads + 1;
					tableStorage.Queues.Add(writeBatch.Value, key, value);

					yield return new Tuple<byte[], object>(ReadDataFromQueue(key), value.Value<byte[]>("id"));
				}
				while (iterator.MoveNext());
			}
		}

		public void DeleteFromQueue(string name, object id)
		{
			var queuesByName = tableStorage.Queues.GetIndex(Tables.Queues.Indices.ByName);

			var key = CreateKey(name, Etag.Parse((byte[])id));
			tableStorage.Queues.Delete(writeBatch.Value, key);
			queuesByName.MultiDelete(writeBatch.Value, CreateKey(name), key);
		}

		private void DeleteQueue(Slice key, string name)
		{
			var queuesData = tableStorage.Queues.GetIndex(Tables.Queues.Indices.Data);
			var queuesByName = tableStorage.Queues.GetIndex(Tables.Queues.Indices.ByName);

			tableStorage.Queues.Delete(writeBatch.Value, key);
			queuesData.Delete(writeBatch.Value, key);
			queuesByName.MultiDelete(writeBatch.Value, CreateKey(name), key);
		}

		private byte[] ReadDataFromQueue(Slice key)
		{
			var queuesData = tableStorage.Queues.GetIndex(Tables.Queues.Indices.Data);

			var read = queuesData.Read(Snapshot, key, writeBatch.Value);
			using (var stream = read.Reader.AsStream())
			{
				return stream.ReadData();
			}
		}
	}
}