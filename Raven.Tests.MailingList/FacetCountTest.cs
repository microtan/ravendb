using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Abstractions.Data;
using Raven.Abstractions.Indexing;
using Raven.Client;
using Raven.Client.Embedded;
using Raven.Client.Indexes;
using Raven.Tests.Common;
using Xunit;

namespace Raven.Tests.MailingList
{
	public class FacetCountTest : RavenTest
	{
		public class WodsProjection
		{
			public string Id { get; set; }
			public string Name { get; set; }
			public string Description { get; set; }
			public string WodType { get; set; }
			public string BenchmarkType { get; set; }
			public double? Score { get; set; }
			public List<string> ExerciseList { get; set; }
		}

		public class WodBase
		{
			public WodBase()
			{
				ExerciseList = new List<string>();
			}

			public string Id { get; set; }
			public WodType WodType { get; set; }
			public BenchmarkType BenchmarkType { get; set; }
			public string Name { get; set; }
			public string Description { get; set; }
			public List<string> ExerciseList { get; set; }

		}

		public enum BenchmarkType
		{
			Heroes = 1,
			Girls = 2,
			Miscellaneous = 3,
			NotBenchMark = 0
		}

		public enum WodType
		{
			AmrapWod,
			TimeWod,
			RunningWod,
			MaxWod,
			MinuteWod,
			TabataWod,
			NotForTimeWod,
			RestDay
		}

		public class Wod_Search : AbstractIndexCreationTask<WodBase>
		{

			public Wod_Search()
			{
				Map = wods => from wod in wods
							  select new
							  {
								  wod.Name,
								  WodType = wod.WodType.ToString(),
								  BenchmarkType = wod.BenchmarkType.ToString(),
								  wod.ExerciseList
							  };

				Index(m => m.ExerciseList, FieldIndexing.Default);
				Index(m => m.WodType, FieldIndexing.Analyzed);
				Index(m => m.BenchmarkType, FieldIndexing.Analyzed);
			}
		}

		public class Wod_SearchTransformer : AbstractTransformerCreationTask<WodBase>
		{

			public Wod_SearchTransformer()
			{
				TransformResults = wods =>
								   from wod in wods
								   select new
								   {
									   wod.Name,
									   wod.Id,
									   wod.WodType,
									   wod.BenchmarkType,
									   wod.ExerciseList,
									   Score = MetadataFor(wod).Value<double?>("Temp-Index-Score")
								   };
			}
		}

		[Fact]
		public void TestFacetsCount()
		{
			using (var store = NewDocumentStore())
			{
				InsertData(store);

				// Create index
				new Wod_Search().Execute(store);
				var wodSearchTransformer = new Wod_SearchTransformer();
				wodSearchTransformer.Execute(store);
				WaitForIndexing(store);
				WaitForIndexing(store);

				for (int i = 1; i <= 5; i++)
				{
					using (var session = store.OpenSession())
					{
						RavenQueryStatistics stats;
						var query = session.Advanced.DocumentQuery<WodsProjection, Wod_Search>()
											.SetResultTransformer(wodSearchTransformer.TransformerName)
										   .WaitForNonStaleResults()
										   .Statistics(out stats)
										   .SelectFields<WodsProjection>();

						query.AndAlso().WhereEquals("ExerciseList", "Pull-ups");

						var wods = query.ToList();

						var facets = session.Advanced.DocumentStore.DatabaseCommands.GetFacets("Wod/Search", new IndexQuery { Query = query.ToString() }, "Facets/WodFacets");

						var pullupsCount = facets.Results["ExerciseList"].Values.First(o => o.Range == "pull-ups").Hits;

						try
						{
							Assert.Equal(11, wods.Count);
							Assert.Equal(11, pullupsCount);
						}
						catch (Exception)
						{
							Console.WriteLine("Failed on iteration #" + i);
							throw;
						}
					}
				}
			}
		}

