﻿// -----------------------------------------------------------------------
//  <copyright file="Esent.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;
using Raven.Abstractions.Data;
using Raven.Client.Embedded;
using Raven.Database;
using Raven.Database.Config;
using Raven.Database.Impl;
using Raven.Json.Linq;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Storage
{
	public class Lists : RavenTest
	{
		private readonly EmbeddableDocumentStore store;
		private readonly DocumentDatabase db;

		public Lists()
		{
			store = NewDocumentStore();
			db = store.DocumentDatabase;
		}

		public override void Dispose()
		{
			store.Dispose();
			base.Dispose();
		}


		[Fact]
		public void CanAddAndReadByKey()
		{
			db.TransactionalStorage.Batch(actions => actions.Lists.Set("items", "1", new RavenJObject
			{
				{"test", "data"}
			}, UuidType.Indexing));

			db.TransactionalStorage.Batch(actions =>
			{
				var ravenJObject = actions.Lists.Read("items", "1");
				Assert.Equal("data", ravenJObject.Data.Value<string>("test"));
			});
		}


		[Fact]
		public void CanAddAndRemove()
		{
			db.TransactionalStorage.Batch(actions => actions.Lists.Set("items", "1", new RavenJObject
			{
				{"test", "data"}
			}, UuidType.Indexing));

			db.TransactionalStorage.Batch(actions =>
			{
				var ravenJObject = actions.Lists.Read("items", "1");
				Assert.Equal("data", ravenJObject.Data.Value<string>("test"));
			});

			db.TransactionalStorage.Batch(actions => actions.Lists.Remove("items", "1"));

			db.TransactionalStorage.Batch(actions => Assert.Null(actions.Lists.Read("items", "1")));
		}


		[Fact]
		public void CanReadByName()
		{
			for (int i = 0; i < 10; i++)
			{
				db.TransactionalStorage.Batch(
					actions => actions.Lists.Set("items", i.ToString(CultureInfo.InvariantCulture), new RavenJObject
					{
						{"i", i}
					}, UuidType.Indexing));
			}


			db.TransactionalStorage.Batch(actions =>
			{
				var list = actions.Lists.Read("items", Etag.Empty, null, 100).ToList();
				Assert.Equal(10, list.Count);
				for (int i = 0; i < 10; i++)
				{
					Assert.Equal(i, list[i].Data.Value<int>("i"));
				}
			});
		}

		[Fact]
		public void CanReadFromMiddle()
		{
			for (int i = 0; i < 10; i++)
			{
				db.TransactionalStorage.Batch(
					actions => actions.Lists.Set("items", i.ToString(CultureInfo.InvariantCulture), new RavenJObject
					{
						{"i", i}
					}, UuidType.Indexing));
			}


			db.TransactionalStorage.Batch(actions =>
			{
				var list = actions.Lists.Read("items", Etag.Empty, null, 5).ToList();
				Assert.Equal(5, list.Count);
				for (int i = 0; i < 5; i++)
				{
					Assert.Equal(i, list[i].Data.Value<int>("i"));
				}

				list = actions.Lists.Read("items", list.Last().Etag, null, 5).ToList();
				Assert.Equal(5, list.Count);
				for (int i = 0; i < 5; i++)
				{
					Assert.Equal(i + 5, list[i].Data.Value<int>("i"));
				}
			});
		}

		[Fact]
		public void CanStopReadingAtEnd()
		{
			for (int i = 0; i < 10; i++)
			{
				db.TransactionalStorage.Batch(
					actions => actions.Lists.Set("items", i.ToString(CultureInfo.InvariantCulture), new RavenJObject
					{
						{"i", i}
					}, UuidType.Indexing));
			}


			db.TransactionalStorage.Batch(actions =>
			{
				var list = actions.Lists.Read("items", Etag.Empty, null, 5).ToList();

				list = actions.Lists.Read("items", list.Last().Etag, list.Last().Etag, 5).ToList();
				Assert.Equal(0, list.Count);
			});
		}



		[Fact]
		public void WillOnlyReadItemsFromTheSameList()
		{
			for (int i = 0; i < 10; i++)
			{
				db.TransactionalStorage.Batch(
					actions => actions.Lists.Set("items", i.ToString(CultureInfo.InvariantCulture), new RavenJObject
					{
						{"i", i}
					}, UuidType.Indexing));

				db.TransactionalStorage.Batch(
					actions => actions.Lists.Set("another", i.ToString(CultureInfo.InvariantCulture), new RavenJObject
					{
						{"i", i*2}
					}, UuidType.Indexing));
			}

			db.TransactionalStorage.Batch(actions =>
			{
				var list = actions.Lists.Read("items", Etag.Empty, null, 100).ToList();
				Assert.Equal(10, list.Count);
				for (int i = 0; i < 10; i++)
				{
					Assert.Equal(i, list[i].Data.Value<int>("i"));
				}
			});
		}
	}

}
