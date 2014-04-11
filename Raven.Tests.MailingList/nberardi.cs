﻿using System.Linq;
using Raven.Abstractions.Indexing;
using Raven.Client.Embedded;
using Raven.Client.Indexes;
using Raven.Client.Linq.Indexing;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.MailingList
{
	public class Nberardi : RavenTest
	{
		[Fact]
		public void Spatial_Search_Should_Integrate_Distance_As_A_Boost_Factor()
		{
			using (var store = new EmbeddableDocumentStore { RunInMemory = true }.Initialize())
			{
				store.ExecuteIndex(new SpatialIndex());

				using (var session = store.OpenSession())
				{
					session.Store(new SpatialEntity(45.70955, -73.569131) // 22.23 Kb
					{
						Id = "se/1",
					});

					session.Store(new SpatialEntity(45.50955, -73.569131) // 0 Km
					{
						Id = "se/2",
					});

					session.Store(new SpatialEntity(45.60955, -73.569131) // 11.11 KM 
					{
						Id = "se/3",
					});

					session.SaveChanges();
				}

				WaitForIndexing(store);

				using (var session = store.OpenSession())
				{
                    var results = session.Advanced.DocumentQuery<SpatialEntity>("SpatialIndex")
						.WithinRadiusOf(500, 45.50955, -73.569133)
						.ToList();

					Assert.Equal(results[0].Id, "se/2");
					Assert.Equal(results[1].Id, "se/3");
					Assert.Equal(results[2].Id, "se/1");
				}

			}
		}

		public class SpatialIndex : AbstractIndexCreationTask<SpatialEntity>
		{
			public SpatialIndex()
			{
				Map =
					entities =>
					from e in entities
					select new
					{
						_ = SpatialGenerate(e.Latitude, e.Longitude)
					};
			}
		}

		public class SpatialEntity
		{
			public SpatialEntity() { }

			public SpatialEntity(double latitude, double longitude)
			{
				Latitude = latitude;
				Longitude = longitude;
			}

			public string Id { get; set; }
			public double Latitude { get; set; }
			public double Longitude { get; set; }
		}
	}
}