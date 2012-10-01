//-----------------------------------------------------------------------
// <copyright file="IndexReplicationIndexUpdateTrigger.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Text;
using System.Text.RegularExpressions;
using Lucene.Net.Documents;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Bundles.IndexReplication.Data;
using Raven.Database.Json;
using Raven.Database.Plugins;
using Document = Lucene.Net.Documents.Document;
using NLog;

namespace Raven.Bundles.IndexReplication
{
	public class IndexReplicationIndexUpdateTrigger : AbstractIndexUpdateTrigger
	{
		private readonly Logger logger = LogManager.GetCurrentClassLogger();

		public override AbstractIndexUpdateTriggerBatcher CreateBatcher(string indexName)
		{
			var document = Database.Get("Raven/IndexReplication/" + indexName, null);
			if (document == null)
				return null; // we don't have any reason to replicate anything 

			var destination = document.DataAsJson.JsonDeserialization<IndexReplicationDestination>();

			var connectionString = ConfigurationManager.ConnectionStrings[destination.ConnectionStringName];
			if(connectionString == null)
				throw new InvalidOperationException("Could not find a connection string name: " + destination.ConnectionStringName);
			if(connectionString.ProviderName == null)
				throw new InvalidOperationException("Connection string name '"+destination.ConnectionStringName+"' must specify the provider name");

			var providerFactory = DbProviderFactories.GetFactory(connectionString.ProviderName);

			var connection = providerFactory.CreateConnection() as SqlConnection;
			connection.ConnectionString = connectionString.ConnectionString;

			logger.Debug("created connection and adapter for index batcher:" + indexName);

            if (String.IsNullOrEmpty(destination.BatchMode))
            {
                #region Old mode
                return new ReplicateToSqlIndexUpdateBatcher(
                    connection,
                    destination);
                #endregion
            }
            else if (destination.BatchMode.ToLower() == IndexReplicationDestination.BATCH_DATASET.ToLower())
            {
                #region batch mode
                string selectCommandText = "select * from " + destination.TableName;
                SqlDataAdapter adapter = new SqlDataAdapter(selectCommandText, connection);
                // Set the INSERT command and parameter.
                adapter.InsertCommand = CreateInsertCommand(destination);
                adapter.InsertCommand.Connection = connection;
                adapter.UpdateBatchSize = 16;

                //Create dateset
                DataSet dataSet = new DataSet();
                adapter.FillSchema(dataSet, SchemaType.Source, destination.TableName);
                logger.Debug("created connection and adapter for index batcher:" + indexName);

                return new ReplicateToSqlIndexUpdateBatchModeBatcher(
                    connection,
                    adapter,
                    dataSet,
                    destination);
                #endregion
            }
            else if (destination.BatchMode.ToLower() == IndexReplicationDestination.BATCH_ASYNC.ToLower())
            {
                #region Async
                return new ReplicateToSqlAsyncIndexUpdateBatcher(
                    connection,
                    destination);
                #endregion
            }
            else 
            {
                #region Batch command
                SqlCommand cmd = new SqlCommand();
                return new ReplicateToSqlIndexUpdateBatchCommandBatcher(
                    connection,
                    cmd,
                    destination);
                #endregion
            }
        }

        #region util function for batch
        protected SqlCommand CreateInsertCommand(IndexReplicationDestination destination)
		{
			var cmd = new SqlCommand();
			cmd.UpdatedRowSource = UpdateRowSource.None;

			var sbF = new StringBuilder("INSERT INTO ")
				.Append(destination.TableName)
				.Append(" (")
				.Append(destination.PrimaryKeyColumnName)
				.Append(", ");

            var sbS = new StringBuilder()
                .Append(") \r\nVALUES (")
				.Append("@entryKey")
				.Append(", ");

            cmd.Parameters.Add("@entryKey", SqlDbType.VarChar, 255, destination.PrimaryKeyColumnName);

			foreach (var mapping in destination.ColumnsMapping)
			{
                sbF.Append(mapping.Value).Append(", ");

                var parameter = new SqlParameter();
                parameter.ParameterName = "@" + mapping.Value;
                parameter.SourceColumn = mapping.Value;
                sbS.Append(parameter.ParameterName).Append(", ");
                cmd.Parameters.Add(parameter);
			}
			sbF.Length = sbF.Length - 2;
            sbS.Length = sbS.Length - 2;
            sbS.Append(")");

            sbF.Append(sbS.ToString());

			cmd.CommandText = sbF.ToString();
			return cmd;
		}


