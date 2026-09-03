# Ownership and lifetime

Every wrapper type holds a native handle, and RocksDb is inconsistent about who frees what. This page collects the cases where that matters, so you do not have to read `db/c.cc` to find out.

The three groups below are not a taste distinction. They come from three genuinely different things `db/c.cc` does with the pointer you give it, and each one implies a different rule for you.

## Handles the native side takes over

These setters wrap the raw pointer in a **fresh** `shared_ptr` or `unique_ptr` of RocksDb's own. RocksDb frees the object, the wrapper stops tracking it, and disposing it yourself does nothing.

| Setter | Type handed over |
| -------- | ------------------ |
| `DbOptions.PrefixExtractor` | `SliceTransform` |
| `DbOptions.EventListener`, `EventListeners` | `EventListener` |
| `DbOptions.MergeOperator` | `MergeOperator` |
| `DbOptions.CompactionFilterFactory` | `CompactionFilterFactory` |
| `BlockBasedTableOptions.SetFilterPolicy` | `FilterPolicy` |

**One instance per setter.** Because the `shared_ptr` is fresh each time, giving the same instance to two options objects creates two independent native owners that each delete it, which corrupts the heap during teardown rather than at the assignment responsible. Assigning an already-attached instance therefore throws `InvalidOperationException` naming the member. Create a separate instance for each options object; sharing one is not supported, however reasonable it looks.

## Handles the options keep for you

RocksDb stores these as a **raw pointer it never frees**, so the wrapper owns them. Keep the options alive as long as the database:

- `DbOptions.Comparator`
- `DbOptions.CompactionFilter`
- `DbOptions.Env`
- `DbOptions.SetWalFilter`

The distinction is not cosmetic. `WalFilter` looks exactly like `EventListener` from the outside, but RocksDb frees one and not the other.

## Handles RocksDb shares with you

These copy an **existing** `shared_ptr`, so the native object is genuinely shared and outlives any single holder:

- `DbOptions.RateLimiter`
- `DbOptions.InfoLog`

`InfoLog` is the sharpest case, and worth knowing about even if you never touch it. The C API gives no destructor callback for a callback logger, so the wrapper cannot be told when RocksDb has finished with it. It stays pinned until the last holder lets go, rather than being unpinned when you dispose it, because RocksDb's copy of the pointer outlives that and would otherwise log through a freed handle.

## Attaching an object to more than one options object

For the two groups above, more than one holder is fine and you do not have to track it. Attaching registers a hold, and the native release happens when the last holder lets go, whichever order things are disposed in.

That makes the ordinary shape safe:

```csharp
using var cmp = new MyComparator();
var opts = new DbOptions();
opts.Comparator = cmp;
using var db = RocksDb.Open(opts, path);
```

The `using` block on `cmp` ends before the database does. Disposing it there is deferred rather than obeyed, so the comparator survives until the database closes and disposes the options. `IsDisposed` stays `false` until the release actually happens, which is the honest answer: the object is still in use.

## `DbOptions.Clone` shares, it does not deep-copy

The native call copies the options struct, so a clone points at the **same** comparator, compaction filter, env, WAL filter, logger and rate limiter as the original. The clone registers itself as another holder of each, so disposing either options object is safe and the objects live until both are gone.

## `RocksDb.Open` consumes its options

`Open`, `OpenReadOnly`, `OpenAsSecondary` and `OpenWithTtl` all transfer ownership of the `DbOptions` you pass. **Do not reuse that instance afterwards**, including for a `BackupEngine.Open` or a second database. Reusing it after the database is closed reads freed memory.

`Destroy`, `Repair` and `ListColumnFamilies` do not take ownership.

## Per-column-family options

A `ColumnFamilyDescriptor` built from a name alone creates its own `DbOptions` and disposes them from its finalizer. A database opened with descriptors holds on to them, so those options cannot be finalized while it is open. Without that, the descriptor list became unreachable as soon as `Open` returned and the next collection destroyed the comparator or compaction filter attached to a column family's options underneath a live database.

They are released when the descriptors are themselves collected, not when the database closes, and that is deliberate rather than a compromise. A descriptor and the options it owns belong to you, and the same list can be handed to a second database: create one, close it, reopen read-only with the same descriptors. Disposing those options as a side effect of closing one database would destroy something you still own and are about to reuse.

Passing a disposed `DbOptions` to any `Open` overload now throws `ObjectDisposedException`, including one reached through a descriptor. Previously the null handle a disposed instance reports went straight into the native open, which requires every pointer argument to be non-null, and the result was an access violation rather than a message naming the mistake.

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

## The rule for reading native data back

A borrowed view is not an acceptable public shape in this library, with `TablePropertiesView` as the single exception. It earns that place because it sits in a per-file read callback where copying the whole property set would be real cost, and because it is explicitly invalidated: using it late throws rather than reading freed memory. Lazy is only allowed when it also fails fast.

Everything else copies. `ColumnFamilyMetadata` and its levels and files, `LiveFileMetadata`, `LiveFileStorageInfo`, `TableProperties`, `CompactionJobStats` and the event listener info records are all read in full before you get them, so none of them need disposing and all of them can be kept and passed between threads.

Merge operands follow the same rule. RocksDb builds those arrays as call-scoped locals, so the operand list is materialised before the callback runs and may be stored beyond it. That costs one array allocation, because each operand was already being copied into a managed array.

The naming convention carries the distinction: a type whose name ends in `View` is a window that stops working when its source goes, and anything else is a copy that does not.
