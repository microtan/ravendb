﻿// -----------------------------------------------------------------------
//  <copyright file="MultiTenant.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System.Collections.Concurrent;
using System.Reactive.Linq;
using Raven.Abstractions.Data;
using Raven.Client.Document;
using Raven.Tests.Common;

using Xunit;
using System;
using Raven.Client.Extensions;

namespace Raven.Tests.Notifications
{
	public class MultiTenant : RavenTest
	{
		private string url = "http://localhost:8079";

		[Fact]
		public void CanGetNotificationsFromTenant_DefaultDatabase()
		{
			using(GetNewServer())
			using (var store = new DocumentStore
			{
				Url = url,
				DefaultDatabase = "test"
			}.Initialize())
			{
				store.DatabaseCommands.GlobalAdmin.EnsureDatabaseExists("test");
				var list = new BlockingCollection<DocumentChangeNotification>();
				var taskObservable = store.Changes();
				taskObservable.Task.Wait();
				taskObservable
					.ForDocument("items/1").Task.Result
					.Subscribe(list.Add);

				using (var session = store.OpenSession())
				{
					session.Store(new ClientServer.Item(), "items/1");
					session.SaveChanges();
				}

				DocumentChangeNotification documentChangeNotification;
				Assert.True(list.TryTake(out documentChangeNotification, TimeSpan.FromSeconds(15)));

				Assert.Equal("items/1", documentChangeNotification.Id);
				Assert.Equal(documentChangeNotification.Type, DocumentChangeTypes.Put);
			}

		}

		[Fact]
		public void CanGetNotificationsFromTenant_ExplicitDatabase()
		{
			using (GetNewServer())
			using (var store = new DocumentStore
			{
				Url = url,
			}.Initialize())
			{
				store.DatabaseCommands.GlobalAdmin.EnsureDatabaseExists("test");
				var list = new BlockingCollection<DocumentChangeNotification>();
				var taskObservable = store.Changes("test").Task.Result;
				taskObservable.Task.Result
					.ForDocument("items/1").Task.Result
					.Subscribe(list.Add);

				using (var session = store.OpenSession("test"))
				{
					session.Store(new ClientServer.Item(), "items/1");
					session.SaveChanges();
				}

				DocumentChangeNotification DocumentChangeNotification;
				Assert.True(list.TryTake(out DocumentChangeNotification, TimeSpan.FromSeconds(15)));

				Assert.Equal("items/1", DocumentChangeNotification.Id);
				Assert.Equal(DocumentChangeNotification.Type, DocumentChangeTypes.Put);
			}

		}

		[Fact]
		public void CanGetNotificationsFromTenant_AndNotFromAnother()
		{
			using (GetNewServer())
			using (var store = new DocumentStore
			{
				Url = url,
			}.Initialize())
			{
				store.DatabaseCommands.GlobalAdmin.EnsureDatabaseExists("test");
				store.DatabaseCommands.GlobalAdmin.EnsureDatabaseExists("another");
				var list = new BlockingCollection<DocumentChangeNotification>();
				store.Changes("test").Task.Result
					.ForDocument("items/1").Task.Result
					.Subscribe(list.Add);

				using (var session = store.OpenSession("another"))
				{
					session.Store(new ClientServer.Item(), "items/2");
					session.SaveChanges();
				}

				using (var session = store.OpenSession("test"))
				{
					session.Store(new ClientServer.Item(), "items/1");
					session.SaveChanges();
				}

				DocumentChangeNotification DocumentChangeNotification;
				Assert.True(list.TryTake(out DocumentChangeNotification, TimeSpan.FromSeconds(15)));

				Assert.Equal("items/1", DocumentChangeNotification.Id);
				Assert.Equal(DocumentChangeNotification.Type, DocumentChangeTypes.Put);

				Assert.False(list.TryTake(out DocumentChangeNotification, TimeSpan.FromMilliseconds(250)));
			}
		}
	}
}