﻿//-----------------------------------------------------------------------
// <copyright file="ReplicationDocument.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Raven.Abstractions.Counters
{
	/// <summary>
	/// This class represent the list of replication destinations for the server
	/// </summary>
	public class CounterStorageReplicationDocument
	{
		/// <summary>
		/// Gets or sets the list of replication destinations.
		/// </summary>
		public List<CounterStorageReplicationDestination> Destinations { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="CounterStorageReplicationDocument"/> class.
		/// </summary>
		public CounterStorageReplicationDocument()
		{
            Destinations = new List<CounterStorageReplicationDestination>();
		}
	}
}