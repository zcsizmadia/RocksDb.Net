# Compared with rocksdb-sharp

There are two maintained RocksDb bindings for .NET: this one, and [rocksdb-sharp](https://github.com/curiosity-ai/rocksdb-sharp), published on NuGet as [`RocksDB`](https://www.nuget.org/packages/RocksDB). Both wrap the same native library through the RocksDb C API, and both currently ship RocksDb 11.8.1. They make different trade-offs, and depending on what you need, either one can be the better choice. This page sets out the differences so you can decide.

It reflects both projects as of October 2026: RocksDb.Net 11.8.1.3 and `RocksDB` 11.8.1.5032. rocksdb-sharp is actively developed, so check its repository for anything that may have changed since.

## At a glance

| | RocksDb.Net | rocksdb-sharp |
| --- | --- | --- |
| Target frameworks | .NET 8, 9, 10 | netstandard2.0 and 2.1, netcoreapp3.1, .NET 5 to 10 |
| .NET Framework | No | Yes, through netstandard2.0 |
| NativeAOT and trimming | Supported; CI publishes and runs AOT samples | Not possible: the bindings are generated at run time with `Reflection.Emit` |
| How the bindings are made | Generated from `c.h` at build time as `LibraryImport` declarations | One class generated at run time, loaded through `dlopen` or `LoadLibrary` |
| Native libraries | Separate `RocksDb.Net.Runtimes` package, pinned to the RocksDb version | Included in the one package |
| Windows | x64, x86, arm64 | x64; 32-bit is refused at load |
| Linux | x64, arm64, both glibc and musl | x64, arm64, both glibc and musl; a jemalloc build for x64 |
| macOS | x64, arm64 | x64, arm64 |
| Version number | `<RocksDb version>.<revision>`; a revision never breaks callers, and packing checks that against the last release | `<RocksDb version>.<build number>`; no stated compatibility policy |
| Replication | None built in | WAL streaming replication and Raft clustering, in the library |

## Where rocksdb-sharp is the better fit

**You need .NET Framework, or an older .NET.** This library targets .NET 8 and later only, and makes no attempt at netstandard. If your application is on .NET Framework, rocksdb-sharp is the one that runs there.

**You want replication.** rocksdb-sharp includes primary-to-replica replication that streams the write-ahead log, and a Raft layer on top of it for leader election and quorum commit. These are rocksdb-sharp's own C# code, not RocksDb features. They are built on stable RocksDb primitives: reading the WAL with `GetUpdatesSince`, checkpoints, and replaying write batches. They are also recent: replication was added in February 2026 and clustering in May 2026, so judge their maturity by their history rather than by the library's age. This library exposes the same primitives, `GetUpdatesSince` among them, but leaves replication to you. RocksDb itself has no replication or consensus. The nearest thing it offers is the `OpenAsFollower` API, which RocksDb's header marks as experimental. The C API has its tuning options but not the call that opens a follower, so neither library can offer it.

**You want one package and a long track record.** rocksdb-sharp goes back to an original binding from 2016, and the `RocksDB` package has millions of downloads. Its natives come inside the package, including a jemalloc build for Linux x64, and its build pipeline rebuilds them for each new RocksDb release.

## Where this library is the better fit

**NativeAOT and trimming.** The bindings are compile-time `LibraryImport` declarations, callbacks are `[UnmanagedCallersOnly]` function pointers, and the library uses no reflection. CI publishes samples with `PublishAot=true` and runs them, including a managed merge operator. rocksdb-sharp builds its binding class with `Reflection.Emit` at start-up, which NativeAOT cannot do.

**A larger idiomatic API.** Both bind most of the C API, but rocksdb-sharp's object model stops earlier, so for transactions and backups you work with the raw native functions. Here these all have wrapper classes:

- `TransactionDb` and `OptimisticTransactionDb`, with `Transaction`: save points, batched and pinned reads, `GetForUpdate`, and two-phase commit with recovery of prepared transactions.
- `BackupEngine`.
- Secondary, TTL and read-only opens, including with column families.
- `LoadedOptions`, which reads back the options a database was last opened with.
- Event listeners, WAL filters, table properties, SST file management, and the performance context.

**Callbacks that cannot take the process down by accident.** A comparator, merge operator, compaction filter, logger or event listener that throws is caught here. Where RocksDb gives a callback a way to fail, the exception is reported through `RocksDbCallbacks.UnhandledException` and turned into that failure. A comparator has no failure channel, so a throw there stops the process with a message saying which callback threw, rather than corrupting the key order. In rocksdb-sharp's callback wrappers, an exception propagates into native code, which ends the process without a managed stack trace. See [Callbacks and exceptions](callbacks.md).

**Explicit ownership and lifetime.** Every native handle has one owner, and the rules are documented in [Ownership and lifetime](ownership.md):

- A database keeps the objects it depends on alive and releases its children before it closes.
- An object released while RocksDb is still reading it waits until RocksDb lets go.
- Arguments stay reachable until the native call they were passed to returns.

Several of these protections exist because a test reproduced the crash they prevent.

**Observability.** `RocksDbMetrics` exports RocksDb's statistics and key properties as `System.Diagnostics.Metrics` instruments, for OpenTelemetry, Prometheus or `dotnet-counters`. `DbOptions.UseLogging` sends RocksDb's info log to an `ILogger`. In rocksdb-sharp, statistics come back as a string, and the info log setter takes a native logger pointer. See [Logging and metrics](observability.md).

**Fewer copies on the read path.** Reads go through pinned slices, so a value is copied once rather than three times. Merge operators and compaction filters have span forms that do not allocate. `TryGetInto` reads straight into a buffer you own, and `TryGet<T>` decodes a value in place through a `ValueDecoder<T>`, so a `static` decoder reads a value without allocating at all. rocksdb-sharp's nearest equivalent is `Get<T>` with a span deserializer. Its `HasKey` reads and then discards the value, while `ContainsKey` here never copies it.

**How it is tested and released:**

- More than 1,300 tests run on net8.0, net9.0 and net10.0. CI runs on Windows x64 and x86, Linux x64 and arm64, and macOS, plus a NativeAOT job.
- Packing fails on any change that would break a caller compiled against the previous release.
- A symbols package with Source Link ships alongside each release.
- Every code snippet in these guides is compiled and run as a test.

## Moving from rocksdb-sharp

The two APIs look alike at the top level, and the same database files work with both, since both run RocksDb 11.8.1. The differences you will meet first:

| rocksdb-sharp | RocksDb.Net |
| --- | --- |
| `new DbOptions().SetCreateIfMissing(true)` | `new DbOptions { CreateIfMissing = true }` |
| `db.Remove(key)` | `db.Delete(key)` |
| `db.Get(key, cf, readOptions)` with optional parameters | Separate overloads, with the options last: `db.Get(key, cf, options)` |
| `GetFixedSizeValue(key, span)` | `TryGetInto(key, span, out int length)`, which also reports the length a too-small buffer needed |
| `HasKey(key)` | `ContainsKey(key)`, which throws on a failed read rather than returning `false` |
| `Get<T>(key, ISpanDeserializer<T>)` | `TryGet(key, static v => ..., out T value)`, with a state overload for a decoder that needs context |
| `MergeOperators.Create(name, partial, full)` | A subclass of `MergeOperator`, overriding `FullMerge` and optionally `PartialMerge` |
| Raw native calls for transactions and backups | `TransactionDb`, `OptimisticTransactionDb`, `BackupEngine` |

`RocksDb.Open` takes ownership of the `DbOptions` it is given here, so do not reuse that instance for another database; see [Ownership and lifetime](ownership.md).
