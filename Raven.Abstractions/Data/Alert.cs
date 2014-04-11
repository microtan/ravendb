﻿using System;

namespace Raven.Abstractions.Data
{
	public class Alert
	{
		public string Title { get; set; }
		public DateTime CreatedAt { get; set; }
		public bool Observed { get; set; }
		public string Message { get; set; }
		public AlertLevel AlertLevel { get; set; }
		public string Exception { get; set; }

		public string UniqueKey { get; set; }
	}

	public enum AlertLevel
	{
		Warning,
		Error
	}
}
