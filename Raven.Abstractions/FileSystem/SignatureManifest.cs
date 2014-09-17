﻿using System;
using System.Collections.Generic;

namespace Raven.Abstractions.FileSystem
{
	public class SignatureManifest
	{
		public string FileName { get; set; }
		public IList<Signature> Signatures { get; set; }
		public long FileLength { get; set; }
	}
}