        protected SqlCommand CreateDeleteCommand(IndexReplicationDestination destination)
		{
			var cmd = new SqlCommand();
			cmd.UpdatedRowSource = UpdateRowSource.None;

			var parameter = cmd.CreateParameter();
			parameter.ParameterName = "@entryKey";
			cmd.Parameters.Add("@entryKey", SqlDbType.VarChar, 255, destination.PrimaryKeyColumnName);
			cmd.CommandText = string.Format("DELETE FROM {0} WHERE {1} = {2}", destination.TableName, destination.PrimaryKeyColumnName, parameter.ParameterName);

			return cmd;
		}
        #endregion

        #region Batch command update
        public class ReplicateToSqlIndexUpdateBatchCommandBatcher : AbstractIndexUpdateTriggerBatcher
        {
			private readonly DbConnection connection;
            private readonly SqlCommand cmd;
			private readonly IndexReplicationDestination destination;
			private DbTransaction tx;
			private static Regex datePattern = new Regex(@"\d{17}", RegexOptions.Compiled);

            public ReplicateToSqlIndexUpdateBatchCommandBatcher(
                DbConnection connection, 
                SqlCommand cmd,
                IndexReplicationDestination destination)
			{
				this.connection = connection;
				this.destination = destination;
                this.cmd = cmd;
			}

			private DbConnection Connection
			{
				get
				{
					if (connection.State != ConnectionState.Open)
					{
						connection.Open();
						//tx = connection.BeginTransaction(IsolationLevel.ReadCommitted);
					}
					return connection;
				}
			}

			public override void OnIndexEntryCreated(string entryKey, Document document)
			{
                //using (var cmd = Connection.CreateCommand())
                //{
					//cmd.Transaction = tx;
					var pkParam = cmd.CreateParameter();
					pkParam.ParameterName = GetParameterName("entryKey");
					pkParam.Value = entryKey;
					cmd.Parameters.Add(pkParam);

					var sb = new StringBuilder("INSERT INTO ")
						.Append(destination.TableName)
						.Append(" (")
						.Append(destination.PrimaryKeyColumnName)
						.Append(", ");

					foreach (var mapping in destination.ColumnsMapping)
					{
						sb.Append(mapping.Value).Append(", ");
					}
					sb.Length = sb.Length - 2;

					sb.Append(") \r\nVALUES (")
						.Append(pkParam.ParameterName)
						.Append(", ");

					foreach (var mapping in destination.ColumnsMapping)
					{
						var parameter = cmd.CreateParameter();
						parameter.ParameterName = GetParameterName(mapping.Key);
						var field = document.GetFieldable(mapping.Key);

						var numericfield = document.GetFieldable(String.Concat(mapping.Key, "_Range"));
						if (numericfield != null)
							field = numericfield;

						if (field == null || field.StringValue() == Constants.NullValue || field.StringValue() == Constants.EmptyString)
							parameter.Value = DBNull.Value;
						else if (field is NumericField)
						{
							var numField = (NumericField)field;
							parameter.Value = numField.GetNumericValue();
						}
						else
						{
							var stringValue = field.StringValue();
							if (datePattern.IsMatch(stringValue))
							{
								try
								{
									parameter.Value = DateTools.StringToDate(stringValue);
								}
								catch
								{
									parameter.Value = stringValue;
								}
							}
							else
							{
								parameter.Value = stringValue;
							}
						}
						cmd.Parameters.Add(parameter);
						sb.Append(parameter.ParameterName).Append(", ");
					}
					sb.Length = sb.Length - 2;
					sb.Append(");");
					cmd.CommandText = sb.ToString();
					cmd.ExecuteNonQuery();
				//}
			}