		private void InsertData(EmbeddableDocumentStore store)
		{
			using (var session = store.OpenSession())
			{
				// Create Facet for wod
				session.Store(new FacetSetup
				{
					Id = "Facets/WodFacets",
					Facets = new List<Facet>
					{
						new Facet<WodsProjection> {Name = o => o.BenchmarkType},
						new Facet<WodsProjection> {Name = o => o.WodType},
						new Facet<WodsProjection> {Name = o => o.ExerciseList}
					}
				});

				// Create Wod's

				#region ANGIE

				// Wodinfo
				var angie = new WodBase
				{
					Name = "Angie",
					Description = "Complete all reps of each exercise before moving to the next.",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				angie.ExerciseList.Add("Pull-ups");
				angie.ExerciseList.Add("Push-ups");
				angie.ExerciseList.Add("Sit-ups");
				angie.ExerciseList.Add("Air Squat");

				// Save wod
				session.Store(angie, "WodBases/1");

				#endregion

				#region BARBARA

				// Wodinfo
				var barbara = new WodBase
				{
					Name = "Barbara",
					Description = "Time each round. Rest precisely three minutes between each round.",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				barbara.ExerciseList.Add("Pull-ups");
				barbara.ExerciseList.Add("Push-ups");
				barbara.ExerciseList.Add("Sit-ups");
				barbara.ExerciseList.Add("Air Squat");
				barbara.ExerciseList.Add("RestPeriod");

				// Save wod
				session.Store(barbara, "WodBases/2");

				#endregion

				#region CHELSEA

				// Wodinfo
				var chelsea = new WodBase
				{
					Name = "Chelsea",
					Description = "Set up before a clock, and every minute on the minute perform 5 pull-ups, " +
								  "10 push-ups, and 15 squats. Can you continue for thirty minutes? Twenty minutes? How about 10? " +
								  "Post results to comments. If you fall behind the clock keep going for thirty minutes and see how many rounds you can complete. " +
								  "If you've finished the workout before this time add +1 to each exercise, i.e., 6 pull-ups, 11 push-ups, and 16 squats each minute, " +
								  "and see if you can go the full thirty minutes.",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.MinuteWod,
				};

				// Add Round and Exercises
				chelsea.ExerciseList.Add("Pull-ups");
				chelsea.ExerciseList.Add("Push-ups");
				chelsea.ExerciseList.Add("Air Squat");

				// Save wod
				session.Store(chelsea, "WodBases/3");

				#endregion

				#region CINDY

				// Wodinfo
				var cindy = new WodBase
				{
					Name = "Cindy",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.AmrapWod,
				};

				// Add Round and Exercises
				cindy.ExerciseList.Add("Pull-ups");
				cindy.ExerciseList.Add("Push-ups");
				cindy.ExerciseList.Add("Air Squat");

				// Save wod
				session.Store(cindy, "WodBases/4");

				#endregion

				#region DIANE

				// Wodinfo
				var diane = new WodBase
				{
					Name = "Diane",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				diane.ExerciseList.Add("Deadlift");
				diane.ExerciseList.Add("Handstand Push-ups");

				// Save wod
				session.Store(diane, "WodBases/5");

				#endregion

				#region ELISABETH

				// Wodinfo
				var elisabeth = new WodBase
				{
					Name = "Elisabeth",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				elisabeth.ExerciseList.Add("Clean");
				elisabeth.ExerciseList.Add("Ring Dips");

				// Save wod
				session.Store(elisabeth, "WodBases/6");

				#endregion

				#region FRAN

				// Wodinfo
				var fran = new WodBase
				{
					Name = "Fran",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				fran.ExerciseList.Add("Thrusters");
				fran.ExerciseList.Add("Pull-ups");

				// Save wod
				session.Store(fran, "WodBases/7");

				#endregion

				#region GRACE

				// Wodinfo
				var grace = new WodBase
				{
					Name = "Grace",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				grace.ExerciseList.Add("Clean & Jerk");

				// Save wod
				session.Store(grace, "WodBases/8");

				#endregion

				#region HELEN

				// Wodinfo
				var helen = new WodBase
				{
					Name = "Helen",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				helen.ExerciseList.Add("Run 400 m");
				helen.ExerciseList.Add("Kettlebell Swings");
				helen.ExerciseList.Add("Pull-ups");

				// Save wod
				session.Store(helen, "WodBases/9");

				#endregion

				#region ISABEL

				// Wodinfo
				var isabel = new WodBase
				{
					Name = "Isabel",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				isabel.ExerciseList.Add("Snatch");

				// Save wod
				session.Store(isabel, "WodBases/10");

				#endregion

				#region JACKIE

				// Wodinfo
				var jackie = new WodBase
				{
					Name = "Jackie",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				jackie.ExerciseList.Add("Row 1000 m");
				jackie.ExerciseList.Add("Thrusters");
				jackie.ExerciseList.Add("Pull-ups");

				// Save wod
				session.Store(jackie, "WodBases/11");

				#endregion

				#region KAREN

				// Wodinfo
				var karen = new WodBase
				{
					Name = "Karen",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				karen.ExerciseList.Add("Wall-ball Shots");

				// Save wod
				session.Store(karen, "WodBases/12");

				#endregion

				#region LINDA

				// Wodinfo
				var linda = new WodBase
				{
					Name = "Linda",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				linda.ExerciseList.Add("Deadlift");
				linda.ExerciseList.Add("Bench Press");
				linda.ExerciseList.Add("Clean");

				// Save wod
				session.Store(linda, "WodBases/13");

				#endregion

				#region MARY

				// Wodinfo
				var mary = new WodBase
				{
					Name = "Mary",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.AmrapWod,
				};

				// Add Round and Exercises
				mary.ExerciseList.Add("Handstand Push-ups");
				mary.ExerciseList.Add("Pistols");
				mary.ExerciseList.Add("Pull-ups");

				// Save wod
				session.Store(mary, "WodBases/14");

				#endregion

				#region NANCY

				// Wodinfo
				var nancy = new WodBase
				{
					Name = "Nancy",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				nancy.ExerciseList.Add("Run 400 m");
				nancy.ExerciseList.Add("Overhead Squat");

				// Save wod
				session.Store(nancy, "WodBases/15");

				#endregion

				#region AMANDA

				// Wodinfo
				var amanda = new WodBase
				{
					Name = "Amanda",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				amanda.ExerciseList.Add("Muscle-ups");
				amanda.ExerciseList.Add("Snatch");

				// Save wod
				session.Store(amanda, "WodBases/16");

				#endregion

				#region ANNIE

				// Wodinfo
				var annie = new WodBase
				{
					Name = "Annie",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				annie.ExerciseList.Add("Double-unders");
				annie.ExerciseList.Add("Sit-ups");

				// Save wod
				session.Store(annie, "WodBases/17");

				#endregion

				#region EVA

				// Wodinfo
				var eva = new WodBase
				{
					Name = "Eva",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				eva.ExerciseList.Add("Run 800 m");
				eva.ExerciseList.Add("Kettlebell Swings");
				eva.ExerciseList.Add("Pull-ups");
				// Save wod
				session.Store(eva, "WodBases/18");

				#endregion

				#region KELLY

				// Wodinfo
				var kelly = new WodBase
				{
					Name = "Kelly",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.TimeWod
				};

				// Add Round and Exercises
				kelly.ExerciseList.Add("Run 400 m");
				kelly.ExerciseList.Add("Box Jump");
				kelly.ExerciseList.Add("Wall-ball Shots");

				// Save wod
				session.Store(kelly, "WodBases/19");

				#endregion

				#region LYNNE

				// Wodinfo
				var lynne = new WodBase
				{
					Name = "Lynne",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.MaxWod
				};

				// Add Round and Exercises
				lynne.ExerciseList.Add("Bench Press");
				lynne.ExerciseList.Add("Pull-ups");
				// Save wod
				session.Store(lynne, "WodBases/20");

				#endregion

				#region NICOLE

				// Wodinfo
				var nicole = new WodBase
				{
					Name = "Nicole",
					Description = "As many rounds as possible in 20 minutes, Note number of pull-ups completed for each round.",
					BenchmarkType = BenchmarkType.Girls,
					WodType = WodType.MaxWod,
				};

				// Add Round and Exercises
				nicole.ExerciseList.Add("Run 400 m");
				nicole.ExerciseList.Add("Pull-ups");
				// Save wod
				session.Store(nicole, "WodBases/21");

				#endregion

				session.SaveChanges();
			}
		}
	}
}