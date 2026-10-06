# API reference

Everything public in the `RocksDbNet` namespace, generated from the source.

The generated P/Invoke declarations in `RocksDbNet.Native` are excluded. There are 1745 of them and they are an implementation detail; use the wrapper types instead.

## Where to start

- `RocksDb` is the database itself: open, read, write, flush, compact, and the write-ahead log.
- `DbOptions` configures a database. Almost everything on it is read once at open time, so see [Callbacks and exceptions](../articles/callbacks.md#options-are-mostly-read-at-open-time) for what can change afterwards.
- `ReadOptions`, `WriteOptions` and `FlushOptions` configure individual operations.
- `WriteBatch` and `WriteBatchWithIndex` group writes atomically.
- `TransactionDb` and `OptimisticTransactionDb` add conflict detection on top of that, by locking and by validating at commit respectively. Both hand out the same `Transaction`. See [Transactions](../articles/transactions.md) for which to choose, since they fail in different places rather than one being safer.
- `Iterator` scans ranges. `Snapshot` pins a consistent view.
- `BackupEngine` and `Checkpoint` copy a database.
- `EventListener`, `CompactionFilter`, `MergeOperator`, `Comparator` and `WalFilter` are the extension points. Read [Callbacks and exceptions](../articles/callbacks.md) before implementing one.

Before writing much code, [Ownership and lifetime](../articles/ownership.md) is worth ten minutes: RocksDb is inconsistent about which side frees what, and the wrapper follows it rather than hiding it.

## What is not wrapped

Every function in `c.h` is bound, but not every one is reachable from the managed API. About 220 of the 1745 are not, and nearly all of those were left out on purpose:

| Group | Why |
| --- | --- |
| Trace and replay | Only half of it is usable from .NET. See [#82](https://github.com/zcsizmadia/RocksDb.Net/issues/82). |
| Remote compaction service | Needs a service on the other end and a large callback surface. See [#83](https://github.com/zcsizmadia/RocksDb.Net/issues/83). |
| User-defined timestamps | Works, but a parallel API surface per column family. Assessed and deferred in [#79](https://github.com/zcsizmadia/RocksDb.Net/issues/79). |
| Plain and cuckoo table formats | Niche formats with sharp edges. See [#85](https://github.com/zcsizmadia/RocksDb.Net/issues/85). |
| Live-file builders | Only needed to build import metadata by hand, which the export and import pair already does for you. |
| Multi-part (`*v`) writes | Handing RocksDb a list of managed buffers means pinning each one, which costs more than copying the parts into one buffer and writing that. |
| `DeleteRange` on `WriteBatchWithIndex` | RocksDb's indexed batch does not support it, and the C API discards the error, so it would silently do nothing. |
| A write batch's maximum size | Enforced by failing the write, and the C API discards that failure too, so writes past it would vanish. |
| `open_and_compact` | An offline-compaction entry point for tooling rather than applications. |
| Aliases and superseded calls | Functions with a wrapped equivalent, such as the copying `get` calls the pinned reads replaced, or setters for the same field under two names. |

If one of these matters to you, open an issue: several were decided on the strength of no one asking.
