//-----------------------------------------------------------------------
// <copyright file="IndexReplicationDestination.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System.Collections.Generic;

namespace Raven.Bundles.IndexReplication.Data
{
	public class IndexReplicationDestination
	{
        public const string BATCH_DATASET = "BatchDataSet";
        public const string BATCH_COMMAND = "BatchCommand";
        public const string BATCH_ASYNC = "BatchAsync";

		public string Id { get; set; }
		public string ConnectionStringName { get; set; }
		public string TableName { get; set; }
		public string PrimaryKeyColumnName { get; set; }
		public IDictionary<string, string> ColumnsMapping { get; set; }
        public string BatchMode { get; set; }

		public IndexReplicationDestination()
		{
			ColumnsMapping = new Dictionary<string, string>();
		}
	}
}
