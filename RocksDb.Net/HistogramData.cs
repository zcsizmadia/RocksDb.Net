namespace RocksDbNet;

/// <summary>
/// A snapshot of histogram data gathered by RocksDB statistics.
/// </summary>
public sealed class HistogramData
{
    internal HistogramData(double median, double p95, double p99, double average, double stdDev, double max, ulong count, ulong sum, double min)
    {
        Median = median;
        P95 = p95;
        P99 = p99;
        Average = average;
        StdDev = stdDev;
        Max = max;
        Count = count;
        Sum = sum;
        Min = min;
    }

    public double Median { get; }
    public double P95 { get; }
    public double P99 { get; }
    public double Average { get; }
    public double StdDev { get; }
    public double Max { get; }
    public ulong Count { get; }
    public ulong Sum { get; }
    public double Min { get; }
}
