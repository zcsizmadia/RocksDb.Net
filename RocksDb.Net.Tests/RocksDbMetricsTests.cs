using System.Diagnostics.Metrics;

namespace RocksDbNet.Tests;

/// <summary>
/// The statistics export, observed the way a collector observes it. See issue #192.
/// </summary>
public class RocksDbMetricsTests
{
    /// <summary>Collects every measurement a meter called <see cref="RocksDbMetrics.MeterName"/> reports.</summary>
    private sealed class Collector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly object _gate = new();
        private readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags, Meter Meter)> _measurements = [];

        public Collector(Meter? only = null)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == RocksDbMetrics.MeterName && (only is null || instrument.Meter == only))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i, v, t));
            _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i, v, t));
            _listener.Start();
        }

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            lock (_gate)
            {
                _measurements.Add((instrument.Name, value, tags.ToArray(), instrument.Meter));
            }
        }

        public IReadOnlyList<(string Name, double Value, KeyValuePair<string, object?>[] Tags, Meter Meter)> Collect()
        {
            lock (_gate)
            {
                _measurements.Clear();
            }

            _listener.RecordObservableInstruments();

            lock (_gate)
            {
                return [.. _measurements];
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private static TempDb StatisticsDb() => new(o => o.EnableStatistics());

    [Fact]
    public void Tickers_ReportUnderRocksDbsNames_AndCount()
    {
        using TempDb db = StatisticsDb();
        using var metrics = RocksDbMetrics.Register(db.Db, new() { DatabaseName = "tickers-test" });
        using var collector = new Collector();

        for (int i = 0; i < 50; i++)
        {
            db.Db.Put($"k{i}", "v");
        }

        var measurements = collector.Collect()
            .Where(m => m.Meter.Tags?.Any(t => t.Key == "db.name" && (string?)t.Value == "tickers-test") == true)
            .ToList();

        double keysWritten = measurements.Single(m => m.Name == "rocksdb.number.keys.written").Value;
        Assert.Equal(50, keysWritten);

        // Every ticker and histogram has an instrument, under RocksDb's name.
        Assert.Contains(measurements, m => m.Name == "rocksdb.block.cache.miss");
        Assert.Contains(measurements, m => m.Name == "rocksdb.db.write.micros.count");
    }

    [Fact]
    public void Histograms_ReportQuantilesCountAndSum()
    {
        using TempDb db = StatisticsDb();
        using var metrics = RocksDbMetrics.Register(db.Db, new()
        {
            DatabaseName = "histogram-test",
            Tickers = [],
            Histograms = [Histogram.DbWrite],
            IncludeProperties = false,
        });
        using var collector = new Collector();

        for (int i = 0; i < 20; i++)
        {
            db.Db.Put($"k{i}", "v");
        }

        var measurements = collector.Collect()
            .Where(m => m.Meter.Tags?.Any(t => (string?)t.Value == "histogram-test") == true)
            .ToList();

        var quantiles = measurements.Where(m => m.Name == "rocksdb.db.write.micros").ToList();
        Assert.Equal([0.5, 0.95, 0.99, 1.0], quantiles.Select(q => (double)q.Tags.Single(t => t.Key == "quantile").Value!).OrderBy(q => q));

        Assert.Equal(20, measurements.Single(m => m.Name == "rocksdb.db.write.micros.count").Value);
        Assert.Contains(measurements, m => m.Name == "rocksdb.db.write.micros.sum");

        // Only what was asked for.
        Assert.DoesNotContain(measurements, m => m.Name == "rocksdb.db.get.micros");
        Assert.DoesNotContain(measurements, m => m.Name.StartsWith("rocksdb.estimate", StringComparison.Ordinal));
    }

    [Fact]
    public void Properties_WorkWithoutStatistics()
    {
        using var db = new TempDb();
        using var metrics = RocksDbMetrics.Register(db.Db, new() { DatabaseName = "properties-test" });
        using var collector = new Collector();

        db.Db.Put("a", "1");

        var measurements = collector.Collect()
            .Where(m => m.Meter.Tags?.Any(t => (string?)t.Value == "properties-test") == true)
            .ToList();

        Assert.True(measurements.Single(m => m.Name == "rocksdb.cur-size-all-mem-tables").Value > 0);
        Assert.Contains(measurements, m => m.Name == "rocksdb.estimate-num-keys");
        Assert.Contains(measurements, m => m.Name == "rocksdb.is-write-stopped");

        // No statistics, so no tickers rather than a column of zeros.
        Assert.DoesNotContain(measurements, m => m.Name == "rocksdb.number.keys.written");
    }

    [Fact]
    public void AskingForTickersWithoutStatistics_Throws()
    {
        using var db = new TempDb();

        Assert.Throws<InvalidOperationException>(
            () => RocksDbMetrics.Register(db.Db, new() { Tickers = [Ticker.BlockCacheMiss] }));
    }

    [Fact]
    public void SummedProperties_CoverEveryColumnFamily()
    {
        using var cfOptions = new DbOptions();
        using var db = new TempDb();
        ColumnFamilyHandle other = db.Db.CreateColumnFamily(cfOptions, "other");

        using var metrics = RocksDbMetrics.Register(db.Db, new() { DatabaseName = "families-test" });
        using var collector = new Collector();

        db.Db.Put("a", "1");
        db.Db.Put("b", "2", other);
        db.Db.Flush();
        db.Db.Flush(other);

        double keys = collector.Collect()
            .Where(m => m.Meter.Tags?.Any(t => (string?)t.Value == "families-test") == true)
            .Single(m => m.Name == "rocksdb.estimate-num-keys").Value;

        Assert.Equal(2, keys);
    }

    [Fact]
    public void AfterTheDatabaseCloses_CollectionReportsNothing()
    {
        var db = StatisticsDb();
        using var metrics = RocksDbMetrics.Register(db.Db, new() { DatabaseName = "closed-test" });
        using var collector = new Collector();

        db.Dispose();

        Assert.DoesNotContain(
            collector.Collect(),
            m => m.Meter.Tags?.Any(t => (string?)t.Value == "closed-test") == true);
    }

    /// <summary>
    /// Collections racing the close, which host shutdown makes likely. Each
    /// collection either completes or reports nothing; none reads a closed
    /// database, which would take the process down rather than fail this test.
    /// </summary>
    [Fact]
    public async Task CollectionRacingDispose_NeverReadsAClosedDatabase()
    {
        for (int round = 0; round < 20; round++)
        {
            var db = StatisticsDb();
            db.Db.Put("k", "v");

            using var metrics = RocksDbMetrics.Register(db.Db, new() { DatabaseName = $"race-{round}" });
            using var collector = new Collector();
            using var stop = new CancellationTokenSource();

            Task scraping = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    collector.Collect();
                }
            }, TestContext.Current.CancellationToken);

            await Task.Delay(5, TestContext.Current.CancellationToken);
            db.Dispose();
            stop.Cancel();
            await scraping;
        }
    }

    [Fact]
    public void DisposingTheExport_RemovesItsInstruments()
    {
        using TempDb db = StatisticsDb();
        var metrics = RocksDbMetrics.Register(db.Db, new() { DatabaseName = "removed-test" });
        using var collector = new Collector();

        Assert.Contains(collector.Collect(), m => m.Meter.Tags?.Any(t => (string?)t.Value == "removed-test") == true);

        metrics.Dispose();

        Assert.DoesNotContain(collector.Collect(), m => m.Meter.Tags?.Any(t => (string?)t.Value == "removed-test") == true);
    }
}
