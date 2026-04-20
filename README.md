# RocksDb.Net

A modern C# wrapper for [RocksDb](https://rocksdb.org/), the high-performance embedded key-value store developed by Meta. Built on .NET's `LibraryImport` source generator with zero-copy spans and deterministic disposal.

## Features

- **Full RocksDb C API coverage** — 1,000+ auto-generated P/Invoke bindings from the official `rocksdb/c.h` header
- **Modern .NET** — targets .NET 10, uses `LibraryImport`, `ReadOnlySpan<byte>`, and `ref struct` iterators
- **Idiomatic C# API** — `IDisposable` handles, properties, string overloads, LINQ-compatible iterators
- **Column families** — create, drop, and operate on multiple column families
- **Merge operators** — built-in `UInt64Add` and custom merge operator support
- **Compaction filters** — filter or transform key-value pairs during compaction
- **Transactions** — `WriteBatch` and `WriteBatchWithIndex` for atomic multi-key operations
- **Backups & checkpoints** — `BackupEngine` and `Checkpoint` for point-in-time snapshots
- **SST file ingestion** — bulk-load data with `SstFileWriter`
- **Bloom/Ribbon filters** — configurable filter policies for point lookups
- **Event listeners** — observe flush, compaction, and background error events
- **Cross-platform** — ships native binaries via the `RocksDb.Net.Runtimes` package

## Requirements

- .NET 10 SDK (or later)
- RocksDb native binaries (provided by the `RocksDb.Net.Runtimes` NuGet package)

## Quick Start

### Install

```shell
dotnet add package RocksDb.Net
```

### Basic Usage

```csharp
using RocksDbNet;

using var options = new DbOptions { CreateIfMissing = true };
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
- If you need options again, create a new `DbOptions` (or `Clone()` before passing ownership).

For static utilities that do not open a DB handle (`Destroy`, `Repair`, `ListColumnFamilies`), ownership is not transferred; dispose those options yourself.

### Iteration

```csharp
using var iterator = db.NewIterator();
iterator.SeekToFirst();

foreach (var entry in iterator)
{
    Console.WriteLine($"{entry.CurrentKey} = {entry.CurrentValue}");
}
```

### Column Families

```csharp
using var options = new DbOptions
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
string? val = db.GetString("key", readOptions: readOpts);
```

### Merge Operators

```csharp
// Built-in UInt64 addition
using var options = new DbOptions { CreateIfMissing = true };
options.SetUInt64AddMergeOperator();
using var db = RocksDb.Open(options, "counters");

db.Merge("visits", BitConverter.GetBytes(1UL));
db.Merge("visits", BitConverter.GetBytes(5UL));

ulong total = BitConverter.ToUInt64(db.Get("visits"));
// total == 6
```

Nested handle lifetime note:
- `MergeOperator`, `CompactionFilterFactory`, and `EventListener` are transferred to native shared ownership when assigned to `DbOptions`.
- `Comparator`, `CompactionFilter`, `Logger`, and `RateLimiter` are disposed with `DbOptions`.
- In all cases, these objects must outlive the open `RocksDb` instance that uses them.

### Backup & Restore

```csharp
using var engine = BackupEngine.Open("backups");
engine.CreateNewBackup(db);

// Later: restore
engine.RestoreDbFromLatestBackup("restored_db");
```

### SST File Ingestion

```csharp
using var envOpts = new EnvOptions();
using var dbOpts = new DbOptions();
using var writer = SstFileWriter.Create(envOpts, dbOpts);

writer.Open("data.sst");
writer.Put("key1", "val1"); // Keys must be in sorted order
writer.Put("key2", "val2");
writer.Finish();

db.IngestExternalFile(new[] { "data.sst" });
```

### Bloom Filters

```csharp
using var tableOptions = new BlockBasedTableOptions();
tableOptions.SetFilterPolicy(FilterPolicy.CreateBloom(10));

using var options = new DbOptions { CreateIfMissing = true };
options.BlockBasedTableFactory = tableOptions;

using var db = RocksDb.Open(options, "filtered_db");
```

## Samples

The [`Samples/`](Samples/) directory contains runnable examples:

| Sample | Description |
|--------|-------------|
| [Basic](Samples/Basic/) | Basic open, put, get, delete |
| [WriteBatchSample](Samples/WriteBatchSample/) | Atomic multi-key writes |
| [IteratorSample](Samples/IteratorSample/) | Key-range scanning and seeking |
| [ColumnFamilySample](Samples/ColumnFamilySample/) | Working with column families |
| [SnapshotSample](Samples/SnapshotSample/) | Point-in-time consistent reads |
| [MergeOperatorSample](Samples/MergeOperatorSample/) | Custom and built-in merge operators |
| [CompactionFilterSample](Samples/CompactionFilterSample/) | Filtering keys during compaction |
| [BackupAndCheckpointSample](Samples/BackupAndCheckpointSample/) | Backups and checkpoints |
| [SstFileWriterSample](Samples/SstFileWriterSample/) | Bulk-loading with SST files |
| [BloomFilterSample](Samples/BloomFilterSample/) | Bloom and Ribbon filter policies |
| [EventListenerSample](Samples/EventListenerSample/) | Observing database events |
| [ReadOnlyAndSecondarySample](Samples/ReadOnlyAndSecondarySample/) | Read-only and secondary instances |
| [TuningAndStatsSample](Samples/TuningAndStatsSample/) | Performance tuning and statistics |

Run any sample with:

```shell
dotnet run --project Samples/Simple
```

## Architecture

```
RocksDb.Net/
├── Native/
│   ├── NativeMethods.g.cs       # Auto-generated P/Invoke bindings (1,047 functions)
│   ├── NativeMethods.Helpers.cs  # Native library resolver and helpers
│   └── NativeResolver.cs         # Platform-specific library loading
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
git clone https://github.com/user/RocksDb.Net.git
cd RocksDb.Net
dotnet build
dotnet test
```

The P/Invoke bindings in `NativeMethods.g.cs` are auto-generated from the [RocksDb C header](https://github.com/facebook/rocksdb/blob/main/include/rocksdb/c.h). To regenerate:

```shell
dotnet run --project NativeMethodsGenerator -- \
    --version 11.0.4 \
    --output RocksDb.Net/Native/NativeMethods.g.cs
```

## License

See [LICENSE](LICENSE) for details.
