using System.Diagnostics.Metrics;

namespace RocksDbNet;

/// <summary>Chooses what <see cref="RocksDbMetrics.Register"/> exports.</summary>
public sealed class RocksDbMetricsOptions
{
    /// <summary>
    /// A name for the database, reported as the <c>db.name</c> tag on every
    /// instrument so several databases can share one meter. Defaults to none.
    /// </summary>
    public string? DatabaseName { get; init; }

    /// <summary>
    /// The tickers to export, or <see langword="null"/> for all of them. They
    /// need statistics enabled; see <see cref="RocksDbMetrics.Register"/>.
    /// </summary>
    public IReadOnlyCollection<Ticker>? Tickers { get; init; }

    /// <summary>The histograms to export, or <see langword="null"/> for all of them.</summary>
    public IReadOnlyCollection<Histogram>? Histograms { get; init; }

    /// <summary>
    /// Whether to export the database properties operators most often watch:
    /// memtable and block cache sizes, pending compaction, running background
    /// work, key and data size estimates, and write stalls. On by default, and
    /// independent of statistics.
    /// </summary>
    public bool IncludeProperties { get; init; } = true;

    /// <summary>
    /// The factory to create the meter from, typically the one dependency
    /// injection provides, or <see langword="null"/> to create it directly.
    /// </summary>
    public IMeterFactory? MeterFactory { get; init; }
}

/// <summary>
/// Exports a database's statistics and key properties as
/// <see cref="System.Diagnostics.Metrics"/> instruments, which OpenTelemetry,
/// Prometheus exporters and <c>dotnet-counters</c> all read.
/// </summary>
/// <remarks>
/// <para>
/// Everything is observable: nothing is read until a listener collects, and
/// each collection reads the current values, at about one native call per
/// instrument. With nothing listening the export costs nothing.
/// </para>
/// <para>
/// Instruments are named as RocksDb names them, so <c>rocksdb.block.cache.miss</c>
/// here is the same counter as in RocksDb's statistics dump, its documentation
/// and existing dashboards. They live on a meter called
/// <see cref="MeterName"/>, tagged <c>db.name</c> when
/// <see cref="RocksDbMetricsOptions.DatabaseName"/> is set.
/// </para>
/// <list type="bullet">
/// <item>Each ticker is an <see cref="ObservableCounter{T}"/>: tickers only
/// grow.</item>
/// <item>Each histogram is an <see cref="ObservableGauge{T}"/> reporting its
/// 0.5, 0.95 and 0.99 quantiles and its maximum (1.0), tagged
/// <c>quantile</c>, with counters <c>&lt;name&gt;.count</c> and
/// <c>&lt;name&gt;.sum</c>. RocksDb keeps summaries rather than samples, so
/// they are exported as summaries.</item>
/// <item>Each property is an <see cref="ObservableGauge{T}"/> under its
/// RocksDb property name, such as <c>rocksdb.estimate-num-keys</c>. Totals
/// are summed over every column family; database-wide values, such as the
/// shared block cache, are read once.</item>
/// </list>
/// <para>
/// A collection can arrive at any moment, including while the database is
/// being disposed, which host shutdown makes likely. Reads are coordinated
/// with the close: a collection either completes before the database closes
/// or finds it closed and reports nothing. Dispose this object to remove the
/// instruments; disposing the database alone leaves them reporting nothing.
/// </para>
/// </remarks>
public sealed class RocksDbMetrics : IDisposable
{
    /// <summary>The meter every instrument is created on.</summary>
    public const string MeterName = "RocksDb.Net";

    // Totals, so summing them over the column families gives the database's.
    private static readonly string[] SummedProperties =
    [
        "rocksdb.estimate-num-keys",
        "rocksdb.cur-size-all-mem-tables",
        "rocksdb.estimate-pending-compaction-bytes",
        "rocksdb.estimate-live-data-size",
        "rocksdb.live-sst-files-size",
    ];

    // Already database-wide, so read once. Summing these over the families
    // would multiply a shared block cache by the number of families, or add
    // rates together.
    private static readonly string[] DatabaseProperties =
    [
        "rocksdb.block-cache-usage",
        "rocksdb.block-cache-pinned-usage",
        "rocksdb.num-running-compactions",
        "rocksdb.num-running-flushes",
        "rocksdb.background-errors",
        "rocksdb.is-write-stopped",
        "rocksdb.actual-delayed-write-rate",
    ];

    private static readonly double[] Quantiles = [0.5, 0.95, 0.99, 1.0];

    private readonly RocksDb _db;
    private readonly Meter _meter;
    private readonly bool _ownsMeter;

    // Each histogram backs three instruments, which a collection reads one
    // after another. Reading a histogram merges RocksDb's per-core shards,
    // and with all eighty exported that was most of a full collection's cost
    // when each instrument read it for itself. A reading is reused for this
    // long, which spans one collection and is far shorter than any scrape
    // interval.
    private static readonly TimeSpan HistogramReuse = TimeSpan.FromMilliseconds(250);
    private readonly Dictionary<Histogram, (long Taken, HistogramData? Data)> _histograms = [];

    private RocksDbMetrics(RocksDb db, Meter meter, bool ownsMeter)
    {
        _db = db;
        _meter = meter;
        _ownsMeter = ownsMeter;
    }

