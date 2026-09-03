# Ownership and lifetime

Every wrapper type holds a native handle, and RocksDb is inconsistent about who frees what. This page collects the cases where that matters, so you do not have to read `db/c.cc` to find out.

## Handles the native side takes over

Some setters hand the object to RocksDb, which then frees it. The wrapper stops tracking it, so disposing it yourself would be a double free. You do not need to keep a reference:

| Setter | Type handed over |
|--------|------------------|
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

## Snapshots you can keep, views you cannot

Event listener info objects, and the `TableProperties` and `CompactionJobStats` they carry, are copied out before the callback returns, so they are safe to keep and to pass between threads.

`ReadOptions.SetTableFilter` is the one place that hands you a live view rather than a snapshot. `TablePropertiesView` reads straight from RocksDb's structure, which dies when the callback returns, so using it afterwards throws. Call `ToSnapshot()` inside the callback to keep the values.

The same applies to the two batches a `WalFilter` receives: both belong to RocksDb and must not be disposed or retained.
