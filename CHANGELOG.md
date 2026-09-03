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
| `CompactionReason`, `FlushReason`, `BackgroundErrorReason` and `WriteStallCondition` now carry the values RocksDb actually uses | The old values were wrong, so code branching on them was already taking the wrong branch. Re-check any `switch` or comparison on these. Members that named a reason RocksDb does not have are gone: `CompactionReason.UniversalSizeEnumeration`, `FIFOFillCache`, `ChangeLevel`, `ForcedTtl`, `ExternalSstIngestionJob`, `BottommostLevel` (now `BottommostFiles`), `FlushReason.CheckPoint`, `TableMetaWrite`, `BufferLimit`, `SleepInterval`, `BackgroundErrorReason.FlushNoSpace`, `CompactionNoSpace` |
| The four enums above no longer have an explicit `uint`/`int` backing type | They are plain `int`-backed enums now, matching the native declarations. Casts to `uint` need to become casts to `int` |
| `ReadOptions.ReadTier` is now the `ReadTier` enum instead of `int` | `opts.ReadTier = 1` becomes `opts.ReadTier = ReadTier.BlockCacheTier`. The numeric values are unchanged |
| `ColumnFamilyMetadata.FileCount`, `ColumnFamilyMetadata.LevelCount` and `ColumnFamilyLevelMetadata.FileCount` return `int` instead of `nuint` | Remove the casts these forced |
| The pinning helpers on `RocksDbHandle` are `protected` instead of `public` | They are implementation details of the callback pattern. Subclasses can still call them; nothing else needed to |
| `ObjectExtensions.CheckIfMethodOverridden` is `internal` instead of `public` | A reflection helper for listener wiring, in a namespace consumers had no reason to import |

The removed `DbOptions` setters were the `rocksdb-sharp` compatibility surface. All 12 had carried an `[Obsolete]` warning naming their replacement since before 11.1.2.1.

### Fixed

Found by an independent pre-release review before the release was cut. All five predate this release.

- **Four event-listener enums did not match the native values.** `CompactionReason`, `FlushReason` and `BackgroundErrorReason` were shifted from the third member onward and carried several names that do not exist in RocksDb, so a manual compaction was reported as `FilesMarkedForCompaction` and an explicit flush as `WriteBufferManager`. Worst of the four, `WriteStallCondition` was inverted: RocksDb declares `kDelayed, kStopped, kNormal` in that order, because it adds new conditions before `kNormal`, so the onset of a write stall was reported as `Normal` and recovery as `Stopped`. Any application reacting to stalls acted on the opposite signal. The enums now match RocksDb 11.8.1 exactly and the tests assert every member against the header rather than spot-checking the wrapper's own numbers.
- **A `MergeOperator` that did not override `PartialMerge` terminated the process on flush.** RocksDb invokes the partial-merge slot through an unchecked function pointer, unlike the delete-value slot beside it, and reaches it on any flush or non-bottommost compaction that collapses two or more operands for one key. The wrapper installed a null pointer whenever the method was not overridden. The slot is now always installed; the base implementation returns false, which tells RocksDb to keep the operands and merge them later. Same defect class as the `EventListener` null callbacks.
- **Three functions returning a struct by value were declared as returning a pointer.** `rocksdb_iter_key_slice`, `rocksdb_iter_value_slice` and `rocksdb_iter_timestamp_slice` return a 16-byte `rocksdb_slice_t`, which Windows x64 returns through a hidden pointer argument. Calling one would have written over the iterator and read the wrong register as its argument. Nothing in the wrapper called them yet.
- **Four functions returning C `bool` were declared as returning a pointer-sized integer**, which reads register bits no ABI requires the callee to define, so a false result could read as true. Seven `bool` parameters had the same declaration. Nothing in the wrapper called them yet.
- **Pointer-to-array parameters were declared as untyped integers** in thirteen functions, including `rocksdb_open_column_families` and `rocksdb_multi_get_cf`. These worked, because a pointer is a pointer, but the declaration gave no protection against passing the wrong thing.

Found earlier in the 11.8.1 work:

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

- The binding generator now fails rather than guessing when it meets a C type it has no mapping for. Every marshalling defect found in the generated file so far, including the three above and the four fixed earlier in this release, reached the output through a silent fallback to a pointer-sized integer.
- `RocksDbCallbacks.UnhandledException` now passes the wrapper instance that threw as the event sender, which was previously always null. The callback name does not identify it, so an application running several filters or listeners could not tell which one failed.
- Corrected the documentation on `DeleteFilesInRange`, which said it does not remove keys. It does: the keys in a deleted file are gone, with no tombstone. Level 0 files are never deleted, and a file only partly inside the range is left alone. Both now documented.
- Documented that `CancelAllBackgroundWork` is irreversible. Reads and `SetOptions` still work afterwards, but `Flush` fails with "Shutdown in progress". Use `PauseBackgroundWork` and `ContinueBackgroundWork` to suspend and resume.
- Documented the two conditions under which `SuggestCompactRange` does something. Auto compactions must be enabled, and only levels below the highest non-empty one are marked, so a database whose data is all in level 0 is unaffected.
- Tests run on Linux, Windows and macOS, and against net8.0, net9.0 and net10.0. Previously only Linux and only net10.0.
- The build is warning-free and CI enforces it with `-warnaserror`.
- Code coverage is collected and published as a build artifact.

## Earlier releases

See the [GitHub releases](https://github.com/zcsizmadia/RocksDb.Net/releases) for 11.1.2.1 and earlier.