    /// <summary>Starts exporting <paramref name="db"/>'s statistics and properties.</summary>
    /// <param name="db">The database to observe.</param>
    /// <param name="options">What to export, or <see langword="null"/> for everything.</param>
    /// <returns>The export. Dispose it to remove its instruments.</returns>
    /// <remarks>
    /// Tickers and histograms need statistics, which must be enabled with
    /// <see cref="DbOptions.EnableStatistics"/> before the database is opened.
    /// Without them, the default of exporting everything exports the
    /// properties only, since every ticker would read zero.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Specific tickers or histograms were asked for and statistics are not
    /// enabled, so each would report a misleading zero.
    /// </exception>
    public static RocksDbMetrics Register(RocksDb db, RocksDbMetricsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        db.ThrowIfDisposed();
        options ??= new RocksDbMetricsOptions();

        bool statistics = db.OpenOptions.GetStatisticsString() is not null;

        if (!statistics && (options.Tickers is not null || options.Histograms is not null))
        {
            throw new InvalidOperationException(
                "Tickers and histograms need statistics, which this database was opened without. " +
                "Call DbOptions.EnableStatistics() before opening it.");
        }

        KeyValuePair<string, object?>[]? tags = options.DatabaseName is null
            ? null
            : [new("db.name", options.DatabaseName)];

        Meter meter;
        bool ownsMeter;

        if (options.MeterFactory is not null)
        {
            // The factory owns what it creates and disposes it with itself.
            meter = options.MeterFactory.Create(new MeterOptions(MeterName) { Tags = tags });
            ownsMeter = false;
        }
        else
        {
            meter = new Meter(new MeterOptions(MeterName) { Tags = tags });
            ownsMeter = true;
        }

        var metrics = new RocksDbMetrics(db, meter, ownsMeter);

        if (statistics)
        {
            foreach (Ticker ticker in options.Tickers ?? Enum.GetValues<Ticker>())
            {
                metrics.AddTicker(ticker);
            }

            foreach (Histogram histogram in options.Histograms ?? Enum.GetValues<Histogram>())
            {
                metrics.AddHistogram(histogram);
            }
        }

        if (options.IncludeProperties)
        {
            foreach (string property in SummedProperties)
            {
                metrics.AddProperty(property, summed: true);
            }

            foreach (string property in DatabaseProperties)
            {
                metrics.AddProperty(property, summed: false);
            }
        }

        return metrics;
    }

    private void AddTicker(Ticker ticker)
    {
        string name = StatisticsNames.Of(ticker);

        _meter.CreateObservableCounter(name, () => Read(db => (long)db.OpenOptions.GetTickerCount(ticker)), UnitOf(name));
    }

    private void AddHistogram(Histogram histogram)
    {
        string name = StatisticsNames.Of(histogram);
        string? unit = UnitOf(name);

        _meter.CreateObservableGauge(name, IEnumerable<Measurement<double>> () =>
        {
            HistogramData? data = ReadHistogram(histogram);
            if (data is null)
            {
                return [];
            }

            return
            [
                new Measurement<double>(data.Median, new KeyValuePair<string, object?>("quantile", Quantiles[0])),
                new Measurement<double>(data.P95, new KeyValuePair<string, object?>("quantile", Quantiles[1])),
                new Measurement<double>(data.P99, new KeyValuePair<string, object?>("quantile", Quantiles[2])),
                new Measurement<double>(data.Max, new KeyValuePair<string, object?>("quantile", Quantiles[3])),
            ];
        }, unit);

        _meter.CreateObservableCounter($"{name}.count", IEnumerable<Measurement<long>> () =>
        {
            HistogramData? data = ReadHistogram(histogram);
            return data is null ? [] : [new Measurement<long>((long)data.Count)];
        });

        _meter.CreateObservableCounter($"{name}.sum", IEnumerable<Measurement<long>> () =>
        {
            HistogramData? data = ReadHistogram(histogram);
            return data is null ? [] : [new Measurement<long>((long)data.Sum)];
        }, unit);
    }

    private void AddProperty(string property, bool summed)
    {
        _meter.CreateObservableGauge(property, IEnumerable<Measurement<long>> () =>
        {
            ulong? value = null;

            _db.TryObserve(db => value = summed
                ? db.GetAggregatedPropertyInt(property)
                : db.GetPropertyInt(property));

            return value is null ? [] : [new Measurement<long>((long)value.Value)];
        }, UnitOf(property));
    }

    private IEnumerable<Measurement<long>> Read(Func<RocksDb, long> read)
    {
        long value = 0;
        return _db.TryObserve(db => value = read(db)) ? [new Measurement<long>(value)] : [];
    }

    private HistogramData? ReadHistogram(Histogram histogram)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();

        lock (_histograms)
        {
            if (_histograms.TryGetValue(histogram, out var cached)
                && System.Diagnostics.Stopwatch.GetElapsedTime(cached.Taken, now) < HistogramReuse)
            {
                return cached.Data;
            }
        }

        HistogramData? data = null;

        // Not cached when the database has closed, so a closed database
        // reports nothing rather than its last reading.
        if (!_db.TryObserve(db => data = db.OpenOptions.GetHistogramData(histogram)))
        {
            return null;
        }

        lock (_histograms)
        {
            _histograms[histogram] = (now, data);
        }

        return data;
    }

    // Read off the name, which is how RocksDb says it too: a size ends in
    // bytes or size, a duration in its unit. Anything else is a count, which
    // has no unit to give.
    private static string? UnitOf(string name)
    {
        if (name.EndsWith(".micros", StringComparison.Ordinal) || name.EndsWith("_micros", StringComparison.Ordinal))
        {
            return "us";
        }

        if (name.EndsWith(".nanos", StringComparison.Ordinal) || name.EndsWith("_nanos", StringComparison.Ordinal))
        {
            return "ns";
        }

        if (name.Contains("bytes", StringComparison.Ordinal)
            || name.EndsWith("-usage", StringComparison.Ordinal)
            || name.EndsWith("-size", StringComparison.Ordinal)
            || name.EndsWith("-mem-tables", StringComparison.Ordinal))
        {
            return "By";
        }

        return null;
    }

    /// <summary>Removes the instruments. The database is left as it is.</summary>
    public void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }
}
