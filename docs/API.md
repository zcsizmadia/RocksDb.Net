# RocksDb.Net API Reference

All types are in the `RocksDbNet` namespace.

---

## RocksDb

The main database class. All instances must be disposed when no longer needed.

Ownership notes:
- `Open`, `OpenReadOnly`, `OpenAsSecondary`, and `OpenWithTtl` transfer ownership of the provided `DbOptions` to the returned `RocksDb` instance.
- Do not reuse the same `DbOptions` instance after a successful open call.
- `Destroy`, `Repair`, and `ListColumnFamilies` do not take ownership of `DbOptions`.

### Opening / Closing

| Method | Description |
|--------|-------------|
| `Open(DbOptions, string)` | Opens (or creates) a database at the given path |
| `Open(DbOptions, string, IEnumerable<ColumnFamilyDescriptor>)` | Opens with specific column families |
| `OpenReadOnly(DbOptions, string)` | Opens in read-only mode |
| `OpenAsSecondary(DbOptions, string, string)` | Opens a secondary (follower) instance |
| `OpenWithTtl(DbOptions, string, int)` | Opens with per-entry time-to-live |
| `Destroy(DbOptions, string)` | Destroys a database directory |
| `Repair(DbOptions, string)` | Attempts to repair a corrupted database |

### Read Operations

| Method | Description |
|--------|-------------|
| `Get(string, ...)` | Returns value as `byte[]`, or `null` |
| `Get(ReadOnlySpan<byte>, ...)` | Binary key overload |
| `GetString(string, ...)` | Returns value as `string?` |
| `TryGet(string, out string?)` | Try-pattern for string reads |
| `MultiGet(string[])` | Batch get for multiple keys |
| `KeyMayExist(string, ...)` | Fast probabilistic existence check |

### Write Operations

| Method | Description |
|--------|-------------|
| `Put(string, string, ...)` | Writes a string key-value pair |
| `Put(ReadOnlySpan<byte>, ReadOnlySpan<byte>, ...)` | Binary overload |
| `Delete(string, ...)` | Removes a key |
| `DeleteRange(string, string, ...)` | Removes all keys in `[start, end)` |
| `Merge(string, string, ...)` | Applies a merge operand |
| `Write(WriteBatch, ...)` | Applies an atomic batch |

### Iteration & Snapshots

| Method | Description |
|--------|-------------|
| `NewIterator(ReadOptions?, ColumnFamilyHandle?)` | Creates a new iterator |
| `NewSnapshot()` | Creates a point-in-time snapshot |

### Column Families

| Method | Description |
|--------|-------------|
| `CreateColumnFamily(DbOptions, string)` | Creates a new column family |
| `DropColumnFamily(ColumnFamilyHandle)` | Removes a column family |
| `GetDefaultColumnFamily()` | Returns the default column family handle |
| `GetColumnFamily(string)` | Returns a named column family handle |
| `ListColumnFamilies(DbOptions, string)` | Lists all column families in a database |

### Maintenance

| Method | Description |
|--------|-------------|
| `Flush(FlushOptions?)` | Flushes memtables to storage |
| `FlushWal(bool)` | Syncs the write-ahead log |
| `CompactRange(...)` | Triggers manual compaction |
| `GetProperty(string, ...)` | Reads a database property |
| `GetPropertyInt(string, ...)` | Reads an integer property |
| `IngestExternalFile(string[], ...)` | Ingests SST files into the database |
| `DisableFileDeletions()` / `EnableFileDeletions()` | Controls SST file deletion |
| `TryCatchUpWithPrimary()` | Catches up a secondary instance |
| `LatestSequenceNumber` | Current sequence number (property) |
| `GetDbIdentity()` | Returns the unique database identity string |

---

## DbOptions

Configuration for opening a database. Supports both get and set for most properties.

Lifetime and ownership:
- A `DbOptions` passed to `RocksDb.Open*` is owned and disposed by that `RocksDb` instance.
- Create a fresh `DbOptions` (or call `Clone()`) if you need to use options for additional operations.
- Nested handle ownership differs by API kind:
    - Shared ownership transfer: `MergeOperator`, `CompactionFilterFactory`, `EventListener`
    - Disposed with `DbOptions`: `Comparator`, `CompactionFilter`, `Logger`, `RateLimiter`

### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `CreateIfMissing` | `bool` | Create the database if it doesn't exist |
| `CreateMissingColumnFamilies` | `bool` | Auto-create missing column families |
| `ErrorIfExists` | `bool` | Fail if the database already exists |
| `ParanoidChecks` | `bool` | Enable aggressive consistency checks |
| `WriteBufferSize` | `ulong` | Size of a single memtable in bytes |
| `MaxOpenFiles` | `int` | Maximum number of open file descriptors |
| `Compression` | `Compression` | Default compression algorithm |
| `CompactionStyle` | `CompactionStyle` | Compaction strategy |
| `MaxBackgroundJobs` | `int` | Total background threads |

