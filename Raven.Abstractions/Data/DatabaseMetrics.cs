using System;
using System.Collections.Generic;

namespace Raven.Abstractions.Data
{
    public interface IMetricsData
    {
    }
    public class DatabaseMetrics
    {
        public double DocsWritesPerSecond { get; set; }
        public double IndexedPerSecond { get; set; }
        public double ReducedPerSecond { get; set; }
        public double RequestsPerSecond { get; set; }
        public MeterData Requests { get; set; }
        public HistogramData RequestsDuration { get; set; }
        public HistogramData StaleIndexMaps { get; set; }
        public HistogramData StaleIndexReduces { get; set; }
        public Dictionary<string, Dictionary<string, string>> Gauges { get; set; }
        public Dictionary<string, MeterData> ReplicationBatchSizeMeter { get; set; }
        public Dictionary<string, MeterData> ReplicationDurationMeter { get; set; }
        public Dictionary<string, HistogramData> ReplicationBatchSizeHistogram { get; set; }
        public Dictionary<string, HistogramData> ReplicationDurationHistogram { get; set; }
    }

    public class HistogramData : IMetricsData
    {
        public HistogramData()
        {
            Percentiles = new Dictionary<string, double>();
        }

        public long Counter { get; set; }
        public double Max { get; set; }
        public double Min { get; set; }
        public double Mean { get; set; }
        public double Stdev { get; set; }
        public Dictionary<string, double> Percentiles { get; set; }
        public MetricType Type = MetricType.Historgram;
    }

    public class MeterData : IMetricsData
    {
        public long Count { get; set; }
        public double MeanRate { get; set; }
        public double OneMinuteRate { get; set; }
        public double FiveMinuteRate { get; set; }
        public double FifteenMinuteRate { get; set; }
        public MetricType Type = MetricType.Meter;
    }

    public enum MetricType
        {
            Meter=1,
            Historgram=2
        }
    
}
