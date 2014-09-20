﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raven.Abstractions.FileSystem
{
    public class FileSystemDocument
    {
        /// <summary>
        /// The ID can be either the file system name ("FilesystemName") or the full document name ("Raven/Filesystems/FilesystemName").
        /// </summary>
        public string Id { get; set; }
        public Dictionary<string, string> Settings { get; set; }
        public bool Disabled { get; set; }

        public FileSystemDocument()
        {
            Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);           
        }
    }
}