			public override void OnIndexEntryDeleted(string entryKey)
			{
				using (var cmd = Connection.CreateCommand())
				{
					cmd.Transaction = tx;
					var parameter = cmd.CreateParameter();
					parameter.ParameterName = GetParameterName("entryKey");
					parameter.Value = entryKey;
					cmd.Parameters.Add(parameter);
					cmd.CommandText = string.Format("DELETE FROM {0} WHERE {1} = {2}", destination.TableName, destination.PrimaryKeyColumnName, parameter.ParameterName);

					cmd.ExecuteNonQuery();
				}
			}

			private string GetParameterName(string paramName)
			{
				if (connection is SqlConnection)
					return "@" + paramName;
				return ":" + paramName;
			}

			public override void Dispose()
			{
				tx.Commit();
				connection.Dispose();
			}
        }
        #endregion

        #region async code
        public class ReplicateToSqlAsyncIndexUpdateBatcher : AbstractIndexUpdateTriggerBatcher
        {
            private readonly Logger logger = LogManager.GetCurrentClassLogger();
            private readonly DbConnection connection;
            private readonly IndexReplicationDestination destination;
            private DbTransaction tx;
            private static Regex datePattern = new Regex(@"\d{17}", RegexOptions.Compiled);

            public ReplicateToSqlAsyncIndexUpdateBatcher(DbConnection connection, IndexReplicationDestination destination)
            {
                this.connection = connection;
                this.destination = destination;
            }

            private DbConnection Connection
            {
                get
                {
                    if (connection.State != ConnectionState.Open)
                    {
                        connection.Open();
                        //tx = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                    }
                    return connection;
                }
            }

            public override void OnIndexEntryCreated(string entryKey, Document document)
            {
                var cmd = (Connection as SqlConnection).CreateCommand();
                
                //cmd.Transaction = tx;
                var pkParam = cmd.CreateParameter();
                pkParam.ParameterName = GetParameterName("entryKey");
                pkParam.Value = entryKey;
                cmd.Parameters.Add(pkParam);

                var sb = new StringBuilder("INSERT INTO ")
                    .Append(destination.TableName)
                    .Append(" (")
                    .Append(destination.PrimaryKeyColumnName)
                    .Append(", ");

                foreach (var mapping in destination.ColumnsMapping)
                {
                    sb.Append(mapping.Value).Append(", ");
                }
                sb.Length = sb.Length - 2;

                sb.Append(") \r\nVALUES (")
                    .Append(pkParam.ParameterName)
                    .Append(", ");

                foreach (var mapping in destination.ColumnsMapping)
                {
                    var parameter = cmd.CreateParameter();
                    parameter.ParameterName = GetParameterName(mapping.Key);
                    var field = document.GetFieldable(mapping.Key);

                    var numericfield = document.GetFieldable(String.Concat(mapping.Key, "_Range"));
                    if (numericfield != null)
                        field = numericfield;

                    if (field == null || field.StringValue() == Constants.NullValue || field.StringValue() == Constants.EmptyString)
                        parameter.Value = DBNull.Value;
                    else if (field is NumericField)
                    {
                        var numField = (NumericField)field;
                        parameter.Value = numField.GetNumericValue();
                    }
                    else
                    {
                        var stringValue = field.StringValue();
                        if (datePattern.IsMatch(stringValue))
                        {
                            try
                            {
                                parameter.Value = DateTools.StringToDate(stringValue);
                            }
                            catch
                            {
                                parameter.Value = stringValue;
                            }
                        }
                        else
                        {
                            parameter.Value = stringValue;
                        }
                    }
                    cmd.Parameters.Add(parameter);
                    sb.Append(parameter.ParameterName).Append(", ");
                }
                sb.Length = sb.Length - 2;
                sb.Append(")");
                cmd.CommandText = sb.ToString();
                //cmd.ExecuteNonQuery();

                AsyncCallback callback = new AsyncCallback(HandleCallback);
                cmd.BeginExecuteNonQuery(callback, cmd);   
            }

            public override void OnIndexEntryDeleted(string entryKey)
            {
                using (var cmd = Connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    var parameter = cmd.CreateParameter();
                    parameter.ParameterName = GetParameterName("entryKey");
                    parameter.Value = entryKey;
                    cmd.Parameters.Add(parameter);
                    cmd.CommandText = string.Format("DELETE FROM {0} WHERE {1} = {2}", destination.TableName, destination.PrimaryKeyColumnName, parameter.ParameterName);

                    cmd.ExecuteNonQuery();
                }
            }

            private string GetParameterName(string paramName)
            {
                if (connection is SqlConnection)
                    return "@" + paramName;
                return ":" + paramName;
            }

            private void HandleCallback(IAsyncResult result)
            {
                try
                {
                    // Retrieve the original command object, passed
                    // to this procedure in the AsyncState property
                    // of the IAsyncResult parameter.
                    SqlCommand command = (SqlCommand)result.AsyncState;
                    command.EndExecuteNonQuery(result);
                }
                catch (Exception ex)
                {
                    // Because you are now running code in a separate thread, 
                    // if you do not handle the exception here, none of your other
                    // code catches the exception. Because none of 
                    // your code is on the call stack in this thread, there is nothing
                    // higher up the stack to catch the exception if you do not 
                    // handle it here. You can either log the exception or 
                    // invoke a delegate (as in the non-error case in this 
                    // example) to display the error on the form. In no case
                    // can you simply display the error without executing a delegate
                    // as in the try block here. 

                    logger.WarnException(
                        string.Format("Failed to async index documents for command: {0}", result.ToString()), ex);
                }
                finally
                {
                }
            }

            public override void Dispose()
            {
                //tx.Commit();
                connection.Dispose();
            }
        }
        #endregion