### Table / Cache / Filter

| Property | Type | Description |
|----------|------|-------------|
| `BlockBasedTableFactory` | `BlockBasedTableOptions` | Block-based table configuration |
| `RowCache` | `Cache` | Row-level cache (shared ownership) |
| `RateLimiter` | `RateLimiter` | I/O rate limiter (disposed with `DbOptions`) |
| `PrefixExtractor` | `SliceTransform` | Prefix bloom extractor (ownership transferred) |

### Compaction / Merge

| Property | Type | Description |
|----------|------|-------------|
| `CompactionFilter` | `CompactionFilter` | Per-key compaction filter |
| `CompactionFilterFactory` | `CompactionFilterFactory` | Per-job factory (shared ownership transfer) |
| `MergeOperator` | `MergeOperator` | Custom merge operator (shared ownership transfer) |
| `Comparator` | `Comparator` | Custom key comparator |

### Methods

| Method | Description |
|--------|-------------|
| `Clone()` | Deep-copies the options |
| `IncreaseParallelism(int)` | Sets thread count for background operations |
| `OptimizeForPointLookup(ulong)` | Tunes for point lookups |
| `OptimizeLevelStyleCompaction(ulong)` | Tunes for level-style compaction |
| `PrepareForBulkLoad()` | Tunes for bulk loading |
| `EnableStatistics()` | Enables internal statistics collection |
| `GetStatisticsString()` | Returns collected statistics |
| `SetUInt64AddMergeOperator()` | Uses the built-in uint64 addition merge operator |

---

## WriteBatch

Atomic batch of write operations. Supports a fluent API.

```csharp
using var batch = new WriteBatch();
batch.Put("k1", "v1").Put("k2", "v2").Delete("k3");
db.Write(batch);
```

| Method | Description |
|--------|-------------|
| `Put(string, string, ...)` | Add a put operation |
| `Delete(string, ...)` | Add a delete operation |
| `SingleDelete(string, ...)` | Add a single-delete operation |
| `DeleteRange(string, string, ...)` | Add a range delete |
| `Merge(string, string, ...)` | Add a merge operand |
| `PutLogData(byte[])` | Attach metadata to the batch |
| `SetSavePoint()` / `RollbackToSavePoint()` / `PopSavePoint()` | Savepoint management |
| `Count` | Number of operations in the batch |
| `Clear()` | Removes all operations |
| `GetData()` | Returns the serialized batch data |

---

## Iterator

Forward and backward iteration over database keys. Supports `foreach` via a `ref struct` enumerator.

```csharp
using var it = db.NewIterator();
for (it.SeekToFirst(); it.IsValid(); it.Next())
    Console.WriteLine(it.KeyAsString());
```

| Method | Description |
|--------|-------------|
| `SeekToFirst()` / `SeekToLast()` | Jump to the first/last key |
| `Seek(string)` / `Seek(ReadOnlySpan<byte>)` | Seek to a specific key |
| `SeekForPrev(string)` | Seek to the key at or before the target |
| `Next()` / `Prev()` | Move forward/backward |
| `IsValid()` | Check if the iterator points to a valid entry |
| `Key` / `Value` | Current key/value as `ReadOnlySpan<byte>` |
| `KeyAsString()` / `ValueAsString()` | Current key/value as `string` |
| `AsEnumerable()` | Returns an `IEnumerable<(string, string)>` |

---

## Snapshot

An immutable point-in-time view of the database.

```csharp
using var snap = db.NewSnapshot();
using var opts = new ReadOptions();
opts.SetSnapshot(snap);
var val = db.GetString("key", readOptions: opts);
```

| Property | Description |
|----------|-------------|
| `SequenceNumber` | The sequence number at snapshot creation |

---

## ColumnFamilyDescriptor / ColumnFamilyHandle

| Type | Description |
|------|-------------|
| `ColumnFamilyDescriptor(name, options?)` | Describes a column family for `Open` |
| `ColumnFamilyHandle` | Handle to an open column family |
| `ColumnFamilyHandle.Id` | Numeric column family ID |
| `ColumnFamilyHandle.Name` | Column family name |

---

## BackupEngine

Create and restore database backups.

| Method | Description |
|--------|-------------|
| `Open(string)` | Opens a backup engine at the given path |
| `CreateNewBackup(RocksDb)` | Creates a new backup |
| `PurgeOldBackups(uint)` | Keeps only the N most recent backups |
| `RestoreDbFromLatestBackup(string, string?)` | Restores the latest backup |
| `AsEnumerable()` | Lists available backups as `BackupInfo` records |

---

## Checkpoint

Create lightweight database checkpoints (hardlinks where possible).

| Method | Description |
|--------|-------------|
| `Create(RocksDb)` | Creates a checkpoint object from a database |
| `CreateCheckpoint(string, ulong)` | Materializes the checkpoint to a directory |

