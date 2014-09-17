﻿using Raven.Abstractions.FileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Raven.Client.FileSystem
{
    public interface IAsyncFilesOrderedQuery<T> : IAsyncFilesQuery<T>, IAsyncFilesOrderedQueryBase<T, IAsyncFilesQuery<T>>
    {
    }

    public interface IAsyncFilesQuery<T> : IAsyncFilesQueryBase<T, IAsyncFilesQuery<T>>
    {
        bool IsDistinct { get; }

        IAsyncFilesQuery<T> OnDirectory(string path = null, bool recursive = false);

        Task<List<T>> ToListAsync();
    }
}