        #region Batch update
        public class ReplicateToSqlIndexUpdateBatchModeBatcher : AbstractIndexUpdateTriggerBatcher
		{
            private readonly Logger logger = LogManager.GetCurrentClassLogger();
			private readonly SqlConnection connection;
			private readonly IndexReplicationDestination destination;
			private SqlDataAdapter adapter;
			private DataSet dataSet;
			private SqlTransaction tx;
			private static Regex datePattern = new Regex(@"\d{17}", RegexOptions.Compiled);

			public ReplicateToSqlIndexUpdateBatchModeBatcher(
				SqlConnection connection, SqlDataAdapter adapter, DataSet dataSet, IndexReplicationDestination destination)
			{
				this.connection = connection;
				this.dataSet = dataSet;
				this.adapter = adapter;
				this.destination = destination;
			}

			private DbConnection Connection
			{
				get
				{
					if (connection.State != ConnectionState.Open)
					{
						connection.Open();
						//tx = connection.BeginTransaction(IsolationLevel.ReadCommitted);
						//adapter.InsertCommand.Transaction = tx;
					}
					return connection;
				}
			}

			public override void OnIndexEntryCreated(string entryKey, Document document)
			{
				var dt = dataSet.Tables[destination.TableName];
				DataRow row = dt.NewRow();
				row[destination.PrimaryKeyColumnName] = entryKey;

				//build row
				foreach (var mapping in destination.ColumnsMapping)
				{
					var parameter = new SqlParameter();
					parameter.ParameterName = mapping.Key;
					var field = document.GetFieldable(mapping.Key);

					var numericfield = document.GetFieldable(String.Concat(mapping.Key, "_Range"));
					if (numericfield != null)
						field = numericfield;

					if (field == null || field.StringValue() == Constants.NullValue || field.StringValue() == Constants.EmptyString)
						parameter.Value = DBNull.Value;
					else if (field is NumericField)
					{
						var numField = (NumericField)field;
						parameter.Value = numField.GetNumericValue();
					}
					else
					{
						var stringValue = field.StringValue();
						if (datePattern.IsMatch(stringValue))
						{
							try
							{
								parameter.Value = DateTools.StringToDate(stringValue);
							}
							catch
							{
								parameter.Value = stringValue;
							}
						}
						else
						{
							parameter.Value = stringValue;
						}
					}

					row[mapping.Value] = parameter.Value;
				}
				dt.Rows.Add(row);
			}

			public override void OnIndexEntryDeleted(string entryKey)
			{
				using (var cmd = Connection.CreateCommand())
				{
					cmd.Transaction = tx;
					var parameter = cmd.CreateParameter();
					parameter.ParameterName = GetParameterName("entryKey");
					parameter.Value = entryKey;
					cmd.Parameters.Add(parameter);
					cmd.CommandText = string.Format("DELETE FROM {0} WHERE {1} = {2}", destination.TableName, destination.PrimaryKeyColumnName, parameter.ParameterName);

					cmd.ExecuteNonQuery();
				}
			}

