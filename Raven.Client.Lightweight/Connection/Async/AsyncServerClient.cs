//-----------------------------------------------------------------------
// <copyright file="AsyncServerClient.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Indexes;
using Raven.Database.Data;
using Raven.Imports.Newtonsoft.Json.Linq;
using Raven.Abstractions;
using Raven.Abstractions.Commands;
using Raven.Abstractions.Connection;
using Raven.Abstractions.Data;
using Raven.Abstractions.Exceptions;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Indexing;
using Raven.Abstractions.Json;
using Raven.Abstractions.Replication;
using Raven.Abstractions.Util;
using Raven.Client.Changes;
using Raven.Client.Connection.Profiling;
using Raven.Client.Document;
using Raven.Client.Exceptions;
using Raven.Client.Extensions;
using Raven.Client.Listeners;
using Raven.Imports.Newtonsoft.Json;
using Raven.Imports.Newtonsoft.Json.Bson;
using Raven.Json.Linq;
using System.Collections.Specialized;

namespace Raven.Client.Connection.Async
{
	/// <summary>
	/// Access the database commands in async fashion
	/// </summary>
	public class AsyncServerClient : IAsyncDatabaseCommands, IAsyncInfoDatabaseCommands
	{
	    private const int MaxQuerySizeForGetRequest = 8 * 1024;
	    private readonly ProfilingInformation profilingInformation;
		private readonly IDocumentConflictListener[] conflictListeners;
		private readonly string url;
		private readonly string rootUrl;
		private readonly OperationCredentials credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication;
		internal readonly DocumentConvention convention;
		private NameValueCollection operationsHeaders = new NameValueCollection();
		internal readonly HttpJsonRequestFactory jsonRequestFactory;
		private readonly Guid? sessionId;
		private readonly Func<string, IDocumentStoreReplicationInformer> replicationInformerGetter;
		private readonly string databaseName;
		private readonly IDocumentStoreReplicationInformer replicationInformer;
		private int requestCount;
		private int readStripingBase;

		public string Url
		{
			get { return url; }
		}

		public IDocumentStoreReplicationInformer ReplicationInformer
		{
			get { return replicationInformer; }
		}

