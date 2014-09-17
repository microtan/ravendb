﻿// -----------------------------------------------------------------------
//  <copyright file="CounterChanges.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;

namespace Raven.Abstractions.Counters
{
	public class CounterView
	{
		public string Name { get; set; }
		public string Group { get; set; }
		public long OverallTotal { get; set; }
		public List<ServerValue> Servers { get; set; }

		public class ServerValue
		{
			public string Name { get; set; }
			public long Positive { get; set; }
			public long Negative { get; set; }
		}
	}
}