namespace RocksDbNet;

/// <summary>
/// A snapshot of histogram data gathered by RocksDb statistics.
/// </summary>
/// <remarks>
/// <para>
/// Obtained from <see cref="DbOptions.GetHistogramData"/>. The unit depends
/// entirely on which histogram was asked for: timing histograms are in
/// microseconds, size histograms in bytes, and counting histograms are
/// dimensionless. Nothing here records which, so keep the histogram type
/// alongside the data.
/// </para>
/// <para>
/// A snapshot, not a live view: the values are read once and do not track
/// later activity. All-zero data means either that no samples were recorded
/// or that no statistics object was attached; the two are indistinguishable.
/// </para>
/// </remarks>
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

    /// <summary>The 50th percentile of the recorded samples.</summary>
    public double Median { get; }

    /// <summary>The 95th percentile of the recorded samples.</summary>
    public double P95 { get; }

    /// <summary>The 99th percentile of the recorded samples.</summary>
    public double P99 { get; }

    /// <summary>The arithmetic mean of the recorded samples.</summary>
    public double Average { get; }

    /// <summary>The standard deviation of the recorded samples.</summary>
    public double StdDev { get; }

    /// <summary>The largest single sample recorded.</summary>
    public double Max { get; }

    /// <summary>
    /// How many samples were recorded. Zero means the percentiles and the
    /// average carry no information.
    /// </summary>
    public ulong Count { get; }

    /// <summary>
    /// The total of all recorded samples. Divided by <see cref="Count"/> this
    /// gives the mean, which is also reported directly as
    /// <see cref="Average"/>.
    /// </summary>
    public ulong Sum { get; }

    /// <summary>The smallest single sample recorded.</summary>
    public double Min { get; }
}
