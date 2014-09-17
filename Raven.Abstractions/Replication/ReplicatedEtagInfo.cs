﻿// -----------------------------------------------------------------------
//  <copyright file="ReplicatedEtagInfo.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;

using Raven.Abstractions.Data;

namespace Raven.Abstractions.Replication
{
	public class ReplicatedEtagInfo
	{
		public string DestinationUrl { get; set; }
		public Etag DocumentEtag { get; set; }

        [Obsolete("Use RavenFS instead.")]
		public Etag AttachmentEtag { get; set; } 
	}
}