# API reference

Everything public in the `RocksDbNet` namespace, generated from the source.

The generated P/Invoke declarations in `RocksDbNet.Native` are excluded. There are 1745 of them and they are an implementation detail; use the wrapper types instead.

## Where to start

- `RocksDb` is the database itself: open, read, write, flush, compact, and the write-ahead log.
- `DbOptions` configures a database. Almost everything on it is read once at open time, so see [Callbacks and exceptions](../articles/callbacks.md#options-are-mostly-read-at-open-time) for what can change afterwards.
- `ReadOptions`, `WriteOptions` and `FlushOptions` configure individual operations.
- `WriteBatch` and `WriteBatchWithIndex` group writes atomically.
- `Iterator` scans ranges. `Snapshot` pins a consistent view.
- `BackupEngine` and `Checkpoint` copy a database.
- `EventListener`, `CompactionFilter`, `MergeOperator`, `Comparator` and `WalFilter` are the extension points. Read [Callbacks and exceptions](../articles/callbacks.md) before implementing one.

Before writing much code, [Ownership and lifetime](../articles/ownership.md) is worth ten minutes: RocksDb is inconsistent about which side frees what, and the wrapper follows it rather than hiding it.
