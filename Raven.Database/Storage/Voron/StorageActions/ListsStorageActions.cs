﻿// -----------------------------------------------------------------------
//  <copyright file="ListsStorageActions.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using Raven.Abstractions.Util.Streams;
using Raven.Database.Util.Streams;

namespace Raven.Database.Storage.Voron.StorageActions
{
	using System.Collections.Generic;
	using System.IO;

	using Raven.Abstractions.Data;
	using Raven.Abstractions.Extensions;
	using Raven.Database.Impl;
	using Raven.Database.Storage.Voron.Impl;
	using Raven.Json.Linq;

	using global::Voron;
	using global::Voron.Impl;

	public class ListsStorageActions : StorageActionsBase, IListsStorageActions
	{
		private readonly TableStorage tableStorage;

		private readonly IUuidGenerator generator;

		private readonly Reference<WriteBatch> writeBatch;

        public ListsStorageActions(TableStorage tableStorage, IUuidGenerator generator, Reference<SnapshotReader> snapshot, Reference<WriteBatch> writeBatch, IBufferPool bufferPool)
			: base(snapshot, bufferPool)
		{
			this.tableStorage = tableStorage;
			this.generator = generator;
			this.writeBatch = writeBatch;
		}

		public void Set(string name, string key, RavenJObject data, UuidType type)
		{
			var listsByName = tableStorage.Lists.GetIndex(Tables.Lists.Indices.ByName);
			var listsByNameAndKey = tableStorage.Lists.GetIndex(Tables.Lists.Indices.ByNameAndKey);

			var etag = generator.CreateSequentialUuid(type);
			var etagAsString = etag.ToString();

			tableStorage.Lists.Add(
				writeBatch.Value,
				etagAsString,
				new RavenJObject
				{
					{ "name", name }, 
					{ "key", key }, 
					{ "etag", etag.ToByteArray() }, 
					{ "data", data }
				});

			listsByName.MultiAdd(writeBatch.Value, CreateKey(name), etagAsString);
			listsByNameAndKey.Add(writeBatch.Value, CreateKey(name, key), etagAsString);
		}

		public void Remove(string name, string key)
		{
			var listsByName = tableStorage.Lists.GetIndex(Tables.Lists.Indices.ByName);
			var listsByNameAndKey = tableStorage.Lists.GetIndex(Tables.Lists.Indices.ByNameAndKey);

			var nameAndKey = CreateKey(name, key);

			var read = listsByNameAndKey.Read(Snapshot, nameAndKey, writeBatch.Value);
			if (read == null)
				return;

			using (var stream = read.Reader.AsStream())
			{
				using (var reader = new StreamReader(stream))
				{
					var etag = reader.ReadToEnd();
					tableStorage.Lists.Delete(writeBatch.Value, etag);
					listsByName.MultiDelete(writeBatch.Value, CreateKey(name), etag);
					listsByNameAndKey.Delete(writeBatch.Value, nameAndKey);
				}
			}
		}

		public IEnumerable<ListItem> Read(string name, Etag start, Etag end, int take)
		{
			var listsByName = tableStorage.Lists.GetIndex(Tables.Lists.Indices.ByName);

			using (var iterator = listsByName.MultiRead(Snapshot, CreateKey(name)))
			{
				if (!iterator.Seek(start.ToString()))
					yield break;

				int count = 0;

				do
				{
					var etag = Etag.Parse(iterator.CurrentKey.ToString());
					if (start.CompareTo(etag) >= 0)
						continue;

					if (end != null && end.CompareTo(etag) <= 0)
						yield break;

					count++;
					yield return ReadInternal(etag);
				}
				while (iterator.MoveNext() && count < take);
			}
		}

		public ListItem Read(string name, string key)
		{
			var listsByNameAndKey = tableStorage.Lists.GetIndex(Tables.Lists.Indices.ByNameAndKey);
			var nameAndKey = CreateKey(name, key);

			var read = listsByNameAndKey.Read(Snapshot, nameAndKey, writeBatch.Value);
			if (read == null)
				return null;

			using (var stream = read.Reader.AsStream())
			{
				using (var reader = new StreamReader(stream))
				{
					var etag = reader.ReadToEnd();
					return ReadInternal(etag);
				}
			}
		}

	    public ListItem ReadLast(string name)
	    {
            var listsByName = tableStorage.Lists.GetIndex(Tables.Lists.Indices.ByName);
	        var nameKey = CreateKey(name);

	        using (var iterator = listsByName.MultiRead(Snapshot, nameKey))
	        {
                if (!iterator.Seek(Slice.AfterAllKeys))
                    return null;

                var etag = Etag.Parse(iterator.CurrentKey.ToString());

	            return ReadInternal(etag);
	        }
	    }

	    public void RemoveAllBefore(string name, Etag etag)
		{
			var listsByName = tableStorage.Lists.GetIndex(Tables.Lists.Indices.ByName);
			var listsByNameAndKey = tableStorage.Lists.GetIndex(Tables.Lists.Indices.ByNameAndKey);

			var nameKey = CreateKey(name);

			using (var iterator = listsByName.MultiRead(Snapshot, nameKey))
			{
				if (!iterator.Seek(Slice.BeforeAllKeys))
					return;

				do
				{
					var currentEtag = Etag.Parse(iterator.CurrentKey.ToString());

					if (currentEtag.CompareTo(etag) <= 0)
					{
						ushort version;
						var value = LoadJson(tableStorage.Lists, iterator.CurrentKey, writeBatch.Value, out version);

						var key = value.Value<string>("key");

						tableStorage.Lists.Delete(writeBatch.Value, currentEtag.ToString());
                        listsByName.MultiDelete(writeBatch.Value, nameKey, currentEtag.ToString());
						listsByNameAndKey.Delete(writeBatch.Value, CreateKey(name, key));
					}
				}
				while (iterator.MoveNext());
			}
		}

		private ListItem ReadInternal(string id)
		{
			ushort version;
			var value = LoadJson(tableStorage.Lists, id, writeBatch.Value, out version);
			if (value == null)
				return null;

			var etag = Etag.Parse(value.Value<byte[]>("etag"));
			var k = value.Value<string>("key");

			return new ListItem
			{
				Data = value.Value<RavenJObject>("data"),
				Etag = etag,
				Key = k
			};
		}
	}
}