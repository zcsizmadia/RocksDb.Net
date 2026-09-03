# Ownership and lifetime

Every wrapper type holds a native handle, and RocksDb is inconsistent about who frees what. This page collects the cases where that matters, so you do not have to read `db/c.cc` to find out.

## Handles the native side takes over

Some setters hand the object to RocksDb, which then frees it. The wrapper stops tracking it, so disposing it yourself would be a double free. You do not need to keep a reference:

| Setter | Type handed over |
| -------- | ------------------ |
| `DbOptions.PrefixExtractor` | `SliceTransform` |
| `DbOptions.RateLimiter` | `RateLimiter` |
| `DbOptions.EventListener` | `EventListener` |
| `BlockBasedTableOptions.SetFilterPolicy` | `FilterPolicy` |

## Handles the options keep for you

Others are stored by RocksDb as a raw pointer it never frees, so the options object owns them and disposes them with itself. Keep the options alive as long as the database:

- `DbOptions.CompactionFilter`
- `DbOptions.CompactionFilterFactory`
- `DbOptions.MergeOperator`
- `DbOptions.Comparator`
- `DbOptions.SetWalFilter`

The distinction is not cosmetic. `WalFilter` looks exactly like `EventListener` from the outside, but RocksDb frees one and not the other.

## Handles that stay yours

A few setters copy a shared pointer rather than taking ownership, so you keep the object and must keep it alive while it is in use:

- `EnvOptions.SetRateLimiter`
- `BackupEngineOptions.SetBackupRateLimiter` and `SetRestoreRateLimiter`
- `DbOptions.SetFileChecksumGenFactory`
- `CompactFilesOptions.CancellationFlag`

## `RocksDb.Open` consumes its options

`Open`, `OpenReadOnly`, `OpenAsSecondary` and `OpenWithTtl` all transfer ownership of the `DbOptions` you pass. **Do not reuse that instance afterwards**, including for a `BackupEngine.Open` or a second database. Reusing it after the database is closed reads freed memory.

`Destroy`, `Repair` and `ListColumnFamilies` do not take ownership.

## Caller-provided buffers

Most methods copy the key or value, or use it only for the duration of the call, so nothing needs to stay alive afterwards.

The iteration bounds are the exception, because RocksDb stores them by reference. `ReadOptions.SetIterateUpperBound` and `SetIterateLowerBound` copy the key into unmanaged memory owned by the `ReadOptions`, released when the bound is replaced or the options are disposed. Passing an empty span clears the bound. You do not need to pin or retain your own buffer.

## Children must be released before their parent

RocksDb requires an iterator, snapshot, pinned value, column family handle or transaction to be destroyed before the database it came from. Their native destructors reach into database internals, and releasing a snapshot dereferences the database pointer with no null check.

You do not have to police this. Each of those types keeps its parent reachable, so the parent cannot be finalized first, and skips its native release if the parent has already been closed. Forgetting to dispose one leaks a small wrapper rather than terminating the process. Disposing in the natural `using` order costs nothing and reclaims everything.

One case the mechanism cannot cover: an iterator holds its `ReadOptions` alive, because RocksDb stores an iterate bound as a pointer into the options struct. That protects against the options being collected, which is the common accident. It cannot protect against disposing them explicitly while the iterator is still in use, because the native struct is gone at that point.

## Indexed write batches

`WriteBatchWithIndex.NewIteratorWithBase` creates its own base iterator internally and never hands one out. That is deliberate: the native call **deletes** the iterator it is given, so passing in an iterator you hold would leave your object pointing at freed memory and its disposal would destroy the same memory a second time.

The overlay iterator reads through both the database and the batch, so it holds both alive and must be disposed before either. Do not modify the batch while such an iterator is positioned; RocksDb invalidates its current key and value.

Applying a batch does not consume it. It can be applied again, or to a second database.

## Transactions

`TransactionDb.Open` consumes its `DbOptions` exactly as `RocksDb.Open` does, and disposes them after the database closes. The `TransactionDbOptions` are copied instead, so dispose those whenever you like.

A `Transaction` must be disposed whether or not it was committed. Neither `Commit` nor `Rollback` releases it; they decide what happens to its writes. A transaction that is never disposed keeps its locks until the database closes.

An iterator created from a transaction is invalidated by `Commit`, `Rollback` and `RollbackToSavePoint`. RocksDb does not stop you using one afterwards, so those three dispose any open iterator first, and a later call throws `ObjectDisposedException` instead of reading freed memory.

Three transaction functions are deliberately not wrapped, because each hands back a pointer to something the transaction still owns and the obvious disposal would free it twice: the transaction snapshot, its internal write batch, and the base database behind a transaction database.

## Snapshots you can keep, views you cannot

Event listener info objects, and the `TableProperties` and `CompactionJobStats` they carry, are copied out before the callback returns, so they are safe to keep and to pass between threads.

`ReadOptions.SetTableFilter` is the one place that hands you a live view rather than a snapshot. `TablePropertiesView` reads straight from RocksDb's structure, which dies when the callback returns, so using it afterwards throws. Call `ToSnapshot()` inside the callback to keep the values.

The same applies to the two batches a `WalFilter` receives: both belong to RocksDb and must not be disposed or retained.
