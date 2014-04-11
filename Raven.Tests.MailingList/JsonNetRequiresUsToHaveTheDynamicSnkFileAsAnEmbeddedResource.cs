﻿// -----------------------------------------------------------------------
//  <copyright file="JsonNetRequiresUsToHaveTheDynamicSnkFileAsAnEmbeddedResource.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.ComponentModel.DataAnnotations;

using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.MailingList
{
	public class JsonNetRequiresUsToHaveTheDynamicSnkFileAsAnEmbeddedResource : RavenTest
	{
		[Fact]
		public void MissingManifestResourceException()
		{
			using (var store = NewDocumentStore())
			using (var session = store.OpenSession())
			{
				// create and store a new page model
				var pageModel = Activator.CreateInstance(typeof (PageModel)) as dynamic;
				session.Store(pageModel);
				session.SaveChanges();
			}
		}

		public class PageModel
		{
			public string Id { get; set; }
			public virtual IPageMetadata Metadata { get; private set; }

			public PageModel()
			{
				Metadata = new PageMetadata();
			}
		}

		public interface IPageMetadata
		{
		}

		[MetadataType(typeof (PageMetadataMetadata))] // Remove this attribute seems to remove the exception
		public class PageMetadata : IPageMetadata
		{
		}

		public class PageMetadataMetadata
		{
		}
	}
}