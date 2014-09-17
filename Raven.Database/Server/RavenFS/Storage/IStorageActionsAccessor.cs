﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;

using Raven.Database.Server.RavenFS.Synchronization.Rdc;
using Raven.Json.Linq;
using Raven.Abstractions.FileSystem;

namespace Raven.Database.Server.RavenFS.Storage
{
    public interface IStorageActionsAccessor : IDisposable
    {
        [DebuggerHidden]
        [DebuggerNonUserCode]
        void Commit();

        void PulseTransaction();

        int InsertPage(byte[] buffer, int size);

        void PutFile(string filename, long? totalSize, RavenJObject metadata, bool tombstone = false);

        void AssociatePage(string filename, int pageId, int pagePositionInFile, int pageSize);

        int ReadPage(int pageId, byte[] buffer);

        FileHeader ReadFile(string filename);

        FileAndPagesInformation GetFile(string filename, int start, int pagesToLoad);

        IEnumerable<FileHeader> ReadFiles(int start, int size);

        IEnumerable<FileHeader> GetFilesAfter(Guid etag, int take);

        void Delete(string filename);
       
        void UpdateFileMetadata(string filename, RavenJObject metadata);

        void CompleteFileUpload(string filename);

        int GetFileCount();

        void DecrementFileCount(string nameOfFileThatShouldNotBeCounted);

        void RenameFile(string filename, string rename, bool commitPeriodically = false);

        RavenJObject GetConfig(string name);

        void SetConfig(string name, RavenJObject metadata);

        void DeleteConfig(string name);

        IEnumerable<SignatureLevels> GetSignatures(string name);

        void ClearSignatures(string name);

        long GetSignatureSize(int id, int level);

        void GetSignatureStream(int id, int level, Action<Stream> action);

        void AddSignature(string name, int level, Action<Stream> action);

        IEnumerable<string> GetConfigNames(int start, int pageSize);

        bool ConfigExists(string name);

        IList<RavenJObject> GetConfigsStartWithPrefix(string prefix, int start, int take);

        IList<string> GetConfigNamesStartingWithPrefix(string prefix, int start, int take, out int total);
    }
}