			private string GetParameterName(string paramName)
			{
				if (connection is SqlConnection)
					return "@" + paramName;
				return ":" + paramName;
			}

			public override void Dispose()
			{
				adapter.Update(dataSet, destination.TableName);
				//tx.Commit();
				connection.Dispose();
                logger.Debug("Commit changes : ");
			}
		}
        #endregion

        #region Existing code
        public class ReplicateToSqlIndexUpdateBatcher : AbstractIndexUpdateTriggerBatcher
		{
			private readonly DbConnection connection;
			private readonly IndexReplicationDestination destination;
			private DbTransaction tx;
			private static Regex datePattern = new Regex(@"\d{17}", RegexOptions.Compiled);

			public ReplicateToSqlIndexUpdateBatcher(DbConnection connection, IndexReplicationDestination destination)
			{
				this.connection = connection;
				this.destination = destination;
			}

			private DbConnection Connection
			{
				get
				{
					if(connection.State != ConnectionState.Open)
					{
						connection.Open();
						tx = connection.BeginTransaction(IsolationLevel.ReadCommitted);
					}
					return connection;
				}
			}

			public override void OnIndexEntryCreated(string entryKey, Document document)
			{
				using (var cmd = Connection.CreateCommand())
				{
					cmd.Transaction = tx;
					var pkParam = cmd.CreateParameter();
					pkParam.ParameterName = GetParameterName("entryKey");
					pkParam.Value = entryKey;
					cmd.Parameters.Add(pkParam);

					var sb = new StringBuilder("INSERT INTO ")
						.Append(destination.TableName)
						.Append(" (")
						.Append(destination.PrimaryKeyColumnName)
						.Append(", ");

					foreach (var mapping in destination.ColumnsMapping)
					{
						sb.Append(mapping.Value).Append(", ");
					}
					sb.Length = sb.Length - 2;

					sb.Append(") \r\nVALUES (")
						.Append(pkParam.ParameterName)
						.Append(", ");

					foreach (var mapping in destination.ColumnsMapping)
					{
						var parameter = cmd.CreateParameter();
						parameter.ParameterName = GetParameterName(mapping.Key);
						var field = document.GetFieldable(mapping.Key);

						var numericfield = document.GetFieldable(String.Concat(mapping.Key, "_Range"));
						if (numericfield != null)
							field = numericfield;

						if (field == null || field.StringValue() == Constants.NullValue || field.StringValue() == Constants.EmptyString)
							parameter.Value = DBNull.Value;
						else if (field is NumericField)
						{
							var numField = (NumericField)field;
							parameter.Value = numField.GetNumericValue();
						}
						else
						{
							var stringValue = field.StringValue();
							if (datePattern.IsMatch(stringValue))
							{
								try
								{
									parameter.Value = DateTools.StringToDate(stringValue);
								}
								catch
								{
									parameter.Value = stringValue;
								}
							}
							else
							{
								parameter.Value = stringValue;
							}
						}
						cmd.Parameters.Add(parameter);
						sb.Append(parameter.ParameterName).Append(", ");
					}
					sb.Length = sb.Length - 2;
					sb.Append(")");
					cmd.CommandText = sb.ToString();
					cmd.ExecuteNonQuery();
				}
			}

			public override void OnIndexEntryDeleted(string entryKey)
			{
				using (var cmd = Connection.CreateCommand())
				{
					cmd.Transaction = tx;
					var parameter = cmd.CreateParameter();
					parameter.ParameterName = GetParameterName("entryKey");
					parameter.Value = entryKey;
					cmd.Parameters.Add(parameter);
					cmd.CommandText = string.Format("DELETE FROM {0} WHERE {1} = {2}", destination.TableName, destination.PrimaryKeyColumnName, parameter.ParameterName);

					cmd.ExecuteNonQuery();
				}
			}

			private string GetParameterName(string paramName)
			{
				if (connection is SqlConnection)
					return "@" + paramName;
				return ":" + paramName;
			}

			public override void Dispose()
			{
				tx.Commit();
				connection.Dispose();
			}
        }
        #endregion
    }
}
