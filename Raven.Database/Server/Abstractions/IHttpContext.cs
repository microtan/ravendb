//-----------------------------------------------------------------------
// <copyright file="IHttpContext.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.IO;
using System.Security.Principal;
using Raven.Abstractions.Logging;
using Raven.Database.Config;

namespace Raven.Database.Server.Abstractions
{
	public interface IHttpContext
	{
		bool RequiresAuthentication { get; }
		InMemoryRavenConfiguration Configuration { get; }
		IHttpRequest Request { get; }
		IHttpResponse Response { get; }
		IPrincipal User { get; set; }
		string GetRequestUrlForTenantSelection();
		void FinalizeResponse();
		void SetResponseFilter(Func<Stream, Stream> responseFilter);
		void OutputSavedLogItems(ILog logger);
		void Log(Action<ILog> loggingAction);
		void SetRequestFilter(Func<Stream, Stream> requestFilter);
	}
}