		public OperationCredentials PrimaryCredentials
		{
			get
			{
				return credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication;
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="AsyncServerClient"/> class.
		/// </summary>
		public AsyncServerClient(string url, DocumentConvention convention, OperationCredentials credentials,
								 HttpJsonRequestFactory jsonRequestFactory, Guid? sessionId,
								 Func<string, IDocumentStoreReplicationInformer> replicationInformerGetter, string databaseName,
								 IDocumentConflictListener[] conflictListeners)
		{
			profilingInformation = ProfilingInformation.CreateProfilingInformation(sessionId);
			this.url = url;
			if (this.url.EndsWith("/"))
				this.url = this.url.Substring(0, this.url.Length - 1);
			rootUrl = this.url;
			var databasesIndex = rootUrl.IndexOf("/databases/", StringComparison.OrdinalIgnoreCase);
			if (databasesIndex > 0)
			{
				rootUrl = rootUrl.Substring(0, databasesIndex);
			}
			this.jsonRequestFactory = jsonRequestFactory;
			this.sessionId = sessionId;
			this.convention = convention;
            this.credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication = credentials;
			this.databaseName = databaseName;
			this.conflictListeners = conflictListeners;
			this.replicationInformerGetter = replicationInformerGetter;
			this.replicationInformer = replicationInformerGetter(databaseName);
			this.readStripingBase = replicationInformer.GetReadStripingBase();

			this.replicationInformer.UpdateReplicationInformationIfNeeded(this);
		}

		/// <summary>
		/// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
		/// </summary>
		public void Dispose()
		{
		}

		/// <summary>
		/// Gets the index names from the server asynchronously
		/// </summary>
		/// <param name="start">Paging start</param>
		/// <param name="pageSize">Size of the page.</param>
		public Task<string[]> GetIndexNamesAsync(int start, int pageSize)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				var json = (RavenJArray)await operationMetadata.Url.IndexNames(start, pageSize)
					.ToJsonRequest(this, operationMetadata.Credentials, convention)
														   .AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer,
																						convention.FailoverBehavior,
																						HandleReplicationStatusChanges)
														   .ReadResponseJsonAsync().ConfigureAwait(false);

				return json.Select(x => x.Value<string>()).ToArray();
			});
		}

		/// <summary>
		/// Gets the indexes from the server asynchronously
		/// </summary>
		/// <param name="start">Paging start</param>
		/// <param name="pageSize">Size of the page.</param>
		public Task<IndexDefinition[]> GetIndexesAsync(int start, int pageSize)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				var url2 = operationMetadata.Url + "/indexes/?start=" + start + "&pageSize=" + pageSize;
				var request =
					jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, url2, "GET", operationMetadata.Credentials, convention));
				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
													HandleReplicationStatusChanges);

				var json = (RavenJArray)await request.ReadResponseJsonAsync().ConfigureAwait(false);
				//NOTE: To review, I'm not confidence this is the correct way to deserialize the index definition
				return json.Select(x =>
				{
					var value = ((RavenJObject)x)["definition"].ToString();
					return JsonConvert.DeserializeObject<IndexDefinition>(value, new JsonToJsonConverter());
				})
							.ToArray();
			});
		}

		/// <summary>
		/// Gets the transformers from the server asynchronously
		/// </summary>
		public Task<TransformerDefinition[]> GetTransformersAsync(int start, int pageSize)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				var url2 = operationMetadata.Url + "/transformers?start=" + start + "&pageSize=" + pageSize;
				var request =
					jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, url2, "GET", operationMetadata.Credentials, convention));
				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
													HandleReplicationStatusChanges);

				var json = (RavenJArray)await request.ReadResponseJsonAsync().ConfigureAwait(false);

				//NOTE: To review, I'm not confidence this is the correct way to deserialize the transformer definition
				return
					json.Select(
						x =>
						JsonConvert.DeserializeObject<TransformerDefinition>(((RavenJObject)x)["definition"].ToString(),
																			 new JsonToJsonConverter()))
							.ToArray();
			});
		}


		/// <summary>
		/// Resets the specified index asynchronously
		/// </summary>
		/// <param name="name">The name.</param>
		public Task ResetIndexAsync(string name)
		{
			return ExecuteWithReplication("RESET", operationMetadata =>
			{
				var httpJsonRequestAsync =
					jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, operationMetadata.Url + "/indexes/" + name,
																							 "RESET", operationMetadata.Credentials, convention));
				httpJsonRequestAsync.AddOperationHeaders(OperationsHeaders);
				httpJsonRequestAsync.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
																 HandleReplicationStatusChanges);

				return httpJsonRequestAsync.ReadResponseJsonAsync();
			});
		}

		public Task<string> PutIndexAsync<TDocument, TReduceResult>(string name,
					 IndexDefinitionBuilder<TDocument, TReduceResult> indexDef, bool overwrite = false)
		{
			return PutIndexAsync(name, indexDef.ToIndexDefinition(convention), overwrite);
		}

	    public Task<bool> IndexHasChangedAsync(string name, IndexDefinition indexDef)
	    {
	        return ExecuteWithReplication("POST", operationMetadata => DirectIndexHasChangedAsync(name, indexDef, operationMetadata));
	    }

	    private async Task<bool> DirectIndexHasChangedAsync(string name, IndexDefinition indexDef, OperationMetadata operationMetadata)
	    {
	        var requestUri = operationMetadata.Url.Indexes(name) + "?op=hasChanged";
            var webRequest = jsonRequestFactory.CreateHttpJsonRequest(
                new CreateHttpJsonRequestParams(this, requestUri, "POST", operationMetadata.Credentials, convention)
                    .AddOperationHeaders(OperationsHeaders));

            webRequest.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
                                                   HandleReplicationStatusChanges);

            var serializeObject = JsonConvert.SerializeObject(indexDef, Default.Converters);

            await webRequest.WriteAsync(serializeObject).ConfigureAwait(false);
            var result = await webRequest.ReadResponseJsonAsync().ConfigureAwait(false);
            return result.Value<bool>("Changed");
	    }

	    /// <summary>
		/// Puts the index definition for the specified name asynchronously
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="indexDef">The index def.</param>
		/// <param name="overwrite">Should overwrite index</param>
		public Task<string> PutIndexAsync(string name, IndexDefinition indexDef, bool overwrite)
		{
			return ExecuteWithReplication("PUT", operationMetadata => DirectPutIndexAsync(name, indexDef, overwrite, operationMetadata));
		}

		/// <summary>
		/// Puts the transformer definition for the specified name asynchronously
		/// </summary>
		public Task<string> PutTransformerAsync(string name, TransformerDefinition transformerDefinition)
		{
			return ExecuteWithReplication("PUT", operationMetadata => DirectPutTransformerAsync(name, transformerDefinition, operationMetadata));
		}

		/// <summary>
		/// Puts the index definition for the specified name asynchronously with url
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="indexDef">The index def.</param>
		/// <param name="overwrite">Should overwrite index</param>
		/// <param name="operationMetadata">The metadata that contains URL and credentials to perform operation</param>
		public async Task<string> DirectPutIndexAsync(string name, IndexDefinition indexDef, bool overwrite, OperationMetadata operationMetadata)
		{
			var requestUri = operationMetadata.Url + "/indexes/" + Uri.EscapeUriString(name) + "?definition=yes";
			var webRequest = jsonRequestFactory.CreateHttpJsonRequest(
				new CreateHttpJsonRequestParams(this, requestUri, "GET", operationMetadata.Credentials, convention)
					.AddOperationHeaders(OperationsHeaders));

			webRequest.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
												   HandleReplicationStatusChanges);

			try
			{
				await webRequest.ExecuteRequestAsync().ConfigureAwait(false);
				if (overwrite == false)
					throw new InvalidOperationException("Cannot put index: " + name + ", index already exists");
			}
			catch (ErrorResponseException e)
			{
				if (e.StatusCode != HttpStatusCode.NotFound)
					throw;
			}

			var request = jsonRequestFactory.CreateHttpJsonRequest(
				new CreateHttpJsonRequestParams(this, requestUri, "PUT", operationMetadata.Credentials, convention)
					.AddOperationHeaders(OperationsHeaders));

			var serializeObject = JsonConvert.SerializeObject(indexDef, Default.Converters);

			ErrorResponseException responseException;
			try
			{
				await request.WriteAsync(serializeObject).ConfigureAwait(false);
				var result = await request.ReadResponseJsonAsync().ConfigureAwait(false);
				return result.Value<string>("Index");
			}
			catch (ErrorResponseException e)
			{
				if (e.StatusCode != HttpStatusCode.BadRequest)
					throw;
				responseException = e;
			}
			var error =
				await
				responseException.TryReadErrorResponseObject(new { Error = "", Message = "", IndexDefinitionProperty = "", ProblematicText = "" }).ConfigureAwait(false);
			if (error == null)
				throw responseException;

			throw new IndexCompilationException(error.Message)
			{
				IndexDefinitionProperty = error.IndexDefinitionProperty,
				ProblematicText = error.ProblematicText
			};
		}

		/// <summary>
		/// Puts the transformer definition for the specified name asynchronously with url
		/// </summary>
		public async Task<string> DirectPutTransformerAsync(string name, TransformerDefinition transformerDefinition,
															OperationMetadata operationMetadata)
		{
			var requestUri = operationMetadata.Url + "/transformers/" + name;

			var request = jsonRequestFactory.CreateHttpJsonRequest(
				new CreateHttpJsonRequestParams(this, requestUri, "PUT", operationMetadata.Credentials, convention)
					.AddOperationHeaders(OperationsHeaders));

			var serializeObject = JsonConvert.SerializeObject(transformerDefinition, Default.Converters);

			ErrorResponseException responseException;
			try
			{
				await request.WriteAsync(serializeObject).ConfigureAwait(false);
				var result = await request.ReadResponseJsonAsync().ConfigureAwait(false);
				return result.Value<string>("Transformer");
			}
			catch (BadRequestException e)
			{
				throw new TransformCompilationException(e.Message);
			}
			catch (ErrorResponseException e)
			{
				if (e.StatusCode != HttpStatusCode.BadRequest)
					throw;

				responseException = e;
			}
			var error = await responseException.TryReadErrorResponseObject(new { Error = "", Message = "" }).ConfigureAwait(false);
			if (error == null)
				throw responseException;

			throw new TransformCompilationException(error.Message);
		}

		/// <summary>
		/// Deletes the index definition for the specified name asynchronously
		/// </summary>
		/// <param name="name">The name.</param>
		public Task DeleteIndexAsync(string name)
		{
			return ExecuteWithReplication("DELETE", operationMetadata => operationMetadata.Url.Indexes(name)
																				.ToJsonRequest(this, operationMetadata.Credentials, convention,
																							   OperationsHeaders, "DELETE")
																				.AddReplicationStatusHeaders(url, operationMetadata.Url,
																											 replicationInformer,
																											 convention
																												 .FailoverBehavior,
																											 HandleReplicationStatusChanges)
																		.ExecuteRequestAsync());
		}


		public Task<Operation> DeleteByIndexAsync(string indexName, IndexQuery queryToDelete, bool allowStale)
		{
			return ExecuteWithReplication("DELETE", async operationMetadata =>
			{
				string path = queryToDelete.GetIndexQueryUrl(operationMetadata.Url, indexName, "bulk_docs") + "&allowStale=" + allowStale;
				var request = jsonRequestFactory.CreateHttpJsonRequest(
						new CreateHttpJsonRequestParams(this, path, "DELETE", operationMetadata.Credentials, convention)
								.AddOperationHeaders(OperationsHeaders));

				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
						HandleReplicationStatusChanges);
				RavenJToken jsonResponse;
				try
				{
					jsonResponse = await request.ReadResponseJsonAsync().ConfigureAwait(false);
				}
				catch (ErrorResponseException e)
				{
					if (e.StatusCode == HttpStatusCode.NotFound)
						throw new InvalidOperationException("There is no index named: " + indexName, e);
					throw;
				}

				// Be compatible with the response from v2.0 server
				var opId = ((RavenJObject)jsonResponse)["OperationId"];

				if (opId == null || opId.Type != JTokenType.Integer)
					return null;

				return new Operation(this, opId.Value<long>());
			});
		}

		public Task DeleteTransformerAsync(string name)
		{
			return ExecuteWithReplication("DELETE", operationMetadata => operationMetadata.Url.Transformer(name)
																						  .ToJsonRequest(this, operationMetadata.Credentials, convention, OperationsHeaders, "DELETE")
																						  .AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges)
																						  .ExecuteRequestAsync());
		}

		/// <summary>
		/// Deletes the document for the specified id asynchronously
		/// </summary>
		/// <param name="id">The id.</param>
		public Task DeleteDocumentAsync(string id)
		{
			return ExecuteWithReplication("DELETE", operationMetadata =>
			{
				return operationMetadata.Url.Doc(id)
					.ToJsonRequest(this, operationMetadata.Credentials, convention, OperationsHeaders, "DELETE")
					.ExecuteRequestAsync();
			});
		}

		/// <summary>
		/// Sends a patch request for a specific document, ignoring the document's Etag
		/// </summary>
		/// <param name="key">Id of the document to patch</param>
		/// <param name="patches">Array of patch requests</param>
		/// <param name="ignoreMissing">true if the patch request should ignore a missing document, false to throw DocumentDoesNotExistException</param>
		public async Task<RavenJObject> PatchAsync(string key, PatchRequest[] patches, bool ignoreMissing)
		{
			var batchResults = await BatchAsync(new ICommandData[]
					{
						new PatchCommandData
							{
								Key = key,
								Patches = patches,
							}
					}).ConfigureAwait(false);
			if (!ignoreMissing && batchResults[0].PatchResult != null &&
				batchResults[0].PatchResult == PatchResult.DocumentDoesNotExists)
				throw new DocumentDoesNotExistsException("Document with key " + key + " does not exist.");
			return batchResults[0].AdditionalData;
		}

		/// <summary>
		/// Sends a patch request for a specific document
		/// </summary>
		/// <param name="key">Id of the document to patch</param>
		/// <param name="patches">Array of patch requests</param>
		/// <param name="etag">Require specific Etag [null to ignore]</param>
		public async Task<RavenJObject> PatchAsync(string key, PatchRequest[] patches, Etag etag)
		{
			var batchResults = await BatchAsync(new ICommandData[]
					{
						new PatchCommandData
							{
								Key = key,
								Patches = patches,
								Etag = etag
							}
					}).ConfigureAwait(false);
			return batchResults[0].AdditionalData;
		}

		/// <summary>
		/// Sends a patch request for a specific document which may or may not currently exist
		/// </summary>
		/// <param name="key">Id of the document to patch</param>
		/// <param name="patchesToExisting">Array of patch requests to apply to an existing document</param>
		/// <param name="patchesToDefault">Array of patch requests to apply to a default document when the document is missing</param>
		/// <param name="defaultMetadata">The metadata for the default document when the document is missing</param>
		public async Task<RavenJObject> PatchAsync(string key, PatchRequest[] patchesToExisting,
												   PatchRequest[] patchesToDefault, RavenJObject defaultMetadata)
		{
			var batchResults = await BatchAsync(new ICommandData[]
					{
						new PatchCommandData
							{
								Key = key,
								Patches = patchesToExisting,
								PatchesIfMissing = patchesToDefault,
								Metadata = defaultMetadata
							}
					}).ConfigureAwait(false);
			return batchResults[0].AdditionalData;
		}

		/// <summary>
		/// Sends a patch request for a specific document, ignoring the document's Etag
		/// </summary>
		/// <param name="key">Id of the document to patch</param>
		/// <param name="patch">The patch request to use (using JavaScript)</param>
		/// <param name="ignoreMissing">true if the patch request should ignore a missing document, false to throw DocumentDoesNotExistException</param>
		public async Task<RavenJObject> PatchAsync(string key, ScriptedPatchRequest patch, bool ignoreMissing)
		{
			var batchResults = await BatchAsync(new ICommandData[]
			{
				new ScriptedPatchCommandData
				{
					Key = key,
					Patch = patch,
				}
			}).ConfigureAwait(false);
			if (!ignoreMissing && batchResults[0].PatchResult != null &&
				batchResults[0].PatchResult == PatchResult.DocumentDoesNotExists)
				throw new DocumentDoesNotExistsException("Document with key " + key + " does not exist.");
			return batchResults[0].AdditionalData;
		}

		/// <summary>
		/// Sends a patch request for a specific document
		/// </summary>
		/// <param name="key">Id of the document to patch</param>
		/// <param name="patch">The patch request to use (using JavaScript)</param>
		/// <param name="etag">Require specific Etag [null to ignore]</param>
		public async Task<RavenJObject> PatchAsync(string key, ScriptedPatchRequest patch, Etag etag)
		{
			var batchResults = await BatchAsync(new ICommandData[]
			{
				new ScriptedPatchCommandData
				{
					Key = key,
					Patch = patch,
					Etag = etag
				}
			}).ConfigureAwait(false);
			return batchResults[0].AdditionalData;
		}

		/// <summary>
		/// Sends a patch request for a specific document which may or may not currently exist
		/// </summary>
		/// <param name="key">Id of the document to patch</param>
		/// <param name="patchExisting">The patch request to use (using JavaScript) to an existing document</param>
		/// <param name="patchDefault">The patch request to use (using JavaScript)  to a default document when the document is missing</param>
		/// <param name="defaultMetadata">The metadata for the default document when the document is missing</param>
		public async Task<RavenJObject> PatchAsync(string key, ScriptedPatchRequest patchExisting,
												   ScriptedPatchRequest patchDefault, RavenJObject defaultMetadata)
		{
			var batchResults = await BatchAsync(new ICommandData[]
			{
				new ScriptedPatchCommandData
				{
					Key = key,
					Patch = patchExisting,
					PatchIfMissing = patchDefault,
					Metadata = defaultMetadata
				}
			}).ConfigureAwait(false);
			return batchResults[0].AdditionalData;
		}

		/// <summary>
		/// Puts the document with the specified key in the database
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="etag">The etag.</param>
		/// <param name="document">The document.</param>
		/// <param name="metadata">The metadata.</param>
		public Task<PutResult> PutAsync(string key, Etag etag, RavenJObject document, RavenJObject metadata)
		{
			return ExecuteWithReplication("PUT", operationMetadata => DirectPutAsync(operationMetadata, key, etag, document, metadata));
		}

		private async Task<PutResult> DirectPutAsync(OperationMetadata operationMetadata, string key, Etag etag, RavenJObject document, RavenJObject metadata)
		{
			if (metadata == null)
				metadata = new RavenJObject();
			var method = String.IsNullOrEmpty(key) ? "POST" : "PUT";
			if (etag != null)
                metadata[Constants.MetadataEtagField] = new RavenJValue((string)etag);

			if (key != null)
				key = Uri.EscapeDataString(key);

			var request = jsonRequestFactory.CreateHttpJsonRequest(
					new CreateHttpJsonRequestParams(this, operationMetadata.Url + "/docs/" + key, method, metadata, operationMetadata.Credentials, convention)
						.AddOperationHeaders(OperationsHeaders));


			request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
												HandleReplicationStatusChanges);

			ErrorResponseException responseException;
			try
			{
				await request.WriteAsync(document).ConfigureAwait(false);
				var result = await request.ReadResponseJsonAsync().ConfigureAwait(false);
				if (result == null)
				{
					throw new InvalidOperationException("Got null response from the server after doing a put on " + key +", something is very wrong. Probably a garbled response.");
				}
				return convention.CreateSerializer().Deserialize<PutResult>(new RavenJTokenReader(result));
			}
			catch (ErrorResponseException e)
			{
				if (e.StatusCode != HttpStatusCode.Conflict)
					throw;
				responseException = e;
			}
			throw FetchConcurrencyException(responseException);
		}

		/// <summary>
		/// Create a new instance of <see cref="IDatabaseCommands"/> that will interacts
		/// with the specified database
		/// </summary>
		public IAsyncDatabaseCommands ForDatabase(string database)
		{
			if (database == Constants.SystemDatabase)
				return ForSystemDatabase();

			var databaseUrl = MultiDatabase.GetRootDatabaseUrl(url);
			databaseUrl = databaseUrl + "/databases/" + database + "/";
			if (databaseUrl == url)
				return this;
			return new AsyncServerClient(databaseUrl, convention, credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication, jsonRequestFactory, sessionId,
										 replicationInformerGetter, database, conflictListeners)
			{
				operationsHeaders = operationsHeaders
			};
		}

		/// <summary>
		/// Create a new instance of <see cref="IDatabaseCommands"/> that will interact
		/// with the root database. Useful if the database has works against a tenant database.
		/// </summary>
		public IAsyncDatabaseCommands ForSystemDatabase()
		{
			var databaseUrl = MultiDatabase.GetRootDatabaseUrl(url);
			if (databaseUrl == url)
				return this;
			return new AsyncServerClient(databaseUrl, convention, credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication, jsonRequestFactory, sessionId,
										 replicationInformerGetter, databaseName, conflictListeners)
			{
				operationsHeaders = operationsHeaders
			};
		}




		/// <summary>
		/// Gets or sets the operations headers.
		/// </summary>
		/// <value>The operations headers.</value>
		public NameValueCollection OperationsHeaders
		{
			get { return operationsHeaders; }
			set { operationsHeaders = value; }
		}
		public IAsyncGlobalAdminDatabaseCommands GlobalAdmin { get { return new AsyncAdminServerClient(this); } }
		public IAsyncAdminDatabaseCommands Admin { get { return new AsyncAdminServerClient(this); } }

		/// <summary>
		/// Begins an async get operation
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns></returns>
		public Task<JsonDocument> GetAsync(string key)
		{
			EnsureIsNotNullOrEmpty(key, "key");

			return ExecuteWithReplication("GET", operationMetadata => DirectGetAsync(operationMetadata, key));
		}




		/// <summary>
		/// Gets the transformer definition for the specified name asynchronously
		/// </summary>
		/// <param name="name">The name.</param>
		public Task<TransformerDefinition> GetTransformerAsync(string name)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				try
				{
					var transformerDefinitionJson = (RavenJObject)await operationMetadata.Url.Transformer(name)
						.ToJsonRequest(this, operationMetadata.Credentials, convention)
						.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges)
						.ReadResponseJsonAsync().ConfigureAwait(false);

					var value = transformerDefinitionJson.Value<RavenJObject>("Transformer");
					return convention.CreateSerializer().Deserialize<TransformerDefinition>(new RavenJTokenReader(value));
				}
				catch (ErrorResponseException we)
				{
					if (we.StatusCode == HttpStatusCode.NotFound)
						return null;

					throw;
				}
			});
		}


		/// <summary>
		/// Gets the index definition for the specified name asynchronously
		/// </summary>
		/// <param name="name">The name.</param>
		public Task<IndexDefinition> GetIndexAsync(string name)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				try
				{
					var indexDefinitionJson = (RavenJObject)await operationMetadata.Url.IndexDefinition(name)
																			   .ToJsonRequest(this, operationMetadata.Credentials, convention)
																			   .AddReplicationStatusHeaders(url, operationMetadata.Url,
																											replicationInformer,
																											convention.FailoverBehavior,
																											HandleReplicationStatusChanges)
																			   .ReadResponseJsonAsync().ConfigureAwait(false);

					var value = indexDefinitionJson.Value<RavenJObject>("Index");
					return convention.CreateSerializer().Deserialize<IndexDefinition>(new RavenJTokenReader(value));
				}
				catch (ErrorResponseException we)
				{
					if (we.StatusCode == HttpStatusCode.NotFound)
						return null;

					throw;
				}

			});
		}

		public async Task<JsonDocument> DirectGetAsync(OperationMetadata operationMetadata, string key)
		{
			if (key.Length > 127)
			{
				// avoid hitting UrlSegmentMaxLength limits in Http.sys
				var multiLoadResult = await DirectGetAsync(operationMetadata, new[] { key }, new string[0], null, new Dictionary<string, RavenJToken>(), false).ConfigureAwait(false);
				var result = multiLoadResult.Results.FirstOrDefault();
				if (result == null)
					return null;
				return SerializationHelper.RavenJObjectToJsonDocument(result);
			}

			var metadata = new RavenJObject();
			AddTransactionInformation(metadata);
			var createHttpJsonRequestParams = new CreateHttpJsonRequestParams(this, (operationMetadata.Url + "/docs?id=" + Uri.EscapeDataString(key)), "GET", metadata, operationMetadata.Credentials, convention);
			var request = jsonRequestFactory.CreateHttpJsonRequest(createHttpJsonRequestParams.AddOperationHeaders(OperationsHeaders))
											.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges);

			Task<JsonDocument> resolveConflictTask;
			try
			{
				var requestJson = await request.ReadResponseJsonAsync().ConfigureAwait(false);
				var docKey = request.ResponseHeaders.Get(Constants.DocumentIdFieldName) ?? key;
				docKey = Uri.UnescapeDataString(docKey);
				request.ResponseHeaders.Remove(Constants.DocumentIdFieldName);
				var deserializeJsonDocument = SerializationHelper.DeserializeJsonDocument(docKey, requestJson, request.ResponseHeaders, request.ResponseStatusCode);
				return deserializeJsonDocument;
			}
			catch (ErrorResponseException e)
			{
				switch (e.StatusCode)
				{
					case HttpStatusCode.NotFound:
						return null;
					case HttpStatusCode.Conflict:
						resolveConflictTask = ResolveConflict(e.ResponseString, e.Etag, operationMetadata, key);
						break;
					default:
						throw;
				}
			}
			return await resolveConflictTask.ConfigureAwait(false);
		}

		private async Task<JsonDocument> ResolveConflict(string httpResponse, Etag etag, OperationMetadata operationMetadata, string key)
		{
			var conflicts = new StringReader(httpResponse);
			var conflictsDoc = RavenJObject.Load(new RavenJsonTextReader(conflicts));
			var result =
				await TryResolveConflictOrCreateConcurrencyException(operationMetadata, key, conflictsDoc, etag).ConfigureAwait(false);
			if (result != null)
				throw result;
			return await DirectGetAsync(operationMetadata, key).ConfigureAwait(false);
		}

		/// <summary>
		/// Begins an async multi get operation
		/// </summary>
		public Task<MultiLoadResult> GetAsync(string[] keys, string[] includes, string transformer = null,
											  Dictionary<string, RavenJToken> transformerParameters = null, bool metadataOnly = false)
		{
			return ExecuteWithReplication("GET", operationMetadata => DirectGetAsync(operationMetadata, keys, includes, transformer, transformerParameters, metadataOnly));
		}

		private async Task<MultiLoadResult> DirectGetAsync(OperationMetadata operationMetadata, string[] keys, string[] includes, string transformer,
														   Dictionary<string, RavenJToken> transformerParameters, bool metadataOnly)
		{
			var path = operationMetadata.Url + "/queries/?";
			if (metadataOnly)
				path += "&metadata-only=true";
			if (includes != null && includes.Length > 0)
			{
				path += string.Join("&", includes.Select(x => "include=" + x).ToArray());
			}
			if (string.IsNullOrEmpty(transformer) == false)
				path += "&transformer=" + transformer;

			if (transformerParameters != null)
			{
				path = transformerParameters.Aggregate(path,
											 (current, transformerParam) =>
											 current + ("&" + string.Format("tp-{0}={1}", transformerParam.Key, transformerParam.Value)));
			}

			var metadata = new RavenJObject();
			AddTransactionInformation(metadata);

			var uniqueIds = new HashSet<string>(keys);
			HttpJsonRequest request;
			// if it is too big, we drop to POST (note that means that we can't use the HTTP cache any longer)
			// we are fine with that, requests to load > 128 items are going to be rare
			if (uniqueIds.Sum(x => x.Length) < 1024)
			{
				path += "&" + string.Join("&", uniqueIds.Select(x => "id=" + Uri.EscapeDataString(x)).ToArray());
				request =
					jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, path, "GET", metadata, operationMetadata.Credentials,
																							 convention)
																 .AddOperationHeaders(OperationsHeaders));

				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
													HandleReplicationStatusChanges);

				var result = await request.ReadResponseJsonAsync().ConfigureAwait(false);
				return await CompleteMultiGetAsync(operationMetadata, keys, includes, result).ConfigureAwait(false);
			}
			request =
				jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, path, "POST", metadata, operationMetadata.Credentials, convention)
															 .AddOperationHeaders(OperationsHeaders));

			await request.WriteAsync(new RavenJArray(uniqueIds)).ConfigureAwait(false);
			var responseResult = await request.ReadResponseJsonAsync().ConfigureAwait(false);
			return await CompleteMultiGetAsync(operationMetadata, keys, includes, responseResult).ConfigureAwait(false);
		}

		private async Task<MultiLoadResult> CompleteMultiGetAsync(OperationMetadata operationMetadata, string[] keys, string[] includes,
																  RavenJToken result)
		{
			ErrorResponseException responseException;
			try
			{
				var uniqueKeys = new HashSet<string>(keys);

				var results =  result
					.Value<RavenJArray>("Results")
					.Select(x=> x as RavenJObject)
					.ToList();

				var documents = results
                    .Where(x => x != null && x.ContainsKey("@metadata") && x["@metadata"].Value<string>("@id") != null)
					.ToDictionary(x => x["@metadata"].Value<string>("@id"), x => x, StringComparer.OrdinalIgnoreCase);

				if (results.Count >= uniqueKeys.Count)
				{
					for (var i = 0; i < uniqueKeys.Count; i++)
					{
						var key = keys[i];
						if (documents.ContainsKey(key)) 
							continue;

						documents.Add(key, results[i]);
					}
				}

				var multiLoadResult = new MultiLoadResult
				{
					Includes = result.Value<RavenJArray>("Includes").Cast<RavenJObject>().ToList(),
					Results = documents.Count == 0 ? results : keys.Select(key => documents.ContainsKey(key) ? documents[key] : null).ToList()
				};

				var docResults = multiLoadResult.Results.Concat(multiLoadResult.Includes);

				return
					await
					RetryOperationBecauseOfConflict(operationMetadata, docResults, multiLoadResult,
													() => DirectGetAsync(operationMetadata, keys, includes, null, null, false)).ConfigureAwait(false);
			}
			catch (ErrorResponseException e)
			{
				if (e.StatusCode != HttpStatusCode.Conflict)
					throw;
				responseException = e;
			}
			throw FetchConcurrencyException(responseException);
		}

		/// <summary>
		/// Begins an async get operation for documents
		/// </summary>
		/// <remarks>
		/// This is primarily useful for administration of a database
		/// </remarks>
		public Task<JsonDocument[]> GetDocumentsAsync(int start, int pageSize, bool metadataOnly = false)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				var result = await GetDocumentsInternalAsync(start, null, pageSize, operationMetadata, metadataOnly).ConfigureAwait(false);

				return result.Cast<RavenJObject>()
							 .ToJsonDocuments()
							 .ToArray();
			});
		}

		public Task<JsonDocument[]> GetDocumentsAsync(Etag fromEtag, int pageSize, bool metadataOnly = false)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				var result = await GetDocumentsInternalAsync(null, fromEtag, pageSize, operationMetadata, metadataOnly).ConfigureAwait(false);
				return result.Cast<RavenJObject>()
							 .ToJsonDocuments()
							 .ToArray();
			});
		}

		public async Task<RavenJArray> GetDocumentsInternalAsync(int? start, Etag fromEtag, int pageSize, OperationMetadata operationMetadata, bool metadataOnly = false)
		{
			var requestUri = url + "/docs/?";
			if (start.HasValue && start.Value > 0)
			{
				requestUri += "start=" + start;
			}
			else if (fromEtag != null)
			{
				requestUri += "etag=" + fromEtag;
			}
			requestUri += "&pageSize=" + pageSize;
			if (metadataOnly)
				requestUri += "&metadata-only=true";
			var @params = new CreateHttpJsonRequestParams(this, requestUri, "GET", operationMetadata.Credentials, convention)
				.AddOperationHeaders(OperationsHeaders);
			return (RavenJArray)await jsonRequestFactory.CreateHttpJsonRequest(@params)
														.ReadResponseJsonAsync().ConfigureAwait(false);
		}

		public Task<Operation> UpdateByIndexAsync(string indexName, IndexQuery queryToUpdate, ScriptedPatchRequest patch, bool allowStale)
		{
			var requestData = RavenJObject.FromObject(patch).ToString(Formatting.Indented);
			return UpdateByIndexImpl(indexName, queryToUpdate, allowStale, requestData, "EVAL");
		}

		public Task<Operation> UpdateByIndexAsync(string indexName, IndexQuery queryToUpdate, PatchRequest[] patchRequests,
				bool allowStale = false)
		{
			var requestData = new RavenJArray(patchRequests.Select(x => x.ToJson())).ToString(Formatting.Indented);
			return UpdateByIndexImpl(indexName, queryToUpdate, allowStale, requestData, "PATCH");
		}

		public async Task<MultiLoadResult> MoreLikeThisAsync(MoreLikeThisQuery query)
		{
			var requestUrl = query.GetRequestUri();
			EnsureIsNotNullOrEmpty(requestUrl, "url");
			var result = await ExecuteWithReplication("GET", async operationMetadata =>
			{
				var metadata = new RavenJObject();
				AddTransactionInformation(metadata);
				var request = jsonRequestFactory.CreateHttpJsonRequest(
						new CreateHttpJsonRequestParams(this, operationMetadata.Url + requestUrl, "GET", metadata, operationMetadata.Credentials, convention)
								.AddOperationHeaders(OperationsHeaders));

				return await request.ReadResponseJsonAsync();
			}).ConfigureAwait(false);
			return ((RavenJObject)result).Deserialize<MultiLoadResult>(convention);
		}

		public Task<long> NextIdentityForAsync(string name)
		{
			return ExecuteWithReplication("POST", async operationMetadata =>
			{
				var request = jsonRequestFactory.CreateHttpJsonRequest(
						new CreateHttpJsonRequestParams(this, operationMetadata.Url + "/identity/next?name=" + Uri.EscapeDataString(name), "POST", operationMetadata.Credentials, convention)
								.AddOperationHeaders(OperationsHeaders));
				var readResponseJson = await request.ReadResponseJsonAsync().ConfigureAwait(false);
				return readResponseJson.Value<long>("Value");
			});
		}

		public Task<long> SeedIdentityForAsync(string name, long value)
		{
			return ExecuteWithReplication("POST", async operationMetadata =>
			{
				var request = jsonRequestFactory.CreateHttpJsonRequest(
					new CreateHttpJsonRequestParams(this, url + "/identity/seed?name=" + Uri.EscapeDataString(name) + "&value=" + Uri.EscapeDataString(value.ToString()), "POST", operationMetadata.Credentials, convention)
						.AddOperationHeaders(OperationsHeaders));

				var readResponseJson = await request.ReadResponseJsonAsync().ConfigureAwait(false);

				return readResponseJson.Value<long>("Value");
			});
		}

		private Task<Operation> UpdateByIndexImpl(string indexName, IndexQuery queryToUpdate, bool allowStale, String requestData, String method)
		{
			return ExecuteWithReplication(method, async operationMetadata =>
			{
				string path = queryToUpdate.GetIndexQueryUrl(operationMetadata.Url, indexName, "bulk_docs") + "&allowStale=" + allowStale;

				var request = jsonRequestFactory.CreateHttpJsonRequest(
						new CreateHttpJsonRequestParams(this, path, method, operationMetadata.Credentials, convention));
				request.AddOperationHeaders(OperationsHeaders);
				await request.WriteAsync(requestData).ConfigureAwait(false);

				RavenJToken jsonResponse;
				try
				{
					jsonResponse = await request.ReadResponseJsonAsync().ConfigureAwait(false);
				}
				catch (ErrorResponseException e)
				{
					if (e.StatusCode == HttpStatusCode.NotFound)
						throw new InvalidOperationException("There is no index named: " + indexName);
					throw;
				}

				return new Operation(this, jsonResponse.Value<long>("OperationId"));
			});
		}

		/// <summary>
		/// Using the given Index, calculate the facets as per the specified doc with the given start and pageSize
		/// </summary>
		/// <param name="index">Name of the index</param>
		/// <param name="query">Query to build facet results</param>
		/// <param name="facetSetupDoc">Name of the FacetSetup document</param>
		/// <param name="start">Start index for paging</param>
		/// <param name="pageSize">Paging PageSize. If set, overrides Facet.MaxResults</param>
		public Task<FacetResults> GetFacetsAsync(string index, IndexQuery query, string facetSetupDoc, int start = 0,
												 int? pageSize = null)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				var requestUri = operationMetadata.Url + string.Format("/facets/{0}?facetDoc={1}{2}&facetStart={3}&facetPageSize={4}",
				Uri.EscapeUriString(index),
				Uri.EscapeDataString(facetSetupDoc),
				query.GetMinimalQueryString(),
				start,
				pageSize);

				var request = jsonRequestFactory.CreateHttpJsonRequest(
					new CreateHttpJsonRequestParams(this, requestUri, "GET", operationMetadata.Credentials, convention)
						.AddOperationHeaders(OperationsHeaders));

				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
													HandleReplicationStatusChanges);

				var json = (RavenJObject)await request.ReadResponseJsonAsync().ConfigureAwait(false);
				return json.JsonDeserialization<FacetResults>();
			});
		}

		public Task<FacetResults[]> GetMultiFacetsAsync(FacetQuery[] facetedQueries)
		{
			return ExecuteWithReplication("POST", async operationMetadata =>
			{

				var requestUri = operationMetadata.Url + "/facets/multisearch";

				var request = jsonRequestFactory.CreateHttpJsonRequest(
					new CreateHttpJsonRequestParams(this, requestUri, "POST", operationMetadata.Credentials, convention)
						.AddOperationHeaders(OperationsHeaders));

				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
													HandleReplicationStatusChanges);

				var data = JsonConvert.SerializeObject(facetedQueries);
				await request.WriteAsync(data).ConfigureAwait(false);
				var response = (RavenJArray)await request.ReadResponseJsonAsync().ConfigureAwait(false);

				return convention.CreateSerializer().Deserialize<FacetResults[]>(new RavenJTokenReader(response));
			});
		}

		/// <summary>
		/// Using the given Index, calculate the facets as per the specified doc with the given start and pageSize
		/// </summary>
		/// <param name="index">Name of the index</param>
		/// <param name="query">Query to build facet results</param>
		/// <param name="facets">List of facets</param>
		/// <param name="start">Start index for paging</param>
		/// <param name="pageSize">Paging PageSize. If set, overrides Facet.MaxResults</param>
		public Task<FacetResults> GetFacetsAsync(string index, IndexQuery query, List<Facet> facets, int start = 0,
												 int? pageSize = null)
		{

			string facetsJson = JsonConvert.SerializeObject(facets);
			var method = facetsJson.Length > 1024 ? "POST" : "GET";
			return ExecuteWithReplication(method, async operationMetadata =>
			{
				var requestUri = operationMetadata.Url + string.Format("/facets/{0}?{1}&facetStart={2}&facetPageSize={3}",
																Uri.EscapeUriString(index),
																query.GetMinimalQueryString(),
																start,
																pageSize);

				if (method == "GET")
					requestUri += "&facets=" + Uri.EscapeDataString(facetsJson);

				var request = jsonRequestFactory.CreateHttpJsonRequest(
					new CreateHttpJsonRequestParams(this, requestUri, method, operationMetadata.Credentials, convention)
						.AddOperationHeaders(OperationsHeaders))
												.AddReplicationStatusHeaders(Url, operationMetadata.Url, replicationInformer,
																			 convention.FailoverBehavior,
																			 HandleReplicationStatusChanges);

				if (method != "GET")
					request.WriteAsync(facetsJson).Wait();

				var json = (RavenJObject)await request.ReadResponseJsonAsync().ConfigureAwait(false);
				return json.JsonDeserialization<FacetResults>();
			});
		}

		public Task<LogItem[]> GetLogsAsync(bool errorsOnly)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				var requestUri = url + "/logs";
				if (errorsOnly)
					requestUri += "?type=error";

				var request =
					jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, requestUri, "GET",
																							 operationMetadata.Credentials, convention));
				request.AddOperationHeaders(OperationsHeaders);
				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
													HandleReplicationStatusChanges);

				var result = await request.ReadResponseJsonAsync().ConfigureAwait(false);
				return convention.CreateSerializer().Deserialize<LogItem[]>(new RavenJTokenReader(result));
			});
		}

		public async Task<LicensingStatus> GetLicenseStatusAsync()
		{
			var request =
				jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, (url + "/license/status"),
																						 "GET", credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication, convention));
			request.AddOperationHeaders(OperationsHeaders);

			var result = await request.ReadResponseJsonAsync().ConfigureAwait(false);
			return convention.CreateSerializer().Deserialize<LicensingStatus>(new RavenJTokenReader(result));
		}

		public async Task<BuildNumber> GetBuildNumberAsync()
		{
			var request =
				jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, (url + "/build/version"),
																						 "GET", credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication, convention));
			request.AddOperationHeaders(OperationsHeaders);

			var result = await request.ReadResponseJsonAsync().ConfigureAwait(false);
			return convention.CreateSerializer().Deserialize<BuildNumber>(new RavenJTokenReader(result));
		}
		public async Task<IndexMergeResults> GetIndexMergeSuggestionsAsync()
		{
			var request =
				jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, (url + "/debug/suggest-index-merge"),
																						 "GET", credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication, convention));
			request.AddOperationHeaders(OperationsHeaders);

			var result = await request.ReadResponseJsonAsync().ConfigureAwait(false);
			return convention.CreateSerializer().Deserialize<IndexMergeResults>(new RavenJTokenReader(result));
		}

		public Task<JsonDocument[]> StartsWithAsync(string keyPrefix, string matches, int start, int pageSize,
									RavenPagingInformation pagingInformation = null, 
									bool metadataOnly = false, 
									string exclude = null,
									string transformer = null, 
									Dictionary<string, RavenJToken> transformerParameters = null,
									string skipAfter = null)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				var metadata = new RavenJObject();
				AddTransactionInformation(metadata);

				var actualStart = start;

				var nextPage = pagingInformation != null && pagingInformation.IsForPreviousPage(start, pageSize);
				if (nextPage)
					actualStart = pagingInformation.NextPageStart;

				var actualUrl = string.Format("{0}/docs?startsWith={1}&matches={5}&exclude={4}&start={2}&pageSize={3}", operationMetadata.Url,
											  Uri.EscapeDataString(keyPrefix), actualStart.ToInvariantString(), pageSize.ToInvariantString(), exclude, matches);

				if (metadataOnly)
					actualUrl += "&metadata-only=true";

				if (string.IsNullOrEmpty(skipAfter) == false)
					actualUrl += "&skipAfter=" + Uri.EscapeDataString(skipAfter);

				if (string.IsNullOrEmpty(transformer) == false)
				{
					actualUrl += "&transformer=" + transformer;

					if (transformerParameters != null)
					{
						actualUrl = transformerParameters.Aggregate(actualUrl,
											 (current, transformerParamater) =>
											 current + ("&" + string.Format("tp-{0}={1}", transformerParamater.Key, transformerParamater.Value)));
					}
				}

				if (nextPage)
					actualUrl += "&next-page=true";

				var request = jsonRequestFactory.CreateHttpJsonRequest(
					new CreateHttpJsonRequestParams(this, actualUrl, "GET", metadata, operationMetadata.Credentials, convention)
						.AddOperationHeaders(OperationsHeaders));

				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges);

				var result = (RavenJArray)await request.ReadResponseJsonAsync().ConfigureAwait(false);

				int nextPageStart;
				if (pagingInformation != null && int.TryParse(request.ResponseHeaders[Constants.NextPageStart], out nextPageStart))
					pagingInformation.Fill(start, pageSize, nextPageStart);

				var docResults = result.OfType<RavenJObject>().ToList();
				var startsWithResults = SerializationHelper.RavenJObjectsToJsonDocuments(docResults.Select(x => (RavenJObject)x.CloneToken()))
																				.ToArray();
				return await RetryOperationBecauseOfConflict(operationMetadata, docResults, startsWithResults,
													() => StartsWithAsync(keyPrefix, matches, start, pageSize, pagingInformation,
														  metadataOnly, exclude, transformer, transformerParameters, skipAfter),
													conflictedResultId =>
												   new ConflictException(
													   "Conflict detected on " +
													   conflictedResultId.Substring(0, conflictedResultId.IndexOf("/conflicts/", StringComparison.InvariantCulture)) +
													   ", conflict must be resolved before the document will be accessible", true)
												   {
													   ConflictedVersionIds = new[] { conflictedResultId }
												   }).ConfigureAwait(false);
			});
		}

		/// <summary>
		/// Perform a single POST request containing multiple nested GET requests
		/// </summary>
		public Task<GetResponse[]> MultiGetAsync(GetRequest[] requests)
		{
			return ExecuteWithReplication("GET", async operationMetadata => // logical GET even though the actual request is a POST
			{
				var multiGetOperation = new MultiGetOperation(this, convention, operationMetadata.Url, requests);

				var httpJsonRequest =
					jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this,
																							 multiGetOperation.RequestUri,
																							 "POST", operationMetadata.Credentials, convention)
					.AddOperationHeaders(OperationsHeaders));

				httpJsonRequest.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
															HandleReplicationStatusChanges);

				var requestsForServer = multiGetOperation.PreparingForCachingRequest(jsonRequestFactory);

				var postedData = JsonConvert.SerializeObject(requestsForServer);

				if (multiGetOperation.CanFullyCache(jsonRequestFactory, httpJsonRequest, postedData))
				{
					var cachedResponses = multiGetOperation.HandleCachingResponse(new GetResponse[requests.Length], jsonRequestFactory);
					return cachedResponses;
				}

				await httpJsonRequest.WriteAsync(postedData).ConfigureAwait(false);
				var result = await httpJsonRequest.ReadResponseJsonAsync().ConfigureAwait(false);
				var responses = convention.CreateSerializer().Deserialize<GetResponse[]>(new RavenJTokenReader(result));
				return multiGetOperation.HandleCachingResponse(responses, jsonRequestFactory);
			});
		}

		/// <summary>
		/// Begins the async query.
		/// </summary>
		/// <param name="index">The index.</param>
		/// <param name="query">The query.</param>
		/// <param name="includes">The include paths</param>
		/// <param name="metadataOnly">Load just the document metadata</param>
		/// <returns></returns>
		public Task<QueryResult> QueryAsync(string index, IndexQuery query, string[] includes = null, bool metadataOnly = false, bool indexEntriesOnly = false)
		{
			var method = query.Query != null && query.Query.Length > MaxQuerySizeForGetRequest ? "POST" : "GET";

			return ExecuteWithReplication(method, async operationMetadata =>
			{
				EnsureIsNotNullOrEmpty(index, "index");
				string path = query.GetIndexQueryUrl(operationMetadata.Url, index, "indexes", includeQuery: method == "GET");

				if (metadataOnly)
					path += "&metadata-only=true";
				if (indexEntriesOnly)
					path += "&debug=entries";
				if (includes != null && includes.Length > 0)
				{
					path += "&" + string.Join("&", includes.Select(x => "include=" + x).ToArray());
				}

				if (method == "POST")
					path += "&postQuery=true";

				var request = jsonRequestFactory.CreateHttpJsonRequest(
						new CreateHttpJsonRequestParams(this, path, method, operationMetadata.Credentials, convention)
						{
							AvoidCachingRequest = query.DisableCaching
						}.AddOperationHeaders(OperationsHeaders));

				request.AddReplicationStatusHeaders(operationMetadata.Url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges);

				if (method == "POST")
					await request.WriteAsync(query.Query).ConfigureAwait(false);

				ErrorResponseException responseException;
				try
				{
					var result = (RavenJObject) await request.ReadResponseJsonAsync().ConfigureAwait(false);
					if (result == null)
						throw new InvalidOperationException("Got empty response from the server for the following request: " + request.Url);

					var queryResult = SerializationHelper.ToQueryResult(result, request.ResponseHeaders.GetEtagHeader(),
						request.ResponseHeaders.Get("Temp-Request-Time"), request.Size);

					var docResults = queryResult.Results.Concat(queryResult.Includes);
					return await RetryOperationBecauseOfConflict(operationMetadata, docResults, queryResult,
						() => QueryAsync(index, query, includes, metadataOnly, indexEntriesOnly),
						conflictedResultId =>
							new ConflictException(
								"Conflict detected on " +
								conflictedResultId.Substring(0, conflictedResultId.IndexOf("/conflicts/", StringComparison.InvariantCulture)) +
								", conflict must be resolved before the document will be accessible", true)
							{
								ConflictedVersionIds = new[] {conflictedResultId}
							}).ConfigureAwait(false);
				}
				catch (ErrorResponseException e)
				{
					if (e.StatusCode == HttpStatusCode.NotFound)
					{
						var text = e.ResponseString;
						if (text.Contains("maxQueryString"))
							throw new ErrorResponseException(e, text);
						throw new ErrorResponseException(e, "There is no index named: " + index);
					}
					responseException = e;
				}
				if (HandleException(responseException))
					return null;
				throw responseException;
			});
		}

		/// <summary>
		/// Attempts to handle an exception raised when receiving a response from the server
		/// </summary>
		/// <param name="e">The exception to handle</param>
		/// <returns>returns true if the exception is handled, false if it should be thrown</returns>
		private bool HandleException(ErrorResponseException e)
		{
			if (e.StatusCode == HttpStatusCode.InternalServerError)
			{
				var content = e.ResponseString;
				var json = RavenJObject.Load(new JsonTextReader(new StringReader(content)));
				var error = json.Deserialize<ServerRequestError>(convention);

				throw new ErrorResponseException(e, error.Error);
			}
			return false;
		}

		/// <summary>
		/// Returns a list of suggestions based on the specified suggestion query.
		/// </summary>
		/// <param name="index">The index to query for suggestions</param>
		/// <param name="suggestionQuery">The suggestion query.</param>
		public Task<SuggestionQueryResult> SuggestAsync(string index, SuggestionQuery suggestionQuery)
		{
			if (suggestionQuery == null)
				throw new ArgumentNullException("suggestionQuery");

			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				var requestUri = operationMetadata.Url + string.Format("/suggest/{0}?term={1}&field={2}&max={3}&popularity={4}",
					Uri.EscapeUriString(index),
					Uri.EscapeDataString(suggestionQuery.Term),
					Uri.EscapeDataString(suggestionQuery.Field),
					Uri.EscapeDataString(suggestionQuery.MaxSuggestions.ToInvariantString()),
															  suggestionQuery.Popularity);

				if (suggestionQuery.Accuracy.HasValue)
					requestUri += "&accuracy=" + suggestionQuery.Accuracy.Value.ToInvariantString();

				if (suggestionQuery.Distance.HasValue)
					requestUri += "&distance=" + suggestionQuery.Distance;

				var request = jsonRequestFactory.CreateHttpJsonRequest(
					new CreateHttpJsonRequestParams(this, requestUri, "GET", operationMetadata.Credentials, convention)
						.AddOperationHeaders(OperationsHeaders));

				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
													HandleReplicationStatusChanges);

				var json = (RavenJObject)await request.ReadResponseJsonAsync().ConfigureAwait(false);
				return new SuggestionQueryResult
				{
					Suggestions = ((RavenJArray)json["Suggestions"]).Select(x => x.Value<string>()).ToArray(),
				};
			});
		}

		/// <summary>
		/// Begins the async batch operation
		/// </summary>
		/// <param name="commandDatas">The command data.</param>
		/// <returns></returns>
		public Task<BatchResult[]> BatchAsync(ICommandData[] commandDatas)
		{
			return ExecuteWithReplication("POST", async operationMetadata =>
			{
				var metadata = new RavenJObject();
				AddTransactionInformation(metadata);
				var req =
					jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, operationMetadata.Url + "/bulk_docs", "POST",
																							 metadata, operationMetadata.Credentials, convention)
					.AddOperationHeaders(OperationsHeaders));

				req.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
												HandleReplicationStatusChanges);

				var jArray = new RavenJArray(commandDatas.Select(x => x.ToJson()));

				ErrorResponseException responseException;
				try
				{
					await req.WriteAsync(jArray).ConfigureAwait(false);
					var response = (RavenJArray)await req.ReadResponseJsonAsync().ConfigureAwait(false);
					if (response == null)
					{
						throw new InvalidOperationException("Got null response from the server after doing a batch, something is very wrong. Probably a garbled response.");
					}
					return convention.CreateSerializer().Deserialize<BatchResult[]>(new RavenJTokenReader(response));
				}
				catch (ErrorResponseException e)
				{
					if (e.StatusCode != HttpStatusCode.Conflict)
						throw;
					responseException = e;
				}
				throw FetchConcurrencyException(responseException);
			});
		}

		private static ConcurrencyException FetchConcurrencyException(ErrorResponseException e)
		{
			var text = e.ResponseString;
			var errorResults = JsonConvert.DeserializeAnonymousType(text, new
			{
				url = (string)null,
				actualETag = Etag.Empty,
				expectedETag = Etag.Empty,
				error = (string)null
			});
			return new ConcurrencyException(errorResults.error)
			{
				ActualETag = errorResults.actualETag,
				ExpectedETag = errorResults.expectedETag
			};
		}

		private void AddTransactionInformation(RavenJObject metadata)
		{
			if (convention.EnlistInDistributedTransactions == false)
				return;

			var transactionInformation = RavenTransactionAccessor.GetTransactionInformation();
			if (transactionInformation == null)
				return;

			string txInfo = string.Format("{0}, {1}", transactionInformation.Id, transactionInformation.Timeout);
			metadata["Raven-Transaction-Information"] = new RavenJValue(txInfo);
		}

		private static void EnsureIsNotNullOrEmpty(string key, string argName)
		{
			if (string.IsNullOrEmpty(key))
				throw new ArgumentException("Key cannot be null or empty", argName);
		}

		/// <summary>
		/// Begins retrieving the statistics for the database
		/// </summary>
		/// <returns></returns>
		public async Task<DatabaseStatistics> GetStatisticsAsync()
		{
			var json = (RavenJObject)await url.Stats()
				.ToJsonRequest(this, credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication, convention)
											   .ReadResponseJsonAsync().ConfigureAwait(false);

			return json.Deserialize<DatabaseStatistics>(convention);
		}

        [Obsolete("Use RavenFS instead.")]
		public Task<AttachmentInformation[]> GetAttachmentsAsync(int start, Etag startEtag, int pageSize)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				var request = jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, operationMetadata.Url + "/static/?pageSize=" + pageSize + "&etag=" + startEtag + "&start=" + start, "GET", operationMetadata.Credentials, convention)
					.AddOperationHeaders(OperationsHeaders));

				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges);

				var json = (RavenJArray)await request.ReadResponseJsonAsync().ConfigureAwait(false);
				return convention.CreateSerializer().Deserialize<AttachmentInformation[]>(new RavenJTokenReader(json));
			});
		}

		/// <summary>
		/// Puts the attachment with the specified key asynchronously
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="etag">The etag.</param>
		/// <param name="data">The data stream.</param>
		/// <param name="metadata">The metadata.</param>
        [Obsolete("Use RavenFS instead.")]
        public Task PutAttachmentAsync(string key, Etag etag, Stream data, RavenJObject metadata)
		{
			return ExecuteWithReplication("PUT", operationMetadata =>
			{
				if (metadata == null)
					metadata = new RavenJObject();

				if (etag != null)
                    metadata[Constants.MetadataEtagField] = new RavenJValue((string)etag);

				var request =
					jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, Static(operationMetadata.Url, key), "PUT",
																							 metadata, operationMetadata.Credentials, convention));
				request.AddOperationHeaders(OperationsHeaders);
				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
													HandleReplicationStatusChanges);

				return request.WriteAsync(data);
			});
		}

		/// <summary>
		/// Gets the attachment by the specified key asynchronously
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns></returns>
        [Obsolete("Use RavenFS instead.")]
        public Task<Attachment> GetAttachmentAsync(string key)
		{
			EnsureIsNotNullOrEmpty(key, "key");

			return ExecuteWithReplication("GET", operationMetadata => DirectGetAttachmentAsync(key, operationMetadata, "GET"));
		}

        [Obsolete("Use RavenFS instead.")]
		public Task<Attachment> HeadAttachmentAsync(string key)
		{
			EnsureIsNotNullOrEmpty(key, "key");

			return ExecuteWithReplication("HEAD", operationMetadata => DirectGetAttachmentAsync(key, operationMetadata, "HEAD"));
		}

        [Obsolete("Use RavenFS instead.")]
		private async Task<Attachment> DirectGetAttachmentAsync(string key, OperationMetadata operationMetadata, string method)
		{
			var metadata = new RavenJObject();
			AddTransactionInformation(metadata);
			var createHttpJsonRequestParams = new CreateHttpJsonRequestParams(this, (operationMetadata.Url + "/static/" + key), method, metadata, operationMetadata.Credentials, convention);
			var request = jsonRequestFactory.CreateHttpJsonRequest(createHttpJsonRequestParams.AddOperationHeaders(OperationsHeaders))
											.AddReplicationStatusHeaders(Url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges);

			ErrorResponseException responseException;
			try
			{
				var result = await request.ReadResponseBytesAsync().ConfigureAwait(false);
				HandleReplicationStatusChanges(request.ResponseHeaders, Url, operationMetadata.Url);

				if (method == "GET")
				{
					var memoryStream = new MemoryStream(result);
					return new Attachment
					{
						Key = key,
						Data = () => memoryStream,
						Size = result.Length,
						Etag = request.ResponseHeaders.GetEtagHeader(),
						Metadata = request.ResponseHeaders.FilterHeadersAttachment()
					};
				}
				else
				{
					return new Attachment
					{
						Key = key,
						Data = () =>
						{
							throw new InvalidOperationException("Cannot get attachment data because it was loaded using: " + method);
						},
						Size = int.Parse(request.ResponseHeaders["Content-Length"]),
						Etag = request.ResponseHeaders.GetEtagHeader(),
						Metadata = request.ResponseHeaders.FilterHeadersAttachment()
					};
				}
			}
			catch (ErrorResponseException e)
			{
				if (e.StatusCode == HttpStatusCode.NotFound)
					return null;
				if (e.StatusCode != HttpStatusCode.Conflict)
					throw;
				responseException = e;
			}



			using (var stream = await responseException.Response.GetResponseStreamWithHttpDecompression().ConfigureAwait(false))
			{
				string[] conflictIds;
				if (method == "GET")
				{
					var conflictsDoc = stream.ToJObject();
					conflictIds = conflictsDoc.Value<RavenJArray>("Conflicts").Select(x => x.Value<string>()).ToArray();
				}
				else
				{
					conflictIds = new[] { "Cannot get conflict ids in HEAD requesT" };
				}

				throw new ConflictException("Conflict detected on " + key + ", conflict must be resolved before the attachment will be accessible", true)
				{
					ConflictedVersionIds = conflictIds,
					Etag = responseException.Etag
				};
			}
		}


		/// <summary>
		/// Deletes the attachment with the specified key asynchronously
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="etag">The etag.</param>
        [Obsolete("Use RavenFS instead.")]
        public Task DeleteAttachmentAsync(string key, Etag etag)
		{
			return ExecuteWithReplication("DELETE", operationMetadata =>
			{
				var metadata = new RavenJObject();

				if (etag != null)
                    metadata[Constants.MetadataEtagField] = new RavenJValue((string)etag);

				var request =
					jsonRequestFactory.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, Static(operationMetadata.Url, key), "DELETE",
																							 metadata, operationMetadata.Credentials, convention));
				request.AddOperationHeaders(OperationsHeaders);
				request.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
													HandleReplicationStatusChanges);

				return request.ExecuteRequestAsync();
			});
		}

		public static string Static(string url, string key)
		{
			return url + "/static/" + Uri.EscapeUriString(key);
		}

		/// <summary>
		/// Disable all caching within the given scope
		/// </summary>
		public IDisposable DisableAllCaching()
		{
			return jsonRequestFactory.DisableAllCaching();
		}

		///<summary>
		/// Get the possible terms for the specified field in the index asynchronously
		/// You can page through the results by use fromValue parameter as the 
		/// starting point for the next query
		///</summary>
		///<returns></returns>
		public Task<string[]> GetTermsAsync(string index, string field, string fromValue, int pageSize)
		{
			return ExecuteWithReplication("GET", async operationMetadata =>
			{
				var result = await operationMetadata.Url.Terms(index, field, fromValue, pageSize)
					.ToJsonRequest(this, operationMetadata.Credentials, convention)
											   .AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer,
																			convention.FailoverBehavior,
																			HandleReplicationStatusChanges)
											   .ReadResponseJsonAsync().ConfigureAwait(false);
				var json = ((RavenJArray)result);
				return json.Select(x => x.Value<string>()).ToArray();
			});
		}

		/// <summary>
		/// The profiling information
		/// </summary>
		public ProfilingInformation ProfilingInformation
		{
			get { return profilingInformation; }
		}

		/// <summary>
		/// Notify when the failover status changed
		/// </summary>
		public event EventHandler<FailoverStatusChangedEventArgs> FailoverStatusChanged
		{
			add { replicationInformer.FailoverStatusChanged += value; }
			remove { replicationInformer.FailoverStatusChanged -= value; }
		}

		/// <summary>
		/// Force the database commands to read directly from the master, unless there has been a failover.
		/// </summary>
		public IDisposable ForceReadFromMaster()
		{
			var old = readStripingBase;
			readStripingBase = -1;// this means that will have to use the master url first
			return new DisposableAction(() => readStripingBase = old);
		}

		public Task<JsonDocumentMetadata> HeadAsync(string key)
		{
			EnsureIsNotNullOrEmpty(key, "key");
			return ExecuteWithReplication("HEAD", u => DirectHeadAsync(u, key));
		}

		public Task<IAsyncEnumerator<RavenJObject>> StreamQueryAsync(string index, IndexQuery query, Reference<QueryHeaderInformation> queryHeaderInfo)
		{
			return ExecuteWithReplication("GET", operationMetadata => DirectStreamQueryAsync(index, query, queryHeaderInfo, operationMetadata));
		}

		private async Task<IAsyncEnumerator<RavenJObject>> DirectStreamQueryAsync(string index, IndexQuery query, Reference<QueryHeaderInformation> queryHeaderInfo, OperationMetadata operationMetadata)
		{
			EnsureIsNotNullOrEmpty(index, "index");
			string path;
			string method;
			if (query.Query != null && query.Query.Length > MaxQuerySizeForGetRequest)
			{
				path = query.GetIndexQueryUrl(operationMetadata.Url, index, "streams/query", includePageSizeEvenIfNotExplicitlySet: false, includeQuery: false) + "&postQuery=true";
				method = "POST";
			}
			else
			{
				method = "GET";
				path = query.GetIndexQueryUrl(operationMetadata.Url, index, "streams/query", includePageSizeEvenIfNotExplicitlySet: false, includeQuery: true);
			}

			var request = jsonRequestFactory
				.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, path, method, operationMetadata.Credentials, convention)
				.AddOperationHeaders(OperationsHeaders))
				.AddReplicationStatusHeaders(Url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges);

			request.RemoveAuthorizationHeader();
			var token = await GetSingleAuthToken(operationMetadata).ConfigureAwait(false);
			try
			{
				token = await ValidateThatWeCanUseAuthenticateTokens(operationMetadata, token).ConfigureAwait(false);
			}
			catch (Exception e)
			{
				throw new InvalidOperationException(
					"Could not authenticate token for query streaming, if you are using ravendb in IIS make sure you have Anonymous Authentication enabled in the IIS configuration",
					e);
			}
			request.AddOperationHeader("Single-Use-Auth-Token", token);

			HttpResponseMessage response;
			try
			{
				if (method == "POST")
				{
					response = await request.ExecuteRawResponseAsync(query.Query)
                                            .ConfigureAwait(false);
				}
				else
				{
					response = await request.ExecuteRawResponseAsync()
                                            .ConfigureAwait(false);
				}

                await response.AssertNotFailingResponse();
			}
			catch (Exception e)
			{
				if (index.StartsWith("dynamic/", StringComparison.InvariantCultureIgnoreCase) && request.ResponseStatusCode == HttpStatusCode.NotFound)
				{
					throw new InvalidOperationException(
						@"StreamQuery does not support querying dynamic indexes. It is designed to be used with large data-sets and is unlikely to return all data-set after 15 sec of indexing, like Query() does.",
						e);
				}

				throw;
			}

			queryHeaderInfo.Value = new QueryHeaderInformation
			{
				Index = response.Headers.GetFirstValue("Raven-Index"),
				IndexTimestamp = DateTime.ParseExact(response.Headers.GetFirstValue("Raven-Index-Timestamp"), Default.DateTimeFormatsToRead,
																CultureInfo.InvariantCulture, DateTimeStyles.None),
				IndexEtag = Etag.Parse(response.Headers.GetFirstValue("Raven-Index-Etag")),
				ResultEtag = Etag.Parse(response.Headers.GetFirstValue("Raven-Result-Etag")),
				IsStale = bool.Parse(response.Headers.GetFirstValue("Raven-Is-Stale")),
				TotalResults = int.Parse(response.Headers.GetFirstValue("Raven-Total-Results"))
			};

			return new YieldStreamResults(await response.GetResponseStreamWithHttpDecompression().ConfigureAwait(false));
		}

		public class YieldStreamResults : IAsyncEnumerator<RavenJObject>
		{
			private readonly int start;

			private readonly int pageSize;

			private readonly RavenPagingInformation pagingInformation;

			private readonly Stream stream;
			private readonly StreamReader streamReader;
			private readonly JsonTextReaderAsync reader;
			private bool complete;

			private bool wasInitialized;

			public YieldStreamResults(Stream stream, int start = 0, int pageSize = 0, RavenPagingInformation pagingInformation = null)
			{
				this.start = start;
				this.pageSize = pageSize;
				this.pagingInformation = pagingInformation;
				this.stream = stream;
				streamReader = new StreamReader(stream);
				reader = new JsonTextReaderAsync(streamReader);
			}

			private async Task InitAsync()
			{
				if (await reader.ReadAsync().ConfigureAwait(false) == false || reader.TokenType != JsonToken.StartObject)
					throw new InvalidOperationException("Unexpected data at start of stream");

				if (await reader.ReadAsync().ConfigureAwait(false) == false || reader.TokenType != JsonToken.PropertyName || Equals("Results", reader.Value) == false)
					throw new InvalidOperationException("Unexpected data at stream 'Results' property name");

				if (await reader.ReadAsync().ConfigureAwait(false) == false || reader.TokenType != JsonToken.StartArray)
					throw new InvalidOperationException("Unexpected data at 'Results', could not find start results array");
			}

			public void Dispose()
			{
				reader.Close();
				streamReader.Close();
				stream.Close();
			}

			public async Task<bool> MoveNextAsync()
			{
				if (complete)
				{
					// to parallel IEnumerable<T>, subsequent calls to MoveNextAsync after it has returned false should
					// also return false, rather than throwing
					return false;
				}

				if (wasInitialized == false)
				{
					await InitAsync().ConfigureAwait(false);
					wasInitialized = true;
				}

				if (await reader.ReadAsync().ConfigureAwait(false) == false)
					throw new InvalidOperationException("Unexpected end of data");

				if (reader.TokenType == JsonToken.EndArray)
				{
					complete = true;

					await TryReadNextPageStart().ConfigureAwait(false);

					await EnsureValidEndOfResponse();

					return false;
				}

				Current = (RavenJObject)await RavenJToken.ReadFromAsync(reader).ConfigureAwait(false);
				return true;
			}

			private async Task TryReadNextPageStart()
			{
				if (!(await reader.ReadAsync().ConfigureAwait(false)) || reader.TokenType != JsonToken.PropertyName)
					return;

			    switch ((string)reader.Value)
			    {
                    case "NextPageStart":
                        var nextPageStart = await reader.ReadAsInt32().ConfigureAwait(false);
			            if (pagingInformation == null)
			                return;				        
                        if (nextPageStart.HasValue == false)
					        throw new InvalidOperationException("Unexpected end of data");

			            pagingInformation.Fill(start, pageSize, nextPageStart.Value);
			            break;
                    case "Error":
			            var err = await reader.ReadAsString().ConfigureAwait(false);
                        throw new InvalidOperationException("Server error" + Environment.NewLine + err);
                    default:
                        throw new InvalidOperationException("Unexpected property name: " + reader.Value);
			    }
				
			}

			private async Task EnsureValidEndOfResponse()
			{
				if(reader.TokenType != JsonToken.EndObject && await reader.ReadAsync().ConfigureAwait(false) == false)
					throw new InvalidOperationException("Unexpected end of response - missing EndObject token");

				if (reader.TokenType != JsonToken.EndObject)
					throw new InvalidOperationException(string.Format("Unexpected token type at the end of the response: {0}. Error: {1}", reader.TokenType, streamReader.ReadToEnd()));

				var remainingContent = streamReader.ReadToEnd();

				if(string.IsNullOrEmpty(remainingContent) == false)
					throw new InvalidOperationException("Server error: " + remainingContent);
			}

			public RavenJObject Current { get; private set; }
		}

		public async Task<IAsyncEnumerator<RavenJObject>> StreamDocsAsync(
						Etag fromEtag = null, string startsWith = null,
						string matches = null, int start = 0,
						int pageSize = Int32.MaxValue,
						string exclude = null,
						RavenPagingInformation pagingInformation = null,
						string skipAfter = null)
		{
			if (fromEtag != null && startsWith != null)
				throw new InvalidOperationException("Either fromEtag or startsWith must be null, you can't specify both");

			if (fromEtag != null) // etags does not match between servers
				return await DirectStreamDocsAsync(fromEtag, null, matches, start, pageSize, exclude, pagingInformation, new OperationMetadata(url, credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication), skipAfter).ConfigureAwait(false);

			return await ExecuteWithReplication("GET", operationMetadata => DirectStreamDocsAsync(null, startsWith, matches, start, pageSize, exclude, pagingInformation, operationMetadata, skipAfter)).ConfigureAwait(false);
		}

		private async Task<IAsyncEnumerator<RavenJObject>> DirectStreamDocsAsync(Etag fromEtag, string startsWith, string matches, int start, int pageSize, string exclude, RavenPagingInformation pagingInformation, OperationMetadata operationMetadata, string skipAfter)
		{
			if (fromEtag != null && startsWith != null)
				throw new InvalidOperationException("Either fromEtag or startsWith must be null, you can't specify both");

			var sb = new StringBuilder(operationMetadata.Url).Append("/streams/docs?");

			if (fromEtag != null)
			{
				sb.Append("etag=").Append(fromEtag).Append("&");
			}
			else
			{
				if (startsWith != null)
				{
					sb.Append("startsWith=").Append(Uri.EscapeDataString(startsWith)).Append("&");
				}
				if (matches != null)
				{
					sb.Append("matches=").Append(Uri.EscapeDataString(matches)).Append("&");
				}
				if (exclude != null)
				{
					sb.Append("exclude=").Append(Uri.EscapeDataString(exclude)).Append("&");
				}
				if (skipAfter != null)
				{
					sb.Append("skipAfter=").Append(Uri.EscapeDataString(skipAfter)).Append("&");
			}
			}

			var actualStart = start;

			var nextPage = pagingInformation != null && pagingInformation.IsForPreviousPage(start, pageSize);
			if (nextPage)
				actualStart = pagingInformation.NextPageStart;

			if (actualStart != 0)
				sb.Append("start=").Append(actualStart).Append("&");

			if (pageSize != int.MaxValue)
				sb.Append("pageSize=").Append(pageSize).Append("&");

			if (nextPage)
				sb.Append("next-page=true").Append("&");

			var request = jsonRequestFactory
				.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, sb.ToString(), "GET", operationMetadata.Credentials, convention)
				.AddOperationHeaders(OperationsHeaders))
				.AddReplicationStatusHeaders(Url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges);

			request.RemoveAuthorizationHeader();

			var token = await GetSingleAuthToken(operationMetadata).ConfigureAwait(false);
			try
			{
				token = await ValidateThatWeCanUseAuthenticateTokens(operationMetadata, token).ConfigureAwait(false);
			}
			catch (Exception e)
			{
				throw new InvalidOperationException("Could not authenticate token for docs streaming, if you are using ravendb in IIS make sure you have Anonymous Authentication enabled in the IIS configuration", e);
			}

			request.AddOperationHeader("Single-Use-Auth-Token", token);

            var response = await request.ExecuteRawResponseAsync()
                                        .ConfigureAwait(false);

            await response.AssertNotFailingResponse();

			return new YieldStreamResults(await response.GetResponseStreamWithHttpDecompression().ConfigureAwait(false), start, pageSize, pagingInformation);
		}

		public Task DeleteAsync(string key, Etag etag)
		{
			EnsureIsNotNullOrEmpty(key, "key");
			return ExecuteWithReplication("DELETE", operationMetadata => operationMetadata.Url.Doc(key)
			 .ToJsonRequest(this, operationMetadata.Credentials, convention, OperationsHeaders, "DELETE")
			 .ExecuteRequestAsync());
		}

		public string UrlFor(string documentKey)
		{
			return url + "/docs/" + documentKey;
		}

		/// <summary>
		/// Get the low level bulk insert operation
		/// </summary>
		public ILowLevelBulkInsertOperation GetBulkInsertOperation(BulkInsertOptions options, IDatabaseChanges changes)
		{
			return new RemoteBulkInsertOperation(options, this, changes);
		}

		/// <summary>
		/// Do a direct HEAD request against the server for the specified document
		/// </summary>
		private async Task<JsonDocumentMetadata> DirectHeadAsync(OperationMetadata operationMetadata, string key)
		{
			var metadata = new RavenJObject();
			AddTransactionInformation(metadata);
			HttpJsonRequest request = jsonRequestFactory.CreateHttpJsonRequest(
																			   new CreateHttpJsonRequestParams(this, operationMetadata.Url + "/docs/" + key, "HEAD", operationMetadata.Credentials, convention)
																				   .AddOperationHeaders(OperationsHeaders))
														.AddReplicationStatusHeaders(Url, operationMetadata.Url, replicationInformer,
																					 convention.FailoverBehavior,
																					 HandleReplicationStatusChanges);

			try
			{
				await request.ReadResponseJsonAsync().ConfigureAwait(false);
				return SerializationHelper.DeserializeJsonDocumentMetadata(key, request.ResponseHeaders,
																		   request.ResponseStatusCode);
			}
			catch (ErrorResponseException e)
			{
				if (e.StatusCode == HttpStatusCode.NotFound)
					return null;
				if (e.StatusCode == HttpStatusCode.Conflict)
				{
					throw new ConflictException("Conflict detected on " + key +
										", conflict must be resolved before the document will be accessible. Cannot get the conflicts ids because a HEAD request was performed. A GET request will provide more information, and if you have a document conflict listener, will automatically resolve the conflict",
										true)
					{
						Etag = e.Etag
					};
				}
				throw;
			}
		}

		public Task<RavenJToken> ExecuteGetRequest(string requestUrl)
		{
			EnsureIsNotNullOrEmpty(requestUrl, "url");
			return ExecuteWithReplication("GET", operationMetadata =>
			{
				var metadata = new RavenJObject();
				AddTransactionInformation(metadata);
				var request = jsonRequestFactory.CreateHttpJsonRequest(
						new CreateHttpJsonRequestParams(this, operationMetadata.Url + requestUrl, "GET", metadata, operationMetadata.Credentials, convention)
								.AddOperationHeaders(OperationsHeaders));

				return request.ReadResponseJsonAsync();
			});
		}

		public HttpJsonRequest CreateRequest(string requestUrl, string method, bool disableRequestCompression = false)
		{
			var metadata = new RavenJObject();
			AddTransactionInformation(metadata);
			var createHttpJsonRequestParams = new CreateHttpJsonRequestParams(this, url + requestUrl, method, metadata, credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication, convention)
				.AddOperationHeaders(OperationsHeaders);
			createHttpJsonRequestParams.DisableRequestCompression = disableRequestCompression;
			return jsonRequestFactory.CreateHttpJsonRequest(createHttpJsonRequestParams);
		}

		public HttpJsonRequest CreateRequest(OperationMetadata operationMetadata, string requestUrl, string method, bool disableRequestCompression = false)
		{
			var metadata = new RavenJObject();
			AddTransactionInformation(metadata);
			var createHttpJsonRequestParams = new CreateHttpJsonRequestParams(this, (operationMetadata.Url + requestUrl), method, metadata, operationMetadata.Credentials, convention)
				.AddOperationHeaders(OperationsHeaders);
			createHttpJsonRequestParams.DisableRequestCompression = disableRequestCompression;
			return jsonRequestFactory.CreateHttpJsonRequest(createHttpJsonRequestParams);
		}

		public HttpJsonRequest CreateReplicationAwareRequest(string currentServerUrl, string requestUrl, string method, bool disableRequestCompression = false)
		{
			var metadata = new RavenJObject();
			AddTransactionInformation(metadata);

			var createHttpJsonRequestParams = new CreateHttpJsonRequestParams(this, currentServerUrl + requestUrl, method, credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication,
																			  convention).AddOperationHeaders(OperationsHeaders);
			createHttpJsonRequestParams.DisableRequestCompression = disableRequestCompression;

			return jsonRequestFactory.CreateHttpJsonRequest(createHttpJsonRequestParams)
									 .AddReplicationStatusHeaders(url, currentServerUrl, replicationInformer,
																  convention.FailoverBehavior, HandleReplicationStatusChanges);
		}

        [Obsolete("Use RavenFS instead.")]
		public Task UpdateAttachmentMetadataAsync(string key, Etag etag, RavenJObject metadata)
		{
			return ExecuteWithReplication("POST", operationMetadata => DirectUpdateAttachmentMetadata(key, metadata, etag, operationMetadata));
		}

        [Obsolete("Use RavenFS instead.")]
		private async Task DirectUpdateAttachmentMetadata(string key, RavenJObject metadata, Etag etag, OperationMetadata operationMetadata)
		{
			if (etag != null)
			{
                metadata[Constants.MetadataEtagField] = etag.ToString();
			}
			var webRequest = jsonRequestFactory.CreateHttpJsonRequest(
					new CreateHttpJsonRequestParams(this, operationMetadata.Url + "/static/" + key, "POST", metadata, operationMetadata.Credentials, convention))
							.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges);

			ErrorResponseException responseException;
			try
			{
				await webRequest.ExecuteRequestAsync().ConfigureAwait(false);
				return;
			}
			catch (ErrorResponseException e)
			{
				responseException = e;
			}
			if (!HandleException(responseException))
				throw responseException;
		}

        [Obsolete("Use RavenFS instead.")]
		public Task<IAsyncEnumerator<Attachment>> GetAttachmentHeadersStartingWithAsync(string idPrefix, int start, int pageSize)
		{
			return ExecuteWithReplication("GET", operationMetadata => DirectGetAttachmentHeadersStartingWith("GET", idPrefix, start, pageSize, operationMetadata));
		}

        [Obsolete("Use RavenFS instead.")]
		private async Task<IAsyncEnumerator<Attachment>> DirectGetAttachmentHeadersStartingWith(string method, string idPrefix, int start, int pageSize, OperationMetadata operationMetadata)
		{
			var webRequest = jsonRequestFactory
				.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, operationMetadata.Url + "/static/?startsWith=" + idPrefix + "&start=" + start + "&pageSize=" + pageSize, method, operationMetadata.Credentials, convention))
				.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges);

			RavenJToken result = await webRequest.ReadResponseJsonAsync().ConfigureAwait(false);

			List<Attachment> attachments = convention
				.CreateSerializer()
				.Deserialize<Attachment[]>(new RavenJTokenReader(result))
				.Select(x => new Attachment
							 {
								 Etag = x.Etag,
								 Metadata = x.Metadata.WithCaseInsensitivePropertyNames(),
								 Size = x.Size,
								 Key = x.Key,
								 Data = () =>
								 {
									 throw new InvalidOperationException("Cannot get attachment data from an attachment header");
								 }
							 })
				.ToList();

			return new AsyncEnumeratorBridge<Attachment>(attachments.GetEnumerator());

		}

		public Task CommitAsync(string txId)
		{
			return ExecuteWithReplication("POST", operationMetadata => DirectCommit(txId, operationMetadata));
		}

		private Task DirectCommit(string txId, OperationMetadata operationMetadata)
		{
			var httpJsonRequest = jsonRequestFactory.CreateHttpJsonRequest(
					new CreateHttpJsonRequestParams(this, operationMetadata.Url + "/transaction/commit?tx=" + txId, "POST",
							operationMetadata.Credentials, convention)
							.AddOperationHeaders(OperationsHeaders))
					.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior,
							HandleReplicationStatusChanges);

			return httpJsonRequest.ReadResponseJsonAsync();
		}

		public Task RollbackAsync(string txId)
		{
			return ExecuteWithReplication("POST", operationMetadata => DirectRollback(txId, operationMetadata));
		}

		private Task DirectRollback(string txId, OperationMetadata operationMetadata)
		{
			var httpJsonRequest = jsonRequestFactory.CreateHttpJsonRequest(
					new CreateHttpJsonRequestParams(this, operationMetadata.Url + "/transaction/rollback?tx=" + txId, "POST", operationMetadata.Credentials, convention)
							.AddOperationHeaders(OperationsHeaders))
							.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges);

			return httpJsonRequest.ReadResponseJsonAsync();
		}

		public Task PrepareTransactionAsync(string txId, Guid? resourceManagerId = null, byte[] recoveryInformation = null)
		{
			return ExecuteWithReplication("POST", operationMetadata => DirectPrepareTransaction(txId, operationMetadata, resourceManagerId, recoveryInformation));
		}

		private async Task DirectPrepareTransaction(string txId, OperationMetadata operationMetadata, Guid? resourceManagerId, byte[] recoveryInformation)
		{
			var opUrl = operationMetadata.Url + "/transaction/prepare?tx=" + txId;
			if (resourceManagerId != null)
				opUrl += "&resourceManagerId=" + resourceManagerId;

			var httpJsonRequest = jsonRequestFactory.CreateHttpJsonRequest(
					new CreateHttpJsonRequestParams(this, opUrl, "POST", operationMetadata.Credentials, convention)
							.AddOperationHeaders(OperationsHeaders))
							.AddReplicationStatusHeaders(url, operationMetadata.Url, replicationInformer, convention.FailoverBehavior, HandleReplicationStatusChanges);

			if (recoveryInformation != null)
			{
				var ms = new MemoryStream(recoveryInformation);
				await httpJsonRequest.WriteAsync(ms);
			}

			await httpJsonRequest.ReadResponseJsonAsync();
		}

		private void HandleReplicationStatusChanges(NameValueCollection headers, string primaryUrl, string currentUrl)
		{
			if (primaryUrl.Equals(currentUrl, StringComparison.OrdinalIgnoreCase))
				return;

			var forceCheck = headers[Constants.RavenForcePrimaryServerCheck];
			bool shouldForceCheck;
			if (!string.IsNullOrEmpty(forceCheck) && bool.TryParse(forceCheck, out shouldForceCheck))
			{
				replicationInformer.ForceCheck(primaryUrl, shouldForceCheck);
			}
		}

		internal Task ExecuteWithReplication(string method, Func<OperationMetadata, Task> operation)
		{
			// Convert the Func<string, Task> to a Func<string, Task<object>>
			return ExecuteWithReplication(method, u => operation(u).ContinueWith<object>(t =>
			{
				t.AssertNotFailed();
				return null;
			}));
		}

		private volatile bool currentlyExecuting;
		private bool resolvingConflict;
		private bool resolvingConflictRetries;

		internal async Task<T> ExecuteWithReplication<T>(string method, Func<OperationMetadata, Task<T>> operation)
		{
			var currentRequest = Interlocked.Increment(ref requestCount);
			if (currentlyExecuting && convention.AllowMultipuleAsyncOperations == false)
				throw new InvalidOperationException("Only a single concurrent async request is allowed per async client instance.");

			currentlyExecuting = true;
			try
			{
				return await replicationInformer.ExecuteWithReplicationAsync(method, Url, credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication, currentRequest, readStripingBase, operation).ConfigureAwait(false);
			}
			finally
			{
				currentlyExecuting = false;
			}
		}

		private async Task<bool> AssertNonConflictedDocumentAndCheckIfNeedToReload(OperationMetadata operationMetadata, RavenJObject docResult,
																					Func<string, ConflictException> onConflictedQueryResult = null)
		{
			if (docResult == null)
				return (false);
			var metadata = docResult[Constants.Metadata];
			if (metadata == null)
				return (false);

			if (metadata.Value<int>("@Http-Status-Code") == 409)
			{
				var etag = HttpExtensions.EtagHeaderToEtag(metadata.Value<string>("@etag"));
				var e = await TryResolveConflictOrCreateConcurrencyException(operationMetadata, metadata.Value<string>("@id"), docResult, etag).ConfigureAwait(false);
				if (e != null)
					throw e;
				return true;

			}

			if (metadata.Value<bool>(Constants.RavenReplicationConflict) && onConflictedQueryResult != null)
				throw onConflictedQueryResult(metadata.Value<string>("@id"));

			return (false);
		}

		private async Task<ConflictException> TryResolveConflictOrCreateConcurrencyException(OperationMetadata operationMetadata, string key,
																							 RavenJObject conflictsDoc,
																							 Etag etag)
		{
			var ravenJArray = conflictsDoc.Value<RavenJArray>("Conflicts");
			if (ravenJArray == null)
				throw new InvalidOperationException(
					"Could not get conflict ids from conflicted document, are you trying to resolve a conflict when using metadata-only?");

			var conflictIds = ravenJArray.Select(x => x.Value<string>()).ToArray();

			var result = await TryResolveConflictByUsingRegisteredListenersAsync(key, etag, conflictIds, operationMetadata).ConfigureAwait(false);
			if (result)
				return null;

			return
				new ConflictException(
					"Conflict detected on " + key + ", conflict must be resolved before the document will be accessible",
												 true)
				{
					ConflictedVersionIds = conflictIds,
					Etag = etag
				};
		}

		internal async Task<bool> TryResolveConflictByUsingRegisteredListenersAsync(string key, Etag etag, string[] conflictIds, OperationMetadata operationMetadata = null)
		{
			if (operationMetadata == null)
				operationMetadata = new OperationMetadata(Url);

			if (conflictListeners.Length > 0 && resolvingConflict == false)
			{
				resolvingConflict = true;
				try
				{
					var result = await DirectGetAsync(operationMetadata, conflictIds, null, null, null, false).ConfigureAwait(false);
					var results = result.Results.Select(SerializationHelper.ToJsonDocument).ToArray();

					foreach (var conflictListener in conflictListeners)
					{
						JsonDocument resolvedDocument;
						if (conflictListener.TryResolveConflict(key, results, out resolvedDocument))
						{
							await DirectPutAsync(operationMetadata, key, etag, resolvedDocument.DataAsJson, resolvedDocument.Metadata).ConfigureAwait(false);
							return true;
						}
					}

					return false;
				}
				finally
				{
					resolvingConflict = false;
				}
			}

			return false;
		}

		private async Task<T> RetryOperationBecauseOfConflict<T>(OperationMetadata operationMetadata, IEnumerable<RavenJObject> docResults,
																 T currentResult, Func<Task<T>> nextTry, Func<string, ConflictException> onConflictedQueryResult = null)
		{
			bool requiresRetry = false;
			foreach (var docResult in docResults)
			{
				requiresRetry |= await AssertNonConflictedDocumentAndCheckIfNeedToReload(operationMetadata, docResult, onConflictedQueryResult).ConfigureAwait(false);
			}
			if (!requiresRetry)
				return currentResult;

			if (resolvingConflictRetries)
				throw new InvalidOperationException(
					"Encountered another conflict after already resolving a conflict. Conflict resultion cannot recurse.");
			resolvingConflictRetries = true;
			try
			{
				return await nextTry().ConfigureAwait(false);
			}
			finally
			{
				resolvingConflictRetries = false;
			}
		}

		public async Task<RavenJToken> GetOperationStatusAsync(long id)
		{
			var request = jsonRequestFactory
				.CreateHttpJsonRequest(new CreateHttpJsonRequestParams(this, url + "/operation/status?id=" + id, "GET",
																	   credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication, convention)
				.AddOperationHeaders(OperationsHeaders));

			try
			{
				return await request.ReadResponseJsonAsync().ConfigureAwait(false);
			}
			catch (ErrorResponseException e)
			{
				if (e.StatusCode == HttpStatusCode.NotFound)
					return null;
				throw;
			}
		}

		private async Task<string> GetSingleAuthToken(OperationMetadata operationMetadata)
		{
			var tokenRequest = CreateRequest(operationMetadata, "/singleAuthToken", "GET", disableRequestCompression: true);

			var response = await tokenRequest.ReadResponseJsonAsync().ConfigureAwait(false);
			return response.Value<string>("Token");
		}

		private async Task<string> ValidateThatWeCanUseAuthenticateTokens(OperationMetadata operationMetadata, string token)
		{
			var request = CreateRequest(operationMetadata, "/singleAuthToken", "GET", disableRequestCompression: true);

			request.DisableAuthentication();
			request.AddOperationHeader("Single-Use-Auth-Token", token);
			var result = await request.ReadResponseJsonAsync().ConfigureAwait(false);
			return result.Value<string>("Token");
		}

		public IAsyncInfoDatabaseCommands Info
		{
			get { return this; }
		}

		async Task<ReplicationStatistics> IAsyncInfoDatabaseCommands.GetReplicationInfoAsync()
		{
			var json = (RavenJObject)await url.ReplicationInfo()
					.ToJsonRequest(this, credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication, convention)
											   .ReadResponseJsonAsync().ConfigureAwait(false);

			return json.Deserialize<ReplicationStatistics>(convention);
		}

		/// <summary>
		/// Returns a new <see cref="IAsyncDatabaseCommands"/> using the specified credentials
		/// </summary>
		/// <param name="credentialsForSession">The credentials for session.</param>
		public IAsyncDatabaseCommands With(ICredentials credentialsForSession)
		{
			return new AsyncServerClient(url, convention, new OperationCredentials(credentialsThatShouldBeUsedOnlyInOperationsWithoutReplication.ApiKey, credentialsForSession), jsonRequestFactory, sessionId,
										 replicationInformerGetter, databaseName, conflictListeners);
		}
	}
	}