---

## SstFileWriter

Create SST files for bulk ingestion. Keys must be added in sorted order.

| Method | Description |
|--------|-------------|
| `Create(EnvOptions, DbOptions)` | Creates a new writer |
| `Open(string)` | Opens an output file |
| `Put(string, string)` | Adds a key-value pair |
| `Merge(string, string)` | Adds a merge operand |
| `Delete(string)` | Adds a tombstone |
| `Finish()` | Finalizes the SST file |
| `FileSize` | Size of the output file in bytes |

---

## MergeOperator

Abstract base for custom merge logic. Override `FullMerge` (and optionally `PartialMerge`).

```csharp
class CounterMerge : MergeOperator
{
    public CounterMerge() : base("counter") { }

    public override bool FullMerge(ReadOnlySpan<byte> key,
        bool hasExisting, ReadOnlySpan<byte> existing,
        IEnumerable<byte[]> operands, out byte[] result)
    {
        ulong total = hasExisting ? BitConverter.ToUInt64(existing) : 0;
        foreach (var op in operands)
            total += BitConverter.ToUInt64(op);
        result = BitConverter.GetBytes(total);
        return true;
    }
}
```

---

## CompactionFilter / CompactionFilterFactory

Filter or transform keys during compaction.

| Return Value | Behavior |
|-------------|----------|
| `FilterDecision.Keep` | Preserve the entry |
| `FilterDecision.Remove` | Delete the entry |
| `FilterDecision.ChangeValue` | Replace the value |

Use `CompactionFilterFactory` to create per-job filter instances for thread safety.

---

## Cache

Block cache for SST data blocks.

| Method | Description |
|--------|-------------|
| `CreateLru(ulong)` | LRU cache with the given capacity |
| `CreateLruWithStrictCapacityLimit(ulong)` | LRU cache that rejects inserts when full |
| `CreateHyperClock(ulong, int)` | HyperClockCache (lock-free, high concurrency) |

---

## FilterPolicy

Bloom and Ribbon filter policies to reduce disk reads for point lookups.

| Method | Description |
|--------|-------------|
| `CreateBloom(double)` | Standard Bloom filter (block-based) |
| `CreateBloomFull(double)` | Full Bloom filter |
| `CreateRibbon(double)` | Ribbon filter (more space-efficient) |
| `CreateRibbonHybrid(double, int)` | Hybrid Ribbon + Bloom |

---

## SliceTransform

Prefix extractors for prefix-based bloom filters and iteration.

| Method | Description |
|--------|-------------|
| `CreateFixedPrefix(ulong)` | Extracts a fixed-length prefix |
| `CreateNoop()` | No-op transform |

---

## ReadOptions / WriteOptions / FlushOptions

Per-operation option overrides.

### ReadOptions

| Property | Type | Description |
|----------|------|-------------|
| `VerifyChecksums` | `bool` | Verify data checksums on read |
| `FillCache` | `bool` | Populate block cache on read |
| `Tailing` | `bool` | Non-snapshot tailing iterator |
| `PinData` | `bool` | Pin data in block cache |
| `TotalOrderSeek` | `bool` | Bypass prefix iteration |
| `AsyncIo` | `bool` | Enable async I/O |
| `IgnoreRangeDeletions` | `bool` | Skip range tombstones |

### WriteOptions

| Property | Type | Description |
|----------|------|-------------|
| `Sync` | `bool` | Sync WAL after write |
| `DisableWal` | `bool` | Skip WAL for this write |
| `NoSlowdown` | `bool` | Fail rather than stall |
| `LowPriority` | `bool` | Low-priority write |

---

## Enums

| Enum | Values |
|------|--------|
| `Compression` | None, Snappy, Zlib, Bz2, Lz4, Lz4hc, Xpress, Zstd |
| `CompactionStyle` | Level, Universal, Fifo |
| `CompactionReason` | 19 values (ManualCompaction, LevelL0FilesNum, etc.) |
| `InfoLogLevel` | Debug, Info, Warn, Error, Fatal, Header |
| `FlushReason` | ManualFlush, WriteBufferFull, WalFull, etc. |
| `FilterDecision` | Keep, Remove, ChangeValue |
| `WriteStallCondition` | Normal, Delayed, Stopped |

---

## Ownership Rules

Some RocksDb C API calls transfer ownership of native handles. After calling these setters, the managed wrapper will not attempt to free the native handle:

| Setter | Object Transferred |
|--------|--------------------|
| `DbOptions.PrefixExtractor` | `SliceTransform` |
| `DbOptions.RateLimiter` | `RateLimiter` |
| `BlockBasedTableOptions.SetFilterPolicy()` | `FilterPolicy` |

Callback-based objects (`MergeOperator`, `CompactionFilter`, `CompactionFilterFactory`, `Comparator`) handle ownership automatically via their native destructor callbacks.
