using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using RocksDbNet;

// ─── Observability: logging and metrics ──────────────────────────────────────
// Sends RocksDb's info log through Microsoft.Extensions.Logging and exports its
// statistics as System.Diagnostics.Metrics instruments, then reads a few of
// them back the way a collector would.

string dbPath = Path.Combine(Path.GetTempPath(), "rocksdbnet-observability-sample");

// --- Logging -----------------------------------------------------------------
// RocksDb writes several hundred lines at Information every time a database
// opens: its version, a summary of the files it found, every option. Keep the
// category at Warning in production; it is at Information here to show the
// lines arriving.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
    .AddSimpleConsole(o => o.SingleLine = true)
    .AddFilter("RocksDb", LogLevel.Information)
    .SetMinimumLevel(LogLevel.Warning));

var options = new DbOptions { CreateIfMissing = true }
    .UseLogging(loggerFactory)

    // Tickers and histograms need statistics, enabled before the database
    // opens. They cost some throughput on every read and write; the property
    // gauges below need nothing and cost nothing until collected.
    .EnableStatistics();

using var db = RocksDb.Open(options, dbPath);

// --- Metrics -----------------------------------------------------------------
// Register once per database. In an application this is where OpenTelemetry
// or a Prometheus exporter would be pointed at the "RocksDb.Net" meter; see
// docs/articles/observability.md.
using var metrics = RocksDbMetrics.Register(db, new RocksDbMetricsOptions { DatabaseName = "sample" });

for (int i = 0; i < 10_000; i++)
{
    db.Put($"key{i:D6}", $"value{i}");
}

for (int i = 0; i < 1_000; i++)
{
    _ = db.GetString($"key{i * 7 % 10_000:D6}");
}

db.Flush();

// A collector does this on each scrape: ask every instrument for its value.
// Nothing is read before this, and nothing in between.
string[] interesting =
[
    "rocksdb.number.keys.written",
    "rocksdb.number.keys.read",
    "rocksdb.bytes.written",
    "rocksdb.db.get.micros",
    "rocksdb.estimate-num-keys",
    "rocksdb.cur-size-all-mem-tables",
    "rocksdb.is-write-stopped",
];

using var listener = new MeterListener
{
    InstrumentPublished = (instrument, l) =>
    {
        if (instrument.Meter.Name == RocksDbMetrics.MeterName && interesting.Contains(instrument.Name))
        {
            l.EnableMeasurementEvents(instrument);
        }
    },
};

listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Print(instrument, value, tags));
listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Print(instrument, value, tags));
listener.Start();

Console.WriteLine();
Console.WriteLine("One collection of a few of the instruments:");
listener.RecordObservableInstruments();

db.Dispose();
RocksDb.Destroy(new DbOptions(), dbPath);
return 0;

static void Print(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
{
    string quantile = "";
    foreach (KeyValuePair<string, object?> tag in tags)
    {
        if (tag.Key == "quantile")
        {
            quantile = $" (quantile {tag.Value})";
        }
    }

    Console.WriteLine($"  {instrument.Name,-36}{quantile,-17} {value,14:N1} {instrument.Unit}");
}
