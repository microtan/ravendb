﻿// -----------------------------------------------------------------------
//  <copyright file="ReplicationConfig.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;

namespace Raven.Abstractions.Replication
{
	/// <summary>
	/// Data class for replication config document, available on a destination server
	/// </summary>
	public class ReplicationConfig
	{
		public StraightforwardConflictResolution DocumentConflictResolution { get; set; }

        [Obsolete("Use RavenFS instead.")]
		public StraightforwardConflictResolution AttachmentConflictResolution { get; set; }
	}

	public enum StraightforwardConflictResolution
	{
		None,
		/// <summary>
		/// Always resolve in favor of a local version
		/// </summary>
		ResolveToLocal,
		/// <summary>
		/// Always resolve in favor of a remote version
		/// </summary>
		ResolveToRemote,
		/// <summary>
		/// Always resolve in favor of the latest version based on the last modified time
		/// </summary>
		ResolveToLatest
	}
}