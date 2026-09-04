![RocksDb.Net](https://raw.githubusercontent.com/zcsizmadia/RocksDb.Net/main/logo-128.png)

# RocksDb.Net

A modern C# wrapper for [RocksDb](https://rocksdb.org/), the high-performance embedded key-value store developed by Meta. Built on .NET's `LibraryImport` source generator with zero-copy spans and deterministic disposal.

[![Build](https://github.com/zcsizmadia/RocksDb.Net/actions/workflows/build.yml/badge.svg)](https://github.com/zcsizmadia/RocksDb.Net/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/RocksDb.Net.svg)](https://www.nuget.org/packages/RocksDb.Net)
[![Downloads](https://img.shields.io/nuget/dt/RocksDb.Net.svg)](https://www.nuget.org/packages/RocksDb.Net)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)
[![RocksDb](https://img.shields.io/badge/RocksDb-11.8.1-blue)](https://github.com/facebook/rocksdb/releases/tag/v11.8.1)
[![License](https://img.shields.io/badge/license-MIT%20%2B%20Apache--2.0-green)](https://github.com/zcsizmadia/RocksDb.Net/blob/main/LICENSE)

**[API reference](https://zcsizmadia.github.io/RocksDb.Net/)** · [Guides](https://zcsizmadia.github.io/RocksDb.Net/articles/getting-started.html) · [Samples](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples) · [Changelog](https://github.com/zcsizmadia/RocksDb.Net/blob/main/CHANGELOG.md)

## Features

- **Full RocksDb C API coverage** — every exported function in the official `rocksdb/c.h` header, auto-generated into P/Invoke bindings
- **Modern .NET** — targets .NET 8, 9 and 10, uses `LibraryImport`, `ReadOnlySpan<byte>`, and `ref struct` iterators
- **Idiomatic C# API** — `IDisposable` handles, properties, string overloads, LINQ-compatible iterators
- **Column families** — create, drop, and operate on multiple column families, with metadata inspection
- **Merge operators** — built-in `UInt64Add` and custom merge operator support
- **Compaction filters** — filter or transform key-value pairs during compaction
- **Transactions** — `WriteBatch` and `WriteBatchWithIndex` for atomic multi-key operations
- **Backups & checkpoints** — `BackupEngine` and `Checkpoint` for point-in-time snapshots
- **SST file ingestion** — bulk-load data with `SstFileWriter`
- **Bloom/Ribbon filters** — configurable filter policies for point lookups
- **Event listeners** — observe flush, compaction, ingestion and background error events, with table properties and compaction statistics
- **Write-ahead log** — list log files, and stream changes with `GetUpdatesSince` for replication
- **WAL filter** — inspect, rewrite or skip records during recovery
- **Cross-platform** — ships native binaries via the `RocksDb.Net.Runtimes` package

## Versioning

The package version is `<RocksDbVersion>.<Revision>`, so `11.8.1.1` wraps RocksDb 11.8.1.

Breaking changes land only when the RocksDb version changes. A revision bump alone, such as `11.8.1.1` to `11.8.1.2`, never breaks compatibility.

The dependency on the native `RocksDb.Net.Runtimes` package is bounded to
revisions of the same RocksDb version, currently `[11.8.1.2, 11.8.2)`. The
P/Invoke declarations are generated from exactly that version's `c.h`, so a
runtimes package built from a different RocksDb version could disagree with them
about the native ABI, and nothing would catch it at build or load time.

**Upgrading from 11.1.2.1 to 11.8.1.1 has breaking changes.** See the [changelog](https://github.com/zcsizmadia/RocksDb.Net/blob/main/CHANGELOG.md#breaking-changes) for the full list and migrations. In short: the 12 deprecated fluent setters on `DbOptions` are gone in favour of the properties that replaced them, `FlushWal` requires its `sync` argument on both database types, `options.EventListener = x` becomes `options.AddEventListener(x)`, the size options that were `nuint` are `ulong`, `ReadTier`, `Checksum` and `VerifyOutputFlags` are enums, and a handful of members that could not work are removed.

## Documentation

- **[API reference](https://zcsizmadia.github.io/RocksDb.Net/)** — every public type and member, generated from the source.
- **[Ownership and lifetime](https://zcsizmadia.github.io/RocksDb.Net/articles/ownership.html)** — which side frees each native handle. RocksDb is inconsistent about this and the wrapper follows it rather than hiding it, so this is worth reading before writing much code.
- **[Callbacks and exceptions](https://zcsizmadia.github.io/RocksDb.Net/articles/callbacks.html)** — what happens when your comparator or merge operator throws, which thread each callback runs on, and why most options only take effect at open time.
- **[Samples](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples)** — runnable examples, one per feature area.
- **[Changelog](https://github.com/zcsizmadia/RocksDb.Net/blob/main/CHANGELOG.md)** — what changed, and how to migrate across a breaking release.

## Requirements

- .NET 8.0, 9.0 or 10.0
- [RocksDb native binaries](https://github.com/zcsizmadia/RocksDb.Net.Runtimes) (provided by the `RocksDb.Net.Runtimes` [NuGet package](https://www.nuget.org/packages/RocksDb.Net.Runtimes))

## Quick Start

Every snippet below is compiled and run as part of this repository's test suite, so they are known to work rather than merely to look right.

### Install

```shell
dotnet add package RocksDb.Net
```

### Basic Usage

```csharp
using RocksDbNet;

// No `using` on the options: Open takes ownership of them.
var options = new DbOptions { CreateIfMissing = true };
using var db = RocksDb.Open(options, "mydb");

// Write
db.Put("hello", "world");

// Read
string? value = db.GetString("hello");
Console.WriteLine(value); // "world"

// Delete
db.Delete("hello");
```

Important lifetime note:

- `RocksDb.Open*` takes ownership of the `DbOptions` instance you pass in.
- After opening, do not reuse that same `DbOptions` instance for other operations (for example `Destroy`, `Repair`, or `ListColumnFamilies`).
- If you need options again, create a new `DbOptions`, or `Clone()` before passing ownership. A clone shares the original's attached comparator, logger and the rest rather than deep-copying them, and registers itself as another holder, so either can be disposed first.

For static utilities that do not open a DB handle (`Destroy`, `Repair`, `ListColumnFamilies`), ownership is not transferred; dispose those options yourself.

### Iteration

```csharp
using var iterator = db.NewIterator();
iterator.SeekToFirst();

foreach (var entry in iterator)
{
    // Spans into the iterator's own buffers, valid until it moves.
    Console.WriteLine($"{Encoding.UTF8.GetString(entry.Key)} = {Encoding.UTF8.GetString(entry.Value)}");
}
```

### Column Families

```csharp
var options = new DbOptions
{
    CreateIfMissing = true,
    CreateMissingColumnFamilies = true
};

var descriptors = new List<ColumnFamilyDescriptor>
{
    new("default"),
    new("logs"),
    new("metrics")
};

using var db = RocksDb.Open(options, "mydb", descriptors);

var logsCf = db.GetColumnFamily("logs");
db.Put("entry1", "data", logsCf);
```

### WriteBatch (Atomic Operations)

```csharp
using var batch = new WriteBatch();
batch.Put("key1", "val1")
     .Put("key2", "val2")
     .Delete("old_key");

db.Write(batch);
```

### Snapshots

```csharp
using var snapshot = db.NewSnapshot();
using var readOpts = new ReadOptions();
readOpts.SetSnapshot(snapshot);

// Reads see the database state at snapshot time
string? val = db.GetString("key", options: readOpts);
```

### Merge Operators

```csharp
// Built-in UInt64 addition
var options = new DbOptions { CreateIfMissing = true };
options.SetUInt64AddMergeOperator();
using var db = RocksDb.Open(options, "counters");

db.Merge("visits"u8, BitConverter.GetBytes(1UL));
db.Merge("visits"u8, BitConverter.GetBytes(5UL));

ulong total = BitConverter.ToUInt64(db.Get("visits"u8));
// total == 6
```

Nested handle lifetime note:

- `MergeOperator`, `CompactionFilterFactory`, `EventListener`, `SliceTransform` and `FilterPolicy` are transferred to native ownership when assigned. RocksDb wraps each in a new shared pointer of its own, so **one instance per options object**: assigning the same one twice would give it two independent owners that each delete it, and the second assignment throws rather than letting that corrupt the heap later.
- `Comparator`, `CompactionFilter`, `Env`, `WalFilter`, `Logger` and `RateLimiter` are released with the `DbOptions`. These may be shared: attaching one registers a hold and the release happens when the last holder lets go, so disposing one options object never pulls an object out from under another, or from under an open database.
- Disposing one of these yourself while it is still attached is therefore deferred, not obeyed, which makes the usual `using` shape safe even though the block ends before the database does.
- In all cases, these objects must outlive the open `RocksDb` instance that uses them.

See [Ownership and lifetime](https://github.com/zcsizmadia/RocksDb.Net/blob/main/docs/articles/ownership.md) for the full rules.

### Metadata and statistics

```csharp
// Statistics live on the options, so keep a reference to read them back.
// These are the options the database owns; do not dispose them yourself.
var options = new DbOptions { CreateIfMissing = true };
options.EnableStatistics();

using var db = RocksDb.Open(options, "stats_db");

db.Put("a", "1");
db.Flush();

var metadata = db.GetColumnFamilyMetadata();
Console.WriteLine(metadata?.Name); // "default"

var histogram = options.GetHistogramData(0);
Console.WriteLine(histogram?.Count);
```

### Live files and approximate sizes

```csharp
using var db = RocksDb.Open(new DbOptions { CreateIfMissing = true }, "inspection_db");

db.Put("a", "1");
db.Put("z", "2");
db.Flush();

// Read in full and copied out, so there is nothing to dispose.
IReadOnlyList<LiveFileMetadata> liveFiles = db.GetLiveFiles();
Console.WriteLine(liveFiles.Count);

ulong[] sizes = db.ApproximateSizes(new[] { ("a", "z") });
Console.WriteLine(sizes[0]);

ulong[] cfSizes = db.ApproximateSizes(db.GetDefaultColumnFamily(), new[] { ("a", "z") });
Console.WriteLine(cfSizes[0]);
```

### Advanced maintenance helpers

```csharp
using var db = RocksDb.Open(new DbOptions { CreateIfMissing = true }, "maintenance_db");

using var compactOpts = new WaitForCompactOptions { Flush = true, TimeoutMicros = 5_000_000 };
db.SuggestCompactRange(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("z"));
db.DeleteFilesInRange("a", "z");
db.WaitForCompact(compactOpts);

// Last, and not before WaitForCompact: cancelling puts the database into
// shutdown, and waiting after that fails with "Shutdown in progress".
db.CancelAllBackgroundWork(wait: false);
```

### Backup & Restore

```csharp
// The options say how to reach the database being backed up; the path is
// where the backups go.
using var backupOptions = new DbOptions();
using var engine = BackupEngine.Open(backupOptions, "backups");

engine.CreateNewBackup(db);

// Later: restore, into a database directory and a WAL directory.
engine.RestoreDbFromLatestBackup("restored_db", "restored_db");
```

### SST File Ingestion

```csharp
using var envOpts = new EnvOptions();
using var dbOpts = new DbOptions();
using var writer = SstFileWriter.Create(envOpts, dbOpts);

writer.Open("data.sst");

// Keys and values are bytes here, and must be in sorted order.
writer.Put("key1"u8, "val1"u8);
writer.Put("key2"u8, "val2"u8);
writer.Finish();

using var ingestOptions = new IngestExternalFileOptions();
db.IngestExternalFile(new[] { "data.sst" }, ingestOptions);
```

### Bloom Filters

```csharp
using var tableOptions = new BlockBasedTableOptions();
tableOptions.SetFilterPolicy(FilterPolicy.CreateBloomFull(10));

var options = new DbOptions { CreateIfMissing = true };
options.BlockBasedTableFactory = tableOptions;

using var db = RocksDb.Open(options, "filtered_db");
```

## Samples

The [`Samples/`](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples) directory contains runnable examples:

| Sample | Description |
| -------- | ------------- |
| [BasicSample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/BasicSample) | Basic open, put, get, delete |
| [WriteBatchSample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/WriteBatchSample) | Atomic multi-key writes |
| [IteratorSample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/IteratorSample) | Key-range scanning and seeking |
| [ColumnFamilySample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/ColumnFamilySample) | Working with column families |
| [SnapshotSample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/SnapshotSample) | Point-in-time consistent reads |
| [MergeOperatorSample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/MergeOperatorSample) | Custom and built-in merge operators |
| [CompactionFilterSample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/CompactionFilterSample) | Filtering keys during compaction |
| [CheckpointAndBackupSample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/CheckpointAndBackupSample) | Backups and checkpoints |
| [SstFileWriterSample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/SstFileWriterSample) | Bulk-loading with SST files |
| [BloomFilterSample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/BloomFilterSample) | Bloom and Ribbon filter policies |
| [EventListenerSample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/EventListenerSample) | Observing database events |
| [ReadOnlyAndSecondarySample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/ReadOnlyAndSecondarySample) | Read-only and secondary instances |
| [TuningAndStatsSample](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples/TuningAndStatsSample) | Performance tuning and statistics |

Run any sample with:

```shell
dotnet run --project Samples/BasicSample
```

## Architecture

```text
RocksDb.Net/
├── Native/
│   ├── NativeMethods.g.cs        # Generated P/Invoke bindings (1,745 functions)
│   └── NativeMethods.Helpers.cs  # Native library resolver and helpers
├── RocksDb.cs                    # Main database class
├── DbOptions.cs                  # Database configuration options
├── WriteBatch.cs                 # Atomic write operations
├── Iterator.cs                   # Key-value iteration
├── ColumnFamilyHandle.cs         # Column family management
├── BackupEngine.cs               # Backup and restore
├── Checkpoint.cs                 # Database checkpoints
├── SstFileWriter.cs              # SST file creation for bulk loading
├── MergeOperator.cs              # Custom merge operators
├── CompactionFilter.cs           # Compaction-time key filtering
├── EventListener.cs              # Database event notifications
└── ...                           # Options, filters, cache, etc.
```

## Building from Source

```shell
git clone https://github.com/zcsizmadia/RocksDb.Net.git
cd RocksDb.Net
dotnet build
dotnet test
```

The P/Invoke bindings in `NativeMethods.g.cs` are auto-generated from the [RocksDb C header](https://github.com/facebook/rocksdb/blob/main/include/rocksdb/c.h). To regenerate:

```shell
dotnet run --project NativeMethodsGenerator -- \
    --version 11.8.1 \
    --output RocksDb.Net/Native/NativeMethods.g.cs
```

## Acknowledgements

[RocksDB](https://rocksdb.org/) is developed and maintained by **Meta Platforms, Inc.** (formerly Facebook, Inc.) and contributors, at [github.com/facebook/rocksdb](https://github.com/facebook/rocksdb). This project is a wrapper around their work and would not exist without it.

RocksDb.Net is not affiliated with, endorsed by, or sponsored by Meta Platforms, Inc.

## License

The wrapper is MIT. See [LICENSE](https://github.com/zcsizmadia/RocksDb.Net/blob/main/LICENSE).

RocksDB is dual-licensed under the GPLv2 and Apache 2.0 License, and its terms apply to the native library and to the generated bindings derived from its C header. See [THIRD-PARTY-NOTICES.md](https://github.com/zcsizmadia/RocksDb.Net/blob/main/THIRD-PARTY-NOTICES.md) for attribution and detail, and RocksDB's own licence files for the authoritative terms.
