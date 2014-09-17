﻿// -----------------------------------------------------------------------
//  <copyright file="DtcAndTouchDocument.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using Raven.Abstractions.Data;
using Raven.Json.Linq;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Bugs
{
	public class DtcAndTouchDocument : RavenTest
	{
		[Fact]
		public void ShouldWork()
		{
			using (var store = NewDocumentStore(requestedStorage:"esent"))
			{
				EnsureDtcIsSupported(store);

				PutResult putResult = store.SystemDatabase.Documents.Put("test", null, new RavenJObject(), new RavenJObject(), null);

				var transactionInformation = new TransactionInformation
				{
					Id = "tx",
					Timeout = TimeSpan.FromDays(1)
				};
				Raven.Abstractions.Data.Etag etag;
				
				store.SystemDatabase.Documents.Put("test", putResult.ETag, new RavenJObject(), new RavenJObject(), transactionInformation);

				store.SystemDatabase.TransactionalStorage.Batch(accessor =>
					accessor.Documents.TouchDocument("test", out etag, out etag));
				
				store.SystemDatabase.PrepareTransaction("tx");
				store.SystemDatabase.Commit("tx");
			}
		}
	}
}