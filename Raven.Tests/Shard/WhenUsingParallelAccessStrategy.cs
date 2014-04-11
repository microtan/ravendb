//-----------------------------------------------------------------------
// <copyright file="WhenUsingParallelAccessStrategy.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using Raven.Client.Document;
using Raven.Client.Shard;
using Raven.Tests.Common;
using Raven.Tests.Document;
using Xunit;

namespace Raven.Tests.Shard
{
	public class WhenUsingParallelAccessStrategy  : RavenTest
	{
		[Fact]
		public void NullResultIsNotAnException()
		{
			using(GetNewServer())
			using (var shard1 = new DocumentStore { Url = "http://localhost:8079" }.Initialize())
			using (var session = shard1.OpenSession())
			{
				var results = new ParallelShardAccessStrategy().Apply(new[] { shard1.DatabaseCommands }, new ShardRequestData(), (x, i) => (IList<Company>)null);

				Assert.Equal(1, results.Length);
				Assert.Null(results[0]);
			}
		}

		[Fact]
		public void ExecutionExceptionsAreRethrown()
		{
			using (GetNewServer())
			using (var shard1 = new DocumentStore { Url = "http://localhost:8079" }.Initialize())
			using (var session = shard1.OpenSession())
			{
				var parallelShardAccessStrategy = new ParallelShardAccessStrategy();
				Assert.Throws<ApplicationException>(() => parallelShardAccessStrategy.Apply<object>(new[] {shard1.DatabaseCommands}, new ShardRequestData(), (x, i) => { throw new ApplicationException(); }));
			}
		}
	}
}