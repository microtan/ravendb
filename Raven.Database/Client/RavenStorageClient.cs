﻿// -----------------------------------------------------------------------
//  <copyright file="RavenStorageClient.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

namespace Raven.Database.Client
{
	public abstract class RavenStorageClient : IDisposable
	{
		private readonly List<HttpClient> clients = new List<HttpClient>(); 

		protected HttpClient GetClient(TimeSpan? timeout = null)
		{
			var client = new HttpClient
			             {
				             Timeout = timeout.HasValue ? timeout.Value : TimeSpan.FromSeconds(120)
			             };

			clients.Add(client);

			return client;
		}

		public virtual void Dispose()
		{
			var exceptions = new List<Exception>();

			foreach (var client in clients)
			{
				try
				{
					client.Dispose();
				}
				catch (Exception e)
				{
					exceptions.Add(e);
				}
			}

			if (exceptions.Count > 0)
				throw new AggregateException(exceptions);
		}

		public class Blob
		{
			public Blob(Stream data, Dictionary<string, string> metadata)
			{
				Data = data;
				Metadata = metadata;
			}

			public Stream Data { get; private set; }

			public Dictionary<string, string> Metadata { get; private set; }
		}
	}
}