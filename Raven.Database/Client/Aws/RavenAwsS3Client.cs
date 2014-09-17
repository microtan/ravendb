﻿// -----------------------------------------------------------------------
//  <copyright file="RavenAwsClient.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

using Raven.Abstractions;
using Raven.Abstractions.Connection;
using Raven.Client.Extensions;

namespace Raven.Database.Client.Aws
{
	public class RavenAwsS3Client : RavenAwsClient
	{
		public RavenAwsS3Client(string awsAccessKey, string awsSecretKey, string awsRegionEndpoint)
			: base(awsAccessKey, awsSecretKey, awsRegionEndpoint)
		{
		}

		public void PutObject(string bucketName, string key, Stream stream, Dictionary<string, string> metadata, int timeoutInSeconds)
		{
			var url = GetUrl(bucketName) + "/" + key;

			var now = SystemTime.UtcNow;

			var payloadHash = RavenAwsHelper.CalculatePayloadHash(stream);

			var content = new StreamContent(stream)
						  {
							  Headers =
				              {
								  { "x-amz-date", RavenAwsHelper.ConvertToString(now) },
								  { "x-amz-content-sha256", payloadHash }
				              }
						  };

			foreach (var metadataKey in metadata.Keys)
				content.Headers.Add("x-amz-meta-" + metadataKey.ToLower(), metadata[metadataKey]);

			var headers = ConvertToHeaders(bucketName, content.Headers);

			var client = GetClient(TimeSpan.FromSeconds(timeoutInSeconds));
			var authorizationHeaderValue = CalculateAuthorizationHeaderValue("PUT", url, now, headers);
			client.DefaultRequestHeaders.Authorization = authorizationHeaderValue;

			var response = client.PutAsync(url, content).ResultUnwrap();
			if (response.IsSuccessStatusCode)
				return;

			throw ErrorResponseException.FromResponseMessage(response);
		}

		public Blob GetObject(string bucketName, string key)
		{
			var url = GetUrl(bucketName) + "/" + key;

			var now = SystemTime.UtcNow;

			var payloadHash = RavenAwsHelper.CalculatePayloadHash(null);

			var requestMessage = new HttpRequestMessage(HttpMethod.Get, url)
								 {
									 Headers =
				                     {
										 { "x-amz-date", RavenAwsHelper.ConvertToString(now) },
										 { "x-amz-content-sha256", payloadHash }
				                     }
								 };

			var headers = ConvertToHeaders(bucketName, requestMessage.Headers);

			var client = GetClient();
			client.DefaultRequestHeaders.Authorization = CalculateAuthorizationHeaderValue("GET", url, now, headers);

			var response = client.SendAsync(requestMessage).ResultUnwrap();
			if (response.StatusCode == HttpStatusCode.NotFound)
				return null;

			if (response.IsSuccessStatusCode == false)
				throw ErrorResponseException.FromResponseMessage(response);

			var data = response.Content.ReadAsStreamAsync().ResultUnwrap();
			var metadataHeaders = response.Headers.ToDictionary(x => x.Key, x => x.Value.FirstOrDefault());

			return new Blob(data, metadataHeaders);
		}

		public override string ServiceName
		{
			get
			{
				return "s3";
			}
		}

		public override string GetHost(string bucketName)
		{
			if (AwsRegion == "us-east-1")
				return string.Format("{0}.s3.amazonaws.com", bucketName);

			return string.Format("{0}.s3-{1}.amazonaws.com", bucketName, AwsRegion);
		}
	}
}