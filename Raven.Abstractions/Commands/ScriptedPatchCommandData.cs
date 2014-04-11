//-----------------------------------------------------------------------
// <copyright file="PatchCommandData.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Linq;
using Raven.Abstractions.Data;
using Raven.Json.Linq;
using Raven.Abstractions.Extensions;

namespace Raven.Abstractions.Commands
{
	///<summary>
	/// A single batch operation for a document EVAL (using a Javascript)
	///</summary>
	public class ScriptedPatchCommandData : ICommandData
	{
		/// <summary>
		/// Gets or sets the ScriptedPatchRequest (using JavaScript) that is used to patch the document
		/// </summary>
		/// <value>The Script.</value>
		public ScriptedPatchRequest Patch 
		{ 
			get; set; 
		}

		/// <summary>
		/// Gets or sets the ScriptedPatchRequest (using JavaScript) that is used to patch a default document if the document is missing
		/// </summary>
		/// <value>The Script.</value>
		public ScriptedPatchRequest PatchIfMissing
		{
			get;
			set;
		}

		/// <summary>
		/// Gets the key.
		/// </summary>
		/// <value>The key.</value>
		public string Key
		{
			get; set;
		}

		/// <summary>
		/// Gets the method.
		/// </summary>
		/// <value>The method.</value>
		public string Method
		{
			get { return "EVAL"; }
		}

		/// <summary>
		/// Gets or sets the etag.
		/// </summary>
		/// <value>The etag.</value>
		public Etag Etag
		{
			get; set;
		}

		public TransactionInformation TransactionInformation
		{
			get; set;
		}

		public RavenJObject Metadata
		{
			get; set;
		}

		public bool DebugMode
		{
			get; set;
		}

		/// <summary>
		/// Gets the Additional Data.
		/// </summary>
		/// <value>The Additional Data.</value>
		public RavenJObject AdditionalData { get; set; }

		/// <summary>
		/// Translate this instance to a Json object.
		/// </summary>
		public RavenJObject ToJson()
		{
			var ret = new RavenJObject
					{
						{"Key", Key},
						{"Method", Method},
						{"Patch", new RavenJObject
						{
							{ "Script", Patch.Script },
							{ "Values", RavenJObject.FromObject(Patch.Values)}
						}},
						{"DebugMode", DebugMode},
						{"AdditionalData", AdditionalData}
					};
			if (Etag != null)
				ret.Add("Etag", Etag.ToString());
			if (PatchIfMissing != null)
			{
				ret.Add("PatchIfMissing", new RavenJObject
						{
							{ "Script", PatchIfMissing.Script },
							{ "Values", RavenJObject.FromObject(PatchIfMissing.Values)}
						});
			}
			return ret;
		}
	}
}
