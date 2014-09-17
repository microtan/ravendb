﻿//-----------------------------------------------------------------------
// <copyright file="RavenTest.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Raven.Abstractions;
using Raven.Abstractions.Logging;
using Raven.Client.Embedded;
using Raven.Database;
using Raven.Database.Util;
using Raven.Server;
using Raven.Tests.Helpers;

using Xunit;

namespace Raven.Tests.Common
{
    public class RavenTest : RavenTestBase
    {
		protected bool ShowLogs { get; set; }

        static RavenTest()
        {
            LogManager.RegisterTarget<DatabaseMemoryTarget>();
        }

        public RavenTest()
        {
            SystemTime.UtcDateTime = () => DateTime.UtcNow;
        }

	    public override void Dispose()
	    {
		    ShowLogsIfNecessary();

		    base.Dispose();
	    }

	    private void ShowLogsIfNecessary()
	    {
		    if (!ShowLogs)
			    return;

		    foreach (var databaseName in DatabaseNames)
		    {
			    var target = LogManager.GetTarget<DatabaseMemoryTarget>()[databaseName];
			    if (target == null)
				    continue;

			    using (var file = File.Open("debug_output.txt", FileMode.Append))
			    using (var writer = new StreamWriter(file))
			    {
					WriteLine(writer);
				    WriteLine(writer, "Logs for: " + databaseName);

				    foreach (var info in target.GeneralLog)
				    {
						WriteLine(writer, "========================================");
						WriteLine(writer, "Time: " + info.TimeStamp);
						WriteLine(writer, "Level: " + info.Level);
						WriteLine(writer, "Logger: " + info.LoggerName);
						WriteLine(writer, "Message: " + info.FormattedMessage);
						WriteLine(writer, "Exception: " + info.Exception);
						WriteLine(writer, "========================================");
				    }

				    WriteLine(writer);
			    }
		    }
	    }

	    private static void WriteLine(TextWriter writer, string message = "")
	    {
		    Console.WriteLine(message);
			writer.WriteLine(message);
	    }

	    protected void Consume(object o)
        {

        }

        protected static void EnsureDtcIsSupported(EmbeddableDocumentStore documentStore)
        {
            EnsureDtcIsSupported(documentStore.SystemDatabase);
        }

        protected static void EnsureDtcIsSupported(DocumentDatabase documentDatabase)
        {
            if (documentDatabase.TransactionalStorage.SupportsDtc == false)
                throw new SkipException("This test requires DTC but the storage engine " + documentDatabase.TransactionalStorage.FriendlyName + " does not support it");
        }

        protected static void EnsureDtcIsSupported(RavenDbServer server)
        {
            EnsureDtcIsSupported(server.SystemDatabase);
        }

        public double Timer(Action action)
        {
            var timer = Stopwatch.StartNew();
            action.Invoke();
            timer.Stop();
            Console.WriteLine("Time take (ms)- " + timer.Elapsed.TotalMilliseconds);
            return timer.Elapsed.TotalMilliseconds;
        }

        public static IEnumerable<object[]> Storages
        {
            get
            {
                return new[]
				{
					new object[] {"voron"},
					new object[] {"esent"}
				};
            }
        }
    }
}