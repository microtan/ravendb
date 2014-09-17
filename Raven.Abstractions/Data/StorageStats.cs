﻿// -----------------------------------------------------------------------
//  <copyright file="StorageStats.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
namespace Raven.Abstractions.Data
{
	public class StorageStats
	{
		public VoronStorageStats VoronStats { get; set; }
		public EsentStorageStats EsentStats { get; set; }
	}
}