# Changelog

## Versioning

The package version is `<RocksDbVersion>.<Revision>`, so `11.8.1.1` wraps RocksDb 11.8.1.

Breaking changes land only when `RocksDbVersion` changes. A revision bump alone, `11.8.1.1` to `11.8.1.2`, never breaks source or binary compatibility. A RocksDb version bump already means a different native library and a required re-test, which is the point at which API cleanup costs least.

## 11.8.1.1

Upgrades the native library from RocksDb 11.1.2 to 11.8.1, which added 697 exported C functions, and exposes them across the wrapper.

### Breaking changes

| Change | Migration |
| -------- | ----------- |
| Removed the 12 deprecated fluent setters on `DbOptions` | Use the properties they were marked obsolete in favour of, for example `opts.MergeOperator = x` instead of `opts.SetMergeOperator(x)` |
| `ReadOptions.ReadTier` is now the `ReadTier` enum instead of `int` | `opts.ReadTier = 1` becomes `opts.ReadTier = ReadTier.BlockCacheTier`. The numeric values are unchanged |
| `ColumnFamilyMetadata.FileCount`, `ColumnFamilyMetadata.LevelCount` and `ColumnFamilyLevelMetadata.FileCount` return `int` instead of `nuint` | Remove the casts these forced |
| The pinning helpers on `RocksDbHandle` are `protected` instead of `public` | They are implementation details of the callback pattern. Subclasses can still call them; nothing else needed to |
| `ObjectExtensions.CheckIfMethodOverridden` is `internal` instead of `public` | A reflection helper for listener wiring, in a namespace consumers had no reason to import |

The removed `DbOptions` setters were the `rocksdb-sharp` compatibility surface. All 12 had carried an `[Obsolete]` warning naming their replacement since before 11.1.2.1.

### Fixed

- **`ReadOptions.SetIterateUpperBound` and `SetIterateLowerBound` passed unpinned managed memory to RocksDb**, which stores the bound by reference and dereferences it on every seek. The key is now copied into unmanaged memory owned by the `ReadOptions`, so callers no longer have to keep their buffer alive, which was not expressible anyway.
- **Managed exceptions escaping into native code terminated the process.** All callbacks now contain their own exceptions and report them through the new `RocksDbCallbacks.UnhandledException` event. Each degrades to the outcome that cannot lose data; `Comparator.Compare` is the exception and fails fast, since any invented ordering would corrupt the database.
- **`EventListener` installed null callbacks for methods a subclass did not override**, and RocksDb invokes all ten without a null check, so any listener that overrode only the events it cared about crashed the process.
- **Restoring a backup by id could not have worked.** A code generator gap mapped a by-value `const uint32_t` to an untyped `nint`, so the id was passed as a pointer.

### Added

Full coverage of the RocksDb 11.8.1 additions. Highlights:

- **Options**: about 70 new `DbOptions` properties, plus new members on `ReadOptions`, `WriteOptions`, `FlushOptions`, `BlockBasedTableOptions`, `EnvOptions`, `IngestExternalFileOptions`, `CompactRangeOptions` and `WaitForCompactOptions`. Many settings that were previously write-only are now readable.
- **`TableProperties` and `CompactionJobStats`**, with the event-listener records extended to carry them along with job, thread and column family identity, per-file details and blob file information.
- **Backups**: `BackupEngineOptions`, `CreateBackupOptions` with progress and exclude-files callbacks, `RestoreOptions`, application metadata, `StopBackup`, `VerifyBackup` and restore by id.
- **New operations**: `CompactFiles`, `GetLiveFilesStorageInfo`, `PauseBackgroundWork` and `ContinueBackgroundWork`, `VerifyChecksum`, `VerifyFileChecksums`, `SetDbOptions`, and `ApproximateSizes` with `SizeApproximationOptions`.
- **Write-ahead log**: `GetSortedWalFiles`, `GetCurrentWalFile`, and `GetUpdatesSince` for replication and change-data-capture.
- **Callbacks**: `WalFilter` for rewriting or skipping records during recovery, and `ReadOptions.SetTableFilter` for skipping SST files during a read.

### Changed

- `RocksDbCallbacks.UnhandledException` now passes the wrapper instance that threw as the event sender, which was previously always null. The callback name does not identify it, so an application running several filters or listeners could not tell which one failed.
- Corrected the documentation on `DeleteFilesInRange`, which said it does not remove keys. It does: the keys in a deleted file are gone, with no tombstone. Level 0 files are never deleted, and a file only partly inside the range is left alone. Both now documented.
- Documented that `CancelAllBackgroundWork` is irreversible. Reads and `SetOptions` still work afterwards, but `Flush` fails with "Shutdown in progress". Use `PauseBackgroundWork` and `ContinueBackgroundWork` to suspend and resume.
- Documented the two conditions under which `SuggestCompactRange` does something. Auto compactions must be enabled, and only levels below the highest non-empty one are marked, so a database whose data is all in level 0 is unaffected.
- Tests run on Linux, Windows and macOS, and against net8.0, net9.0 and net10.0. Previously only Linux and only net10.0.
- The build is warning-free and CI enforces it with `-warnaserror`.
- Code coverage is collected and published as a build artifact.

## Earlier releases

See the [GitHub releases](https://github.com/zcsizmadia/RocksDb.Net/releases) for 11.1.2.1 and earlier.
