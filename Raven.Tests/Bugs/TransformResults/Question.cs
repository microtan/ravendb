﻿using System;

namespace Raven.Tests.Bugs.TransformResults
{
	public class Question
	{
		public string Id { get; set; }
		public string UserId { get; set; }
		public string Title { get; set; }
		public string Content { get; set; }
	}
	public class Question2
	{
		public Guid Id { get; set; }
		public string UserId { get; set; }
		public string Title { get; set; }
		public string Content { get; set; }
	}
}