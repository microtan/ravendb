﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Logging;
using Raven.Abstractions.Util;
using Raven.Database.Server;
using Raven.Database.Util;

namespace Raven.Database.Indexing
{
	public class DefaultBackgroundTaskExecuter : IBackgroundTaskExecuter
	{
		private static readonly ILog logger = LogManager.GetCurrentClassLogger();

		public IList<TResult> Apply<T, TResult>(WorkContext context, IEnumerable<T> source, Func<T, TResult> func)
			where TResult : class
		{
			if (context.Configuration.MaxNumberOfParallelProcessingTasks == 1)
			{
				return source.Select(func).ToList();
			}

			return source.AsParallel()
				.Select(func)
				.Where(x => x != null)
				.ToList();
		}

		private readonly AtomicDictionary<Tuple<Timer, ConcurrentSet<IRepeatedAction>>> timers =
			new AtomicDictionary<Tuple<Timer, ConcurrentSet<IRepeatedAction>>>();

		public void Repeat(IRepeatedAction action)
		{
			var tuple = timers.GetOrAdd(action.RepeatDuration.ToString(),
												  span =>
												  {
													  var repeatedActions = new ConcurrentSet<IRepeatedAction>
			                                      	{
			                                      		action
			                                      	};
													  var timer = new Timer(ExecuteTimer, action.RepeatDuration,
																			action.RepeatDuration,
																			action.RepeatDuration);
													  return Tuple.Create(timer, repeatedActions);
												  });
			tuple.Item2.TryAdd(action);
		}

		private void ExecuteTimer(object state)
		{
			var span = state.ToString();
			Tuple<Timer, ConcurrentSet<IRepeatedAction>> tuple;
			if (timers.TryGetValue(span, out tuple) == false)
				return;

			foreach (var repeatedAction in tuple.Item2)
			{
				if (repeatedAction.IsValid == false)
					tuple.Item2.TryRemove(repeatedAction);

				try
				{
					repeatedAction.Execute();
				}
				catch (Exception e)
				{
					logger.ErrorException("Could not execute repeated task", e);
				}
			}

			if (tuple.Item2.Count != 0)
				return;

			if (timers.TryRemove(span, out tuple) == false)
				return;

			tuple.Item1.Dispose();
		}

		/// <summary>
		/// Note that here we assume that  source may be very large (number of documents)
		/// </summary>
		public void ExecuteAllBuffered<T>(WorkContext context, IList<T> source, Action<IEnumerator<T>> action)
		{
			var maxNumberOfParallelIndexTasks = context.Configuration.MaxNumberOfParallelProcessingTasks;
			var size = Math.Max(source.Count / maxNumberOfParallelIndexTasks, 1024);
			if (maxNumberOfParallelIndexTasks == 1 || source.Count <= size)
			{
				using (var e = source.GetEnumerator())
					action(e);
				return;
			}
			int remaining = source.Count;
			int iteration = 0;
			var parts = new List<IEnumerator<T>>();
			while (remaining > 0)
			{
				parts.Add(Yield(source, iteration * size, size));
				iteration++;
				remaining -= size;
			}

			ExecuteAllInterleaved(context, parts, action);
		}

		private IEnumerator<T> Yield<T>(IList<T> source, int start, int end)
		{
			while (start < source.Count && end > 0)
			{
				end--;
				yield return source[start];
				start++;
			}
		}

		/// <summary>
		/// Note that we assume that source is a relatively small number, expected to be 
		/// the number of indexes, not the number of documents.
		/// </summary>
		public void ExecuteAll<T>(
			WorkContext context,
			IList<T> source, Action<T, long> action)
		{
			if (context.Configuration.MaxNumberOfParallelProcessingTasks == 1)
			{
				long i = 0;
				foreach (var item in source)
				{
					action(item, i++);
				}
				return;
			}
			context.CancellationToken.ThrowIfCancellationRequested();
			var partitioneds = Partition(source, context.Configuration.MaxNumberOfParallelProcessingTasks).ToList();
			int start = 0;
			foreach (var partitioned in partitioneds)
			{
				context.CancellationToken.ThrowIfCancellationRequested();
				var currentStart = start;
				Parallel.ForEach(partitioned, new ParallelOptions
				{
					TaskScheduler = context.TaskScheduler,
					MaxDegreeOfParallelism = context.Configuration.MaxNumberOfParallelProcessingTasks
				}, (item, _, index) =>
				{
					using (LogContext.WithDatabase(context.DatabaseName))
					{
						action(item, currentStart + index);
					}
				});
				start += partitioned.Count;
			}
		}

		static IEnumerable<IList<T>> Partition<T>(IList<T> source, int size)
		{
			if (size <= 0)
				throw new ArgumentException("Size cannot be 0");

			for (int i = 0; i < source.Count; i += size)
			{
				yield return source.Skip(i).Take(size).ToList();
			}
		}

		public void ExecuteAllInterleaved<T>(WorkContext context, IList<T> result, Action<T> action)
		{
			if (result.Count == 0)
				return;
			if (result.Count == 1)
			{
				action(result[0]);
				return;
			}

			using (LogContext.WithDatabase(context.DatabaseName))
			using (var semaphoreSlim = new SemaphoreSlim(context.Configuration.MaxNumberOfParallelProcessingTasks))
			{
				var tasks = new Task[result.Count];
				for (int i = 0; i < result.Count; i++)
				{
					var index = result[i];
					var indexToWorkOn = index;

					var task = new Task(() => action(indexToWorkOn));
					tasks[i] = task.ContinueWith(done =>
					{
						semaphoreSlim.Release();
						return done;
					}).Unwrap();

					semaphoreSlim.Wait();

					task.Start(context.Database.BackgroundTaskScheduler);
				}

				Task.WaitAll(tasks);
			}
		}
	}
}