﻿using Mono.Unix.Native;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Voron.Debugging;
using Voron.Impl;
using Voron.Platform.Win32;
using Voron.Tests.Backups;
using Voron.Tests.Bugs;
using Voron.Tests.Storage;
using Voron.Trees;
using Snapshots = Voron.Tests.Bugs.Snapshots;

namespace Voron.Tryout
{
	public unsafe class Program
	{
		public static void Main()
		{
			
			var basePath = @"C:\Work\ravendb-3.0\Raven.Voron\Voron.Tryout\bin\Debug\v4";

			var win = new Win32MemoryMapPager(Path.Combine(basePath, "v2", "Raven.voron"));
			var lin = new Win32MemoryMapPager(Path.Combine(basePath, "v2l", "Raven.voron"));

			var winPage = (PageHeader*)win.AcquirePagePointer(0);
			var linPage = (PageHeader*)lin.AcquirePagePointer(0);

			return;
			var path = "v4";
			if (Directory.Exists(path))
				Directory.Delete(path, true);
			Console.WriteLine(Process.GetCurrentProcess().Id);
			using (var env = new StorageEnvironment(StorageEnvironmentOptions.ForPath(path)))
			{
				var batch = new WriteBatch();
				batch.Add("ayende@ayende.com", "Oren Eini", "Names");
				env.Writer.Write(batch);

				using (var snp = env.CreateSnapshot())
				{
					var reader = snp.Read("Names", "ayende@ayende.com");
					if (reader == null)
					{
						Console.WriteLine("Couldn't find it");
					}
					else
					{
						Console.WriteLine(reader.Reader.ToStringValue());
					}
				}
			}

			using (var env = new StorageEnvironment(StorageEnvironmentOptions.ForPath(path)))
			{
				//				using (var snp = env.CreateSnapshot()) 
				//				{
				//					var reader = snp.Read ("Names", "ayende@ayende.com");
				//					if (reader == null) 
				//					{
				//						Console.WriteLine ("Couldn't find it");
				//					} 
				//					else 
				//					{
				//						Console.WriteLine (reader.Reader.ToStringValue());
				//					}
				//				}
			}


			using (var env = new StorageEnvironment(StorageEnvironmentOptions.ForPath(path)))
			{

				using (var snp = env.CreateSnapshot())
				{
					var reader = snp.Read("Names", "ayende@ayende.com");
					if (reader == null)
					{
						Console.WriteLine("Couldn't find it");
					}
					else
					{
						Console.WriteLine(reader.Reader.ToStringValue());
					}
				}
			}
			Console.WriteLine("Done");
		}
	}
}