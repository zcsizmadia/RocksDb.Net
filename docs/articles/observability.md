# Logging and metrics

RocksDb reports on itself two ways: an info log of what it is doing, and statistics that count what it has done. This library sends the first through `Microsoft.Extensions.Logging` and exports the second through `System.Diagnostics.Metrics`, so both land in whatever an application already uses, OpenTelemetry included.

Both are off until asked for.

## Logging

```csharp
var options = new DbOptions { CreateIfMissing = true }
    .UseLogging(loggerFactory);            // category "RocksDb" by default

using var db = RocksDb.Open(options, path);
```

`UseLogging` creates a `MicrosoftExtensionsLogger` and hands it to the options, which release it after the last database opened with them closes. RocksDb's levels map like this:

| RocksDb | `LogLevel` |
| --- | --- |
| `Debug` | `Debug` |
| `Info` | `Information` |
| `Warn` | `Warning` |
| `Error` | `Error` |
| `Fatal` | `Critical` |
| `Header` | `Debug` |

**Expect volume at `Information`.** Every time a database opens, RocksDb writes several hundred lines: its version, a summary of the files it found, and every option it was opened with. It writes them without a level, so they arrive as `Info` and cannot be told apart from its other informational messages. In production, set the category's minimum to `Warning`, as with any chatty component:

```json
{ "Logging": { "LogLevel": { "RocksDb": "Warning" } } }
```

RocksDb fixes a logger's level when it is created, so `UseLogging` asks the `ILogger` which levels are enabled at that moment and has RocksDb skip formatting anything below the lowest. A filter changed while a database is open still takes effect, because the `ILogger` checks it again for every message, but RocksDb keeps formatting what it was told to. To choose the level yourself, construct the logger directly:

```csharp
options.InfoLog = new MicrosoftExtensionsLogger(logger, InfoLogLevel.Warn);
```

Messages are logged with the template `{RocksDbMessage}`, because RocksDb's own text contains braces. Logging happens on RocksDb's background threads; a logger that throws has the exception reported through `RocksDbCallbacks.UnhandledException` and the line dropped, never the database's work.

## Metrics

```csharp
using var metrics = RocksDbMetrics.Register(db, new RocksDbMetricsOptions
{
    DatabaseName = "orders",               // the db.name tag
});
```

`Register` takes a `RocksDb`. `TransactionDb` and `OptimisticTransactionDb` cannot be exported yet, though `UseLogging` works for them as for any database.

Instruments are created on a meter called `RocksDb.Net` and named as RocksDb names them, so `rocksdb.block.cache.miss` here is the counter of that name in RocksDb's statistics dump, its documentation and existing dashboards.

| Source | Instrument | Notes |
| --- | --- | --- |
| Tickers, such as `rocksdb.number.keys.written` | `ObservableCounter<long>` | Cumulative. Need statistics. |
| Histograms, such as `rocksdb.db.get.micros` | `ObservableGauge<double>` tagged `quantile` 0.5, 0.95, 0.99 and 1.0 (the maximum), plus counters `<name>.count` and `<name>.sum` | RocksDb keeps summaries, not samples. Need statistics. |
| Properties, such as `rocksdb.estimate-num-keys` | `ObservableGauge<long>` | Totals are summed over every column family; database-wide values such as the shared block cache are read once. Need nothing. |

The properties exported are the ones usually watched: memtable size, block cache usage and pinned usage, pending compaction bytes, running flushes and compactions, estimated keys, live data and SST file sizes, background errors, whether writes are stopped, and the delayed write rate.

### Statistics, and what they cost

Tickers and histograms only exist when statistics are enabled, before the database opens:

```csharp
var options = new DbOptions { CreateIfMissing = true }.EnableStatistics();
options.StatisticsLevel = StatsLevel.ExceptDetailedTimers;   // the default level
```

Without statistics, `Register` exports the properties alone. Asking for specific tickers or histograms without them throws, rather than reporting a column of zeros.

There are two costs, and only one of them is paid all the time:

- **Statistics** count on the hot path of every read and write, whether or not anything exports them. That is the cost to weigh, and `StatisticsLevel` trades it against detail.
- **The export** costs nothing until something collects: every instrument is observable, so a value is read only when a listener asks. A collection reads every instrument once, on the collector's thread. Reads and writes do not wait for it.

Measured with `StatisticsOverheadBenchmarks` and `MetricsScrapeBenchmarks` in `RocksDb.Net.Benchmarks` (medium job, i7-12800H, an in-memory database of 10,000 keys with 64-byte values, served from the memtable and block cache):

| | Cost |
| --- | --- |
| Writes with statistics enabled, any level | 4–7% slower than with none |
| Reads with statistics enabled, any level | 10–15% slower than with none |
| One collection, properties only | about 20 µs |
| One collection, everything (343 statistics, 12 properties) | about 1.6 ms |

The level made little difference in this workload. With storage I/O behind each operation, rather than memory, the balance may differ, so run the benchmarks on your own hardware and workload before deciding. A collection every 15 seconds costs about a hundredth of a percent of one core, on the collector's thread.

### Shutdown

A collection can arrive at any moment, including while the database is being disposed, which host shutdown makes likely. The export coordinates with the close: a collection either completes before the database closes or finds it closed and reports nothing. Dispose the `RocksDbMetrics` to remove its instruments.

### With OpenTelemetry

Point the meter provider at the meter by name:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(RocksDbMetrics.MeterName)
        .AddPrometheusExporter());
```

When the application uses dependency injection, pass its `IMeterFactory` as `RocksDbMetricsOptions.MeterFactory`, so the meter's lifetime follows the container's. The meter then belongs to the factory, so disposing the export cannot remove its instruments: they stay registered until the container is disposed, but report nothing and no longer hold the database.

`dotnet-counters monitor --counters RocksDb.Net -p <pid>` shows the same instruments without any of that.

The [ObservabilitySample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/ObservabilitySample) shows both halves running together.
