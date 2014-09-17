﻿using System;

namespace Raven.Abstractions.Data
{
    public enum UuidType : byte
    {
        Documents = 1,

        [Obsolete("Use RavenFS instead.")]
        Attachments = 2,
        DocumentTransactions = 3,
        MappedResults = 4,
        ReduceResults = 5,
        ScheduledReductions = 6,
        Queue = 7,
        Tasks = 8,
        Indexing = 9,
		DocumentReferences = 11
    }
}
