using System.Diagnostics.Metrics;
using BenchmarkDotNet.Attributes;

namespace RocksDbNet.Benchmarks;

/// <summary>
/// What a metrics collection costs: reading every instrument once, which is
/// what a Prometheus scrape or an OpenTelemetry export interval does.
/// </summary>
/// <remarks>
/// <para>
/// Paid on the collector's thread, once per scrape, typically every 15 to 60
/// seconds. Reads and writes on the database do not wait on it.
/// </para>
/// <para>
/// Measured cold. A histogram reading is reused for a quarter of a second, so
/// its three instruments share one, and collections back to back would mostly
/// measure that reuse. Real scrapes are far apart, so each iteration registers
/// a fresh export and times one collection.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class MetricsScrapeBenchmarks
{
    private BenchmarkDb _db = null!;
    private RocksDbMetrics _metrics = null!;
    private MeterListener _listener = null!;
    private long _measurements;

    /// <summary>Everything, or the property gauges only, which need no statistics.</summary>
    [Params(true, false)]
    public bool Statistics { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _db = BenchmarkDb.Create(count: 10_000, valueSize: 64, o =>
        {
            if (Statistics)
            {
                o.EnableStatistics();
            }
        });

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == RocksDbMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>((_, _, _, _) => _measurements++);
        _listener.SetMeasurementEventCallback<double>((_, _, _, _) => _measurements++);
        _listener.Start();
    }

    [IterationSetup]
    public void Register() => _metrics = RocksDbMetrics.Register(_db.Db);

    [IterationCleanup]
    public void Unregister() => _metrics.Dispose();

    [GlobalCleanup]
    public void Cleanup()
    {
        _listener.Dispose();
        _db.Dispose();
    }

    [Benchmark(Description = "One collection of every instrument")]
    public long Collect()
    {
        _measurements = 0;
        _listener.RecordObservableInstruments();
        return _measurements;
    }
}

/// <summary>
/// What RocksDb's statistics cost on the hot path, at each level, against none.
/// </summary>
/// <remarks>
/// This is the overhead that matters when deciding whether to enable them. It
/// is paid on every read and write whether or not anything exports the
/// numbers; the export itself only reads what is already counted.
/// </remarks>
[MemoryDiagnoser]
public class StatisticsOverheadBenchmarks
{
    private BenchmarkDb _db = null!;
    private byte[][] _keys = null!;
    private readonly byte[] _value = new byte[64];

    /// <summary>
    /// <see cref="StatsLevel.DisableAll"/> stands for statistics never enabled,
    /// the default, rather than enabled at that level: the baseline.
    /// </summary>
    [Params(StatsLevel.DisableAll, StatsLevel.ExceptHistogramOrTimers, StatsLevel.ExceptDetailedTimers, StatsLevel.All)]
    public StatsLevel Level { get; set; }

    [Params(10_000)]
    public int Operations { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _db = BenchmarkDb.Create(count: Operations, valueSize: 64, o =>
        {
            if (Level != StatsLevel.DisableAll)
            {
                o.EnableStatistics();
                o.StatisticsLevel = Level;
            }
        });

        _keys = _db.Keys;
    }

    [GlobalCleanup]
    public void Cleanup() => _db.Dispose();

    [Benchmark(Description = "Puts")]
    public void Puts()
    {
        foreach (byte[] key in _keys)
        {
            _db.Db.Put(key, _value);
        }
    }

    [Benchmark(Description = "Gets")]
    public long Gets()
    {
        long bytes = 0;
        Span<byte> buffer = stackalloc byte[128];

        foreach (byte[] key in _keys)
        {
            if (_db.Db.TryGetInto(key, buffer, out int length))
            {
                bytes += length;
            }
        }

        return bytes;
    }
}
