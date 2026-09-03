# Callbacks and exceptions

A managed exception must never propagate into native code. The runtime treats that as unrecoverable and terminates the process, so every callback this library installs catches exceptions at the boundary and reports them through `RocksDbCallbacks.UnhandledException`.

Subscribe to that event, because an exception in a callback is otherwise invisible:

```csharp
RocksDbCallbacks.UnhandledException += (_, e) =>
    logger.LogError(e.Exception, "RocksDb callback {Callback} threw", e.CallbackName);
```

Handlers run on whichever thread raised the exception, which is a RocksDb background thread for flush, compaction and backup events, so they must be thread-safe. A handler that itself throws is ignored, so it cannot mask the failure it was reporting.

## What happens after the exception

Each callback degrades to the outcome that cannot lose or alter data:

| Callback | Behaviour when it throws |
| ---------- | -------------------------- |
| `CompactionFilter.Filter` | Entry kept unchanged |
| `CompactionFilterFactory.CreateFilter` | No filter for that compaction job |
| `MergeOperator.FullMerge` | Merge fails, so the read reports a corruption error |
| `MergeOperator.PartialMerge` | Operands kept and merged later by `FullMerge` |
| `Logger.Log` | Log line dropped |
| `EventListener` events | Notification skipped |
| `ReadOptions` table filter | File included in the read |
| `WalFilter.LogRecordFound` | Record applied as written |
| `CreateBackupOptions` exclude-files | File included in the backup |
| `Comparator.Compare` | **Process terminates** |

Two of these are worth understanding rather than memorising.

**The table filter includes the file.** Excluding it is the one outcome that would silently hide data from a read, so a throwing filter is treated as permissive.

**`Comparator.Compare` fails fast.** It has no failure channel: it must return an ordering, and any value invented misrepresents key order for data RocksDb then writes and later reads back. Terminating with a message naming the callback is worse than working code and far better than silent corruption. Handle exceptions inside your comparator.

## Threading

- `EventListener`, `CompactionFilter` and `MergeOperator` callbacks run on RocksDb background threads, concurrently when several flushes or compactions are in flight. Make them thread-safe, or use `CompactionFilterFactory` to get one filter instance per job.
- The `ReadOptions` table filter runs on the reader's own thread, once per candidate SST file per read, so keep it cheap.
- The backup progress and exclude-files callbacks run on copy threads, concurrently when `BackupEngineOptions.MaxBackgroundOperations` is above one.
- `WalFilter` runs during `RocksDb.Open` on the calling thread and never concurrently.

## Options are mostly read at open time

Nearly every `DbOptions` value is read once when the database opens and ignored afterwards. Setting one on a live `DbOptions` does nothing.

`RocksDb.SetDbOptions` is the runtime path for database-scoped options, and `RocksDb.SetOptions` for column-family ones. The two scopes are distinct: `max_background_jobs` is accepted by the first and rejected by the second, and `write_buffer_size` the other way round.
