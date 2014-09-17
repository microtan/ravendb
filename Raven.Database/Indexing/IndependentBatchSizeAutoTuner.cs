using System;
using System.Collections.Generic;

using Raven.Database.Prefetching;

namespace Raven.Database.Indexing
{
	public class IndependentBatchSizeAutoTuner : BaseBatchSizeAutoTuner
	{
        public IndependentBatchSizeAutoTuner(WorkContext context, PrefetchingUser user)
            : base(context)
		{
		    this.User = user;
            InstallGauges();
		}

        public PrefetchingUser User { get; set; }

        private void InstallGauges()
        {
            var metricCounters = this.context.MetricsCounters;
            metricCounters.AddGauge(typeof(IndependentBatchSizeAutoTuner), User + ".InitialNumberOfItems", () => InitialNumberOfItems);
            metricCounters.AddGauge(typeof(IndependentBatchSizeAutoTuner), User + ".MaxNumberOfItems", () => MaxNumberOfItems);
            metricCounters.AddGauge(typeof(IndependentBatchSizeAutoTuner), User + ".CurrentNumberOfItems", () => CurrentNumberOfItems);
        }

		protected override int InitialNumberOfItems
		{
			get { return context.Configuration.InitialNumberOfItemsToProcessInSingleBatch; }
		}

		protected override int MaxNumberOfItems
		{
			get { return context.Configuration.MaxNumberOfItemsToProcessInSingleBatch; }
		}

		protected override int CurrentNumberOfItems { get; set; }
		protected override int LastAmountOfItemsToRemember { get; set; }

		private int lastAmount;

		protected override void RecordAmountOfItems(int numberOfItems)
		{
			lastAmount = numberOfItems;
		}

		protected override IEnumerable<int> GetLastAmountOfItems()
		{
			yield return lastAmount;
		}
	}
}