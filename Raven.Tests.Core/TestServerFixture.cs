﻿// -----------------------------------------------------------------------
//  <copyright file="CoreTestServer.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.IO;

using Raven.Database.Config;
using Raven.Database.Extensions;
using Raven.Server;

namespace Raven.Tests.Core
{
	public class TestServerFixture : IDisposable
	{
		public const int Port = 8079;
		public const string ServerName = "Raven.Tests.Core.Server";

		public TestServerFixture()
		{
			var configuration = new RavenConfiguration();
			configuration.Port = Port;
			configuration.ServerName = ServerName;
			configuration.RunInMemory = configuration.DefaultStorageTypeName == InMemoryRavenConfiguration.VoronTypeName;
			configuration.DataDirectory = Path.Combine(configuration.DataDirectory, "Tests");

			IOExtensions.DeleteDirectory(configuration.DataDirectory);

			Server = new RavenDbServer(configuration)
			{
				UseEmbeddedHttpServer = true
			}.Initialize();
		}

		public RavenDbServer Server { get; private set; }

		public void Dispose()
		{
			Server.Dispose();
		}
	}
}