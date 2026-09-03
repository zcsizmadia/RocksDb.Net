using System.Collections.Concurrent;
using System.Text;

namespace RocksDbNet;

/// <summary>Compression algorithm used by RocksDb.</summary>
public enum Compression
{
    None = 0,
    Snappy = 1,
    Zlib = 2,
    Bz2 = 3,
    Lz4 = 4,
    Lz4Hc = 5,
    Xpress = 6,
    Zstd = 7,
}

/// <summary>Compaction style.</summary>
public enum CompactionStyle
{
    Level = 0,
    Universal = 1,
    Fifo = 2,
}

/// <summary>WAL recovery mode.</summary>
public enum WalRecoveryMode
{
    TolerateCorruptedTailRecords = 0,
    AbsoluteConsistency = 1,
    PointInTime = 2,
    SkipAnyCorruptedRecords = 3,
}

/// <summary>
/// Options used when opening a <see cref="RocksDb"/> instance.
/// Maps to <c>rocksdb_options_t</c>.
/// </summary>
public sealed class DbOptions : RocksDbHandle
{
    private readonly ConcurrentBag<RocksDbHandle> _ownedHandles = [];

    public DbOptions()
        : base(NativeMethods.rocksdb_options_create())
    {
    }

    /// <summary>Creates a deep copy of this options object.</summary>
    public DbOptions Clone()
    {
        return new DbOptions(NativeMethods.rocksdb_options_create_copy(Handle));
    }

    private DbOptions(nint handle)
    {
        Handle = handle;
    }

    // ── Convenience presets ──────────────────────────────────────────────────

    /// <summary>Sets parallelism for background jobs to <paramref name="totalThreads"/>.</summary>
    public DbOptions IncreaseParallelism(int totalThreads)
    {
        NativeMethods.rocksdb_options_increase_parallelism(Handle, totalThreads);
        return this;
    }

    /// <summary>Optimizes the options for a point-lookup workload using a block cache of <paramref name="blockCacheSizeMb"/> MB.</summary>
    public DbOptions OptimizeForPointLookup(ulong blockCacheSizeMb)
    {
        NativeMethods.rocksdb_options_optimize_for_point_lookup(Handle, blockCacheSizeMb);
        return this;
    }

    /// <summary>Optimizes the options for level-style compaction using <paramref name="memtableMemoryBudgetBytes"/> bytes for memtable.</summary>
    public DbOptions OptimizeLevelStyleCompaction(ulong memtableMemoryBudgetBytes = 512 * 1024 * 1024)
    {
        NativeMethods.rocksdb_options_optimize_level_style_compaction(Handle, memtableMemoryBudgetBytes);
        return this;
    }

    /// <summary>Optimizes the options for universal-style compaction.</summary>
    public DbOptions OptimizeUniversalStyleCompaction(ulong memtableMemoryBudgetBytes = 512 * 1024 * 1024)
    {
        NativeMethods.rocksdb_options_optimize_universal_style_compaction(Handle, memtableMemoryBudgetBytes);
        return this;
    }

    /// <summary>Prepares options for a bulk-load scenario.</summary>
    public DbOptions PrepareForBulkLoad()
    {
        NativeMethods.rocksdb_options_prepare_for_bulk_load(Handle);
        return this;
    }

    // ── Core options ─────────────────────────────────────────────────────────

    /// <summary>If true, create the database directory if it does not exist.</summary>
    public bool CreateIfMissing
    {
        get => NativeMethods.rocksdb_options_get_create_if_missing(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_create_if_missing(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, create missing column families on open.</summary>
    public bool CreateMissingColumnFamilies
    {
        get => NativeMethods.rocksdb_options_get_create_missing_column_families(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_create_missing_column_families(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, return an error if the database already exists.</summary>
    public bool ErrorIfExists
    {
        get => NativeMethods.rocksdb_options_get_error_if_exists(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_error_if_exists(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, perform extra checks on data to detect corruption.</summary>
    public bool ParanoidChecks
    {
        get => NativeMethods.rocksdb_options_get_paranoid_checks(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_paranoid_checks(Handle, value ? (byte)1 : (byte)0);
    }

    // ── Buffer / file limits ─────────────────────────────────────────────────

    /// <summary>Amount of data (in bytes) to build up in memory before writing to disk.</summary>
    public ulong WriteBufferSize
    {
        get => (ulong)NativeMethods.rocksdb_options_get_write_buffer_size(Handle);
        set => NativeMethods.rocksdb_options_set_write_buffer_size(Handle, (nuint)value);
    }

    /// <summary>DB-level write buffer size cap (across all column families).</summary>
    public ulong DbWriteBufferSize
    {
        get => (ulong)NativeMethods.rocksdb_options_get_db_write_buffer_size(Handle);
        set => NativeMethods.rocksdb_options_set_db_write_buffer_size(Handle, (nuint)value);
    }

    /// <summary>Maximum number of open files. -1 = unlimited.</summary>
    public int MaxOpenFiles
    {
        get => NativeMethods.rocksdb_options_get_max_open_files(Handle);
        set => NativeMethods.rocksdb_options_set_max_open_files(Handle, value);
    }

    /// <summary>Total WAL size limit (bytes) before a column-family flush is triggered.</summary>
    public ulong MaxTotalWalSize
    {
        get => NativeMethods.rocksdb_options_get_max_total_wal_size(Handle);
        set => NativeMethods.rocksdb_options_set_max_total_wal_size(Handle, value);
    }

    /// <summary>Maximum number of write buffers that are built up in memory.</summary>
    public int MaxWriteBufferNumber
    {
        get => NativeMethods.rocksdb_options_get_max_write_buffer_number(Handle);
        set => NativeMethods.rocksdb_options_set_max_write_buffer_number(Handle, value);
    }

    /// <summary>Minimum number of write buffers to merge before flushing to storage.</summary>
    public int MinWriteBufferNumberToMerge
    {
        get => NativeMethods.rocksdb_options_get_min_write_buffer_number_to_merge(Handle);
        set => NativeMethods.rocksdb_options_set_min_write_buffer_number_to_merge(Handle, value);
    }

    // ── Compaction / levels ───────────────────────────────────────────────────

    /// <summary>Compression algorithm for all levels.</summary>
    public Compression Compression
    {
        get => (Compression)NativeMethods.rocksdb_options_get_compression(Handle);
        set => NativeMethods.rocksdb_options_set_compression(Handle, (int)value);
    }

    /// <summary>Compression algorithm for the bottommost level.</summary>
    public Compression BottommostCompression
    {
        get => (Compression)NativeMethods.rocksdb_options_get_bottommost_compression(Handle);
        set => NativeMethods.rocksdb_options_set_bottommost_compression(Handle, (int)value);
    }

    /// <summary>Compaction algorithm.</summary>
    public CompactionStyle CompactionStyle
    {
        get => (CompactionStyle)NativeMethods.rocksdb_options_get_compaction_style(Handle);
        set => NativeMethods.rocksdb_options_set_compaction_style(Handle, (int)value);
    }

    /// <summary>Disables automatic compactions.</summary>
    public bool DisableAutoCompactions
    {
        get => NativeMethods.rocksdb_options_get_disable_auto_compactions(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_disable_auto_compactions(Handle, value ? 1 : 0);
    }

    /// <summary>If true, allow ingesting behind the database.</summary>
    public bool AllowIngestBehind
    {
        get => NativeMethods.rocksdb_options_get_allow_ingest_behind(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_allow_ingest_behind(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Size of the readahead buffer used for compaction, in bytes.</summary>
    public ulong CompactionReadaheadSize
    {
        get => NativeMethods.rocksdb_options_get_compaction_readahead_size(Handle);
        set => NativeMethods.rocksdb_options_compaction_readahead_size(Handle, (nuint)value);
    }

    /// <summary>Maximum size of the write buffer to maintain, in bytes.</summary>
    public long MaxWriteBufferSizeToMaintain
    {
        get => NativeMethods.rocksdb_options_get_max_write_buffer_size_to_maintain(Handle);
        set => NativeMethods.rocksdb_options_set_max_write_buffer_size_to_maintain(Handle, value);
    }

    /// <summary>Maximum size of a single compaction, in bytes.</summary>
    public ulong MaxCompactionBytes
    {
        get => NativeMethods.rocksdb_options_get_max_compaction_bytes(Handle);
        set => NativeMethods.rocksdb_options_set_max_compaction_bytes(Handle, value);
    }

    /// <summary>Number of levels used for level-style compaction.</summary>
    public int NumLevels
    {
        get => NativeMethods.rocksdb_options_get_num_levels(Handle);
        set => NativeMethods.rocksdb_options_set_num_levels(Handle, value);
    }

    /// <summary>Number of files at level-0 that triggers compaction.</summary>
    public int Level0FileNumCompactionTrigger
    {
        get => NativeMethods.rocksdb_options_get_level0_file_num_compaction_trigger(Handle);
        set => NativeMethods.rocksdb_options_set_level0_file_num_compaction_trigger(Handle, value);
    }

    /// <summary>Number of level-0 files that triggers write slowdown.</summary>
    public int Level0SlowdownWritesTrigger
    {
        get => NativeMethods.rocksdb_options_get_level0_slowdown_writes_trigger(Handle);
        set => NativeMethods.rocksdb_options_set_level0_slowdown_writes_trigger(Handle, value);
    }

    /// <summary>Number of level-0 files that triggers a full write stop.</summary>
    public int Level0StopWritesTrigger
    {
        get => NativeMethods.rocksdb_options_get_level0_stop_writes_trigger(Handle);
        set => NativeMethods.rocksdb_options_set_level0_stop_writes_trigger(Handle, value);
    }

    /// <summary>Target file size for SST files at level-1, in bytes.</summary>
    public ulong TargetFileSizeBase
    {
        get => NativeMethods.rocksdb_options_get_target_file_size_base(Handle);
        set => NativeMethods.rocksdb_options_set_target_file_size_base(Handle, value);
    }

    /// <summary>Maximum total size of level-1 data in bytes.</summary>
    public ulong MaxBytesForLevelBase
    {
        get => NativeMethods.rocksdb_options_get_max_bytes_for_level_base(Handle);
        set => NativeMethods.rocksdb_options_set_max_bytes_for_level_base(Handle, value);
    }

    /// <summary>Multiplier for computing max bytes at each subsequent level.</summary>
    public double MaxBytesForLevelMultiplier
    {
        get => NativeMethods.rocksdb_options_get_max_bytes_for_level_multiplier(Handle);
        set => NativeMethods.rocksdb_options_set_max_bytes_for_level_multiplier(Handle, value);
    }

    /// <summary>If true, RocksDb dynamically adjusts the files sizes in each level.</summary>
    public bool LevelCompactionDynamicLevelBytes
    {
        get => NativeMethods.rocksdb_options_get_level_compaction_dynamic_level_bytes(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_level_compaction_dynamic_level_bytes(Handle, value ? (byte)1 : (byte)0);
    }

    // ── Background threads ────────────────────────────────────────────────────

    /// <summary>Total count of background jobs (compactions + flushes).</summary>
    public int MaxBackgroundJobs
    {
        get => NativeMethods.rocksdb_options_get_max_background_jobs(Handle);
        set => NativeMethods.rocksdb_options_set_max_background_jobs(Handle, value);
    }

    /// <summary>Maximum number of concurrent background compaction jobs.</summary>
    public int MaxBackgroundCompactions
    {
        get => NativeMethods.rocksdb_options_get_max_background_compactions(Handle);
        set => NativeMethods.rocksdb_options_set_max_background_compactions(Handle, value);
    }

    /// <summary>Maximum number of concurrent background flush jobs.</summary>
    public int MaxBackgroundFlushes
    {
        get => NativeMethods.rocksdb_options_get_max_background_flushes(Handle);
        set => NativeMethods.rocksdb_options_set_max_background_flushes(Handle, value);
    }

    /// <summary>Maximum number of subcompactions per compaction job.</summary>
    public uint MaxSubcompactions
    {
        get => NativeMethods.rocksdb_options_get_max_subcompactions(Handle);
        set => NativeMethods.rocksdb_options_set_max_subcompactions(Handle, value);
    }

    // ── I/O options ───────────────────────────────────────────────────────────

    /// <summary>Enable direct I/O for reads, bypassing the OS page cache.</summary>
    public bool UseDirectReads
    {
        get => NativeMethods.rocksdb_options_get_use_direct_reads(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_use_direct_reads(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Enable direct I/O for flush and compaction writes.</summary>
    public bool UseDirectIoForFlushAndCompaction
    {
        get => NativeMethods.rocksdb_options_get_use_direct_io_for_flush_and_compaction(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_use_direct_io_for_flush_and_compaction(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Allow memory-mapped reads.</summary>
    public bool AllowMmapReads
    {
        get => NativeMethods.rocksdb_options_get_allow_mmap_reads(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_allow_mmap_reads(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Allow memory-mapped writes.</summary>
    public bool AllowMmapWrites
    {
        get => NativeMethods.rocksdb_options_get_allow_mmap_writes(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_allow_mmap_writes(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Use fsync instead of fdatasync for syncing data to disk.</summary>
    public bool UseFsync
    {
        get => NativeMethods.rocksdb_options_get_use_fsync(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_use_fsync(Handle, value ? 1 : 0);
    }

    /// <summary>Allow concurrent inserts into the memtable from multiple threads.</summary>
    public bool AllowConcurrentMemtableWrite
    {
        get => NativeMethods.rocksdb_options_get_allow_concurrent_memtable_write(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_allow_concurrent_memtable_write(Handle, value ? (byte)1 : (byte)0);
    }

    // ── Env ─────────────────────────────────────────────────────────

    /// <summary>Sets the environment for the database options.</summary>
    public Env Env
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_env(Handle, value.Handle);
            _ownedHandles.Add(value);
        }
    }

    // ── WAL / logging ─────────────────────────────────────────────────────────

    /// <summary>WAL recovery mode used when opening the database.</summary>
    public WalRecoveryMode WalRecoveryMode
    {
        get => (WalRecoveryMode)NativeMethods.rocksdb_options_get_wal_recovery_mode(Handle);
        set => NativeMethods.rocksdb_options_set_wal_recovery_mode(Handle, (int)value);
    }

    /// <summary>Time-to-live for WAL files in seconds (0 = no TTL).</summary>
    public ulong WalTtlSeconds
    {
        get => NativeMethods.rocksdb_options_get_WAL_ttl_seconds(Handle);
        set => NativeMethods.rocksdb_options_set_WAL_ttl_seconds(Handle, value);
    }

    /// <summary>Total WAL size limit in MB before old WAL files are archived.</summary>
    public ulong WalSizeLimitMb
    {
        get => NativeMethods.rocksdb_options_get_WAL_size_limit_MB(Handle);
        set => NativeMethods.rocksdb_options_set_WAL_size_limit_MB(Handle, value);
    }

    /// <summary>Bytes synced per WAL write (0 = sync after every write).</summary>
    public ulong WalBytesPerSync
    {
        get => NativeMethods.rocksdb_options_get_wal_bytes_per_sync(Handle);
        set => NativeMethods.rocksdb_options_set_wal_bytes_per_sync(Handle, value);
    }

    /// <summary>If true, WAL is flushed only when explicitly requested.</summary>
    public bool ManualWalFlush
    {
        get => NativeMethods.rocksdb_options_get_manual_wal_flush(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_manual_wal_flush(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Compression type for WAL files.</summary>
    public Compression WalCompression
    {
        get => (Compression)NativeMethods.rocksdb_options_get_wal_compression(Handle);
        set => NativeMethods.rocksdb_options_set_wal_compression(Handle, (int)value);
    }

    /// <summary>
    /// The directory where RocksDb writes log files. An empty string means the
    /// database path is used.
    /// </summary>
    public unsafe string DbLogDir
    {
        get => NativeMethods.PtrToStringUTF8(
            NativeMethods.rocksdb_options_get_db_log_dir(Handle, out nuint length), length) ?? string.Empty;
        set
        {
            fixed (byte* p = Encoding.UTF8.GetBytes(value + '\0'))
                NativeMethods.rocksdb_options_set_db_log_dir(Handle, p);
        }
    }

    /// <summary>
    /// The directory where WAL files are stored. An empty string means the
    /// database path is used.
    /// </summary>
    public unsafe string WalDir
    {
        get => NativeMethods.PtrToStringUTF8(
            NativeMethods.rocksdb_options_get_wal_dir(Handle, out nuint length), length) ?? string.Empty;
        set
        {
            fixed (byte* p = Encoding.UTF8.GetBytes(value + '\0'))
                NativeMethods.rocksdb_options_set_wal_dir(Handle, p);
        }
    }

    /// <summary>
    /// If true, memory allocator statistics are included in the log when
    /// statistics are dumped.
    /// </summary>
    public bool DumpMallocStats
    {
        get => NativeMethods.rocksdb_options_get_dump_malloc_stats(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_dump_malloc_stats(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, the memtable keeps a whole-key bloom filter, which speeds up
    /// point lookups that miss.
    /// </summary>
    public bool MemtableWholeKeyFiltering
    {
        get => NativeMethods.rocksdb_options_get_memtable_whole_key_filtering(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_memtable_whole_key_filtering(Handle, value ? (byte)1 : (byte)0);
    }


    // ── Logging ───────────────────────────────────────────────────────────────

    /// <summary>Info log verbosity level.</summary>
    public InfoLogLevel InfoLogLevel
    {
        get => (InfoLogLevel)NativeMethods.rocksdb_options_get_info_log_level(Handle);
        set => NativeMethods.rocksdb_options_set_info_log_level(Handle, (int)value);
    }

    /// <summary>Maximum number of info log files to keep.</summary>
    public ulong KeepLogFileNum
    {
        get => (ulong)NativeMethods.rocksdb_options_get_keep_log_file_num(Handle);
        set => NativeMethods.rocksdb_options_set_keep_log_file_num(Handle, (nuint)value);
    }

    /// <summary>Maximum size of a single info log file before rotation, in bytes.</summary>
    public ulong MaxLogFileSize
    {
        get => (ulong)NativeMethods.rocksdb_options_get_max_log_file_size(Handle);
        set => NativeMethods.rocksdb_options_set_max_log_file_size(Handle, (nuint)value);
    }

    // ── Misc ──────────────────────────────────────────────────────────────────

    /// <summary>Bytes to sync to storage per write operation (0 = sync everything).</summary>
    public ulong BytesPerSync
    {
        get => NativeMethods.rocksdb_options_get_bytes_per_sync(Handle);
        set => NativeMethods.rocksdb_options_set_bytes_per_sync(Handle, value);
    }

    /// <summary>Period (in seconds) between statistics dumps to the info log.</summary>
    public uint StatsDumpPeriodSec
    {
        get => NativeMethods.rocksdb_options_get_stats_dump_period_sec(Handle);
        set => NativeMethods.rocksdb_options_set_stats_dump_period_sec(Handle, value);
    }

    /// <summary>If true, flush all column families atomically.</summary>
    public bool AtomicFlush
    {
        get => NativeMethods.rocksdb_options_get_atomic_flush(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_atomic_flush(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Time-to-live for data in seconds. Expired entries are removed during compaction.</summary>
    public ulong Ttl
    {
        get => NativeMethods.rocksdb_options_get_ttl(Handle);
        set => NativeMethods.rocksdb_options_set_ttl(Handle, value);
    }

    /// <summary>Interval (in seconds) for periodic compaction of all files.</summary>
    public ulong PeriodicCompactionSeconds
    {
        get => NativeMethods.rocksdb_options_get_periodic_compaction_seconds(Handle);
        set => NativeMethods.rocksdb_options_set_periodic_compaction_seconds(Handle, value);
    }

    /// <summary>Fraction of memtable size allocated to the prefix bloom filter (0.0 to 1.0).</summary>
    public double MemtablePrefixBloomSizeRatio
    {
        get => NativeMethods.rocksdb_options_get_memtable_prefix_bloom_size_ratio(Handle);
        set => NativeMethods.rocksdb_options_set_memtable_prefix_bloom_size_ratio(Handle, value);
    }

    // ── Blob files ────────────────────────────────────────────────────────────

    /// <summary>Enable storing large values in separate blob files.</summary>
    public bool EnableBlobFiles
    {
        get => NativeMethods.rocksdb_options_get_enable_blob_files(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_enable_blob_files(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Minimum value size (in bytes) to be stored in a blob file.</summary>
    public ulong MinBlobSize
    {
        get => NativeMethods.rocksdb_options_get_min_blob_size(Handle);
        set => NativeMethods.rocksdb_options_set_min_blob_size(Handle, value);
    }

    /// <summary>Size of a single blob file in bytes.</summary>
    public ulong BlobFileSize
    {
        get => NativeMethods.rocksdb_options_get_blob_file_size(Handle);
        set => NativeMethods.rocksdb_options_set_blob_file_size(Handle, value);
    }

    /// <summary>Enable garbage collection for blob files during compaction.</summary>
    public bool EnableBlobGc
    {
        get => NativeMethods.rocksdb_options_get_enable_blob_gc(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_enable_blob_gc(Handle, value ? (byte)1 : (byte)0);
    }

    // ── Table factory / cache / rate limiter ──────────────────────────────────

    /// <summary>Configures block-based table options.</summary>

    public BlockBasedTableOptions BlockBasedTableFactory
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_block_based_table_factory(Handle, value.Handle);
        }
    }

    /// <summary>Attaches a row cache.</summary>
    /// 

    public Cache RowCache
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_row_cache(Handle, value.Handle);
        }
    }

    /// <summary>Attaches a rate limiter.</summary>

    public RateLimiter RateLimiter
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_ratelimiter(Handle, value.Handle);
            _ownedHandles.Add(value);
        }
    }

    /// <summary>Attaches a prefix extractor (slice transform).</summary>

    public SliceTransform PrefixExtractor
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_prefix_extractor(Handle, value.Handle);
            value.TransferOwnership();
        }
    }

    // ── Compaction filter ──────────────────────────────────

    /// <summary>
    /// Attaches a compaction filter. The filter is invoked for every key-value
    /// pair during table-file creation (compaction and flush).
    /// </summary>
    /// <remarks>
    /// The <paramref name="value"/> instance must remain alive (not disposed)
    /// for the entire lifetime of the database. Dispose it only after the
    /// database has been closed.
    /// </remarks>

    public CompactionFilter CompactionFilter
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_compaction_filter(Handle, value.Handle);
            _ownedHandles.Add(value);
        }
    }

    /// <summary>
    /// Attaches a compaction filter factory. RocksDb calls
    /// <see cref="CompactionFilterFactory.CreateFilter"/> at the start of
    /// each compaction or flush job and owns the returned filter.
    /// </summary>
    /// <remarks>
    /// The C++ options object takes ownership of the factory via
    /// <c>shared_ptr</c>. Do not dispose <paramref name="value"/> before
    /// the database and its options have been closed.
    /// </remarks>

    public CompactionFilterFactory CompactionFilterFactory
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_compaction_filter_factory(Handle, value.Handle);
            value.TransferOwnership();
        }
    }

    // ── Merge operator ──────────────────────────────────

    /// <summary>Attaches a custom merge operator.</summary>
    public MergeOperator MergeOperator
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_merge_operator(Handle, value.Handle);
            value.TransferOwnership();
        }
    }

    // ── Comparator ──────────────────────────────────

    /// <summary>Attaches a custom comparator for key ordering.</summary>
    public Comparator Comparator
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_comparator(Handle, value.Handle);
            _ownedHandles.Add(value);
        }
    }

    // ── Logging ──────────────────────────────────

    /// <summary>Attaches a custom info logger.</summary>
    public Logger InfoLog
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_info_log(Handle, value.Handle);
            _ownedHandles.Add(value);
        }
    }

    // ── WAL filter ──────────────────────────────────────

    /// <summary>
    /// Installs a filter that inspects, rewrites or skips write-ahead log
    /// records while the database is being opened.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="EventListener"/>, RocksDb stores only a raw pointer to
    /// the filter and never frees it, so these options take responsibility for
    /// disposing it. The filter must therefore outlive the database, which
    /// happens automatically when the options do.
    /// </remarks>
    public DbOptions SetWalFilter(WalFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        NativeMethods.rocksdb_options_set_wal_filter(Handle, filter.Handle);
        _ownedHandles.Add(filter);
        return this;
    }

    /// <summary>Removes any WAL filter previously installed on these options.</summary>
    public DbOptions ClearWalFilter()
    {
        NativeMethods.rocksdb_options_clear_wal_filter(Handle);
        return this;
    }

    // ── Event listener ──────────────────────────────────

    /// <summary>Adds an event listener to receive database event notifications.</summary>
    public EventListener EventListener
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_add_eventlistener(Handle, value.Handle);
            value.TransferOwnership();
        }
    }

    /// <summary>Adds multiple event listeners to receive database event notifications.</summary>
    public IEnumerable<EventListener> EventListeners
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (var listener in value)
            {
                NativeMethods.rocksdb_options_add_eventlistener(Handle, listener.Handle);
                listener.TransferOwnership();
            }
        }
    }

    // ── Statistics ────────────────────────────────────────────────────────────

    /// <summary>Enables collection of internal statistics. Call <see cref="GetStatisticsString"/> to retrieve them.</summary>
    public DbOptions EnableStatistics()
    {
        NativeMethods.rocksdb_options_enable_statistics(Handle);
        return this;
    }

    /// <summary>Returns a string dump of the collected statistics, or <c>null</c> if statistics are not enabled.</summary>
    public string? GetStatisticsString()
    {
        nint ptr = NativeMethods.rocksdb_options_statistics_get_string(Handle);
        if (ptr == nint.Zero)
            return null;

        string? result = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr);
        NativeMethods.rocksdb_free(ptr);
        return result;
    }

    /// <summary>Returns the current value of a ticker from the statistics subsystem.</summary>
    public ulong GetTickerCount(uint tickerType)
        => NativeMethods.rocksdb_options_statistics_get_ticker_count(Handle, tickerType);

    /// <summary>Returns histogram data for a statistics histogram type.</summary>
    public HistogramData? GetHistogramData(uint histogramType)
    {
        nint dataHandle = NativeMethods.rocksdb_statistics_histogram_data_create();
        if (dataHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            NativeMethods.rocksdb_options_statistics_get_histogram_data(Handle, histogramType, dataHandle);
            return new HistogramData(
                NativeMethods.rocksdb_statistics_histogram_data_get_median(dataHandle),
                NativeMethods.rocksdb_statistics_histogram_data_get_p95(dataHandle),
                NativeMethods.rocksdb_statistics_histogram_data_get_p99(dataHandle),
                NativeMethods.rocksdb_statistics_histogram_data_get_average(dataHandle),
                NativeMethods.rocksdb_statistics_histogram_data_get_std_dev(dataHandle),
                NativeMethods.rocksdb_statistics_histogram_data_get_max(dataHandle),
                NativeMethods.rocksdb_statistics_histogram_data_get_count(dataHandle),
                NativeMethods.rocksdb_statistics_histogram_data_get_sum(dataHandle),
                NativeMethods.rocksdb_statistics_histogram_data_get_min(dataHandle));
        }
        finally
        {
            NativeMethods.rocksdb_statistics_histogram_data_destroy(dataHandle);
        }
    }

    // ── Merge operator ──────────────────────────────────

    /// <summary>Sets the built-in UInt64Add merge operator, which treats values as little-endian 64-bit integers and adds them.</summary>
    public DbOptions SetUInt64AddMergeOperator()
    {
        NativeMethods.rocksdb_options_set_uint64add_merge_operator(Handle);
        return this;
    }

    // ── Additional column-family and database settings ───────────────────────
    // Note: nearly every option here is read once when the database is opened.
    // Changing it on a DbOptions instance afterwards has no effect; use
    // RocksDb.SetDbOptions for the options that can change at runtime.

    /// <summary>If true, two-phase commit is allowed, which is required for transactions that prepare before committing.</summary>
    public bool Allow2Pc
    {
        get => NativeMethods.rocksdb_options_get_allow_2pc(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_allow_2pc(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, error messages may include key and value data, which is useful for debugging but may leak sensitive content into logs.</summary>
    public bool AllowDataInErrors
    {
        get => NativeMethods.rocksdb_options_get_allow_data_in_errors(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_allow_data_in_errors(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, RocksDb preallocates file space with fallocate. Disable on filesystems where preallocation is expensive.</summary>
    public bool AllowFallocate
    {
        get => NativeMethods.rocksdb_options_get_allow_fallocate(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_allow_fallocate(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, the next WAL file is created in the background before it is needed, smoothing out write latency at WAL rotation.</summary>
    public bool AsyncWalPrecreate
    {
        get => NativeMethods.rocksdb_options_get_async_wal_precreate(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_async_wal_precreate(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, data recovered from the WAL is not flushed to SST files during open, which speeds up recovery at the cost of replaying more WAL next time.</summary>
    public bool AvoidFlushDuringRecovery
    {
        get => NativeMethods.rocksdb_options_get_avoid_flush_during_recovery(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_avoid_flush_during_recovery(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, memtables are not flushed when the database is closed. Data is still durable if the WAL is enabled, but the next open replays more WAL.</summary>
    public bool AvoidFlushDuringShutdown
    {
        get => NativeMethods.rocksdb_options_get_avoid_flush_during_shutdown(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_avoid_flush_during_shutdown(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, WAL files that are no longer being written are closed on a background thread rather than on the write path.</summary>
    public bool BackgroundCloseInactiveWals
    {
        get => NativeMethods.rocksdb_options_get_background_close_inactive_wals(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_background_close_inactive_wals(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, open recovers as much data as it can rather than failing on corruption. Data may be silently lost, so use this only to salvage a damaged database.</summary>
    public bool BestEffortsRecovery
    {
        get => NativeMethods.rocksdb_options_get_best_efforts_recovery(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_best_efforts_recovery(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Interval in microseconds between attempts to recover from a background error.</summary>
    public ulong BgErrorResumeRetryInterval
    {
        get => NativeMethods.rocksdb_options_get_bgerror_resume_retry_interval(Handle);
        set => NativeMethods.rocksdb_options_set_bgerror_resume_retry_interval(Handle, value);
    }

    /// <summary>Number of partitions used when writing blob files directly.</summary>
    public uint BlobDirectWritePartitions
    {
        get => NativeMethods.rocksdb_options_get_blob_direct_write_partitions(Handle);
        set => NativeMethods.rocksdb_options_set_blob_direct_write_partitions(Handle, value);
    }

    /// <summary>Per-key checksum bytes added to block data to detect in-memory corruption. 0 disables it. Larger values catch more corruption at the cost of memory.</summary>
    public byte BlockProtectionBytesPerKey
    {
        get => NativeMethods.rocksdb_options_get_block_protection_bytes_per_key(Handle);
        set => NativeMethods.rocksdb_options_set_block_protection_bytes_per_key(Handle, value);
    }

    /// <summary>Seconds to wait before compacting a bottommost file that has become eligible. 0 compacts without delay.</summary>
    public uint BottommostFileCompactionDelay
    {
        get => NativeMethods.rocksdb_options_get_bottommost_file_compaction_delay(Handle);
        set => NativeMethods.rocksdb_options_set_bottommost_file_compaction_delay(Handle, value);
    }

    /// <summary>If true, this column family permits ingesting files below the existing data, which is required by IngestExternalFileOptions.IngestBehind.</summary>
    public bool CfAllowIngestBehind
    {
        get => NativeMethods.rocksdb_options_get_cf_allow_ingest_behind(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_cf_allow_ingest_behind(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, compaction verifies that the number of records written matches the number read, catching silent data loss.</summary>
    public bool CompactionVerifyRecordCount
    {
        get => NativeMethods.rocksdb_options_get_compaction_verify_record_count(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_compaction_verify_record_count(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Daily window in which RocksDb may schedule extra background work, as "HH:mm-HH:mm" in UTC. An empty string disables it.</summary>
    public unsafe string DailyOffpeakTimeUtc
    {
        get => NativeMethods.PtrToStringUTF8(
            NativeMethods.rocksdb_options_get_daily_offpeak_time_utc(Handle, out nuint length), length) ?? string.Empty;
        set
        {
            fixed (byte* p = Encoding.UTF8.GetBytes(value + '\0'))
                NativeMethods.rocksdb_options_set_daily_offpeak_time_utc(Handle, p);
        }
    }

    /// <summary>Host identifier recorded in SST files and the manifest. Useful for tracing which machine produced a file.</summary>
    public unsafe string DbHostId
    {
        get => NativeMethods.PtrToStringUTF8(
            NativeMethods.rocksdb_options_get_db_host_id(Handle, out nuint length), length) ?? string.Empty;
        set
        {
            fixed (byte* p = Encoding.UTF8.GetBytes(value + '\0'))
                NativeMethods.rocksdb_options_set_db_host_id(Handle, p);
        }
    }

    /// <summary>Storage temperature applied to files with no more specific temperature setting.</summary>
    public Temperature DefaultTemperature
    {
        get => (Temperature)NativeMethods.rocksdb_options_get_default_temperature(Handle);
        set => NativeMethods.rocksdb_options_set_default_temperature(Handle, (int)value);
    }

    /// <summary>Storage temperature for newly written files.</summary>
    public Temperature DefaultWriteTemperature
    {
        get => (Temperature)NativeMethods.rocksdb_options_get_default_write_temperature(Handle);
        set => NativeMethods.rocksdb_options_set_default_write_temperature(Handle, (int)value);
    }

    /// <summary>Rate in bytes per second that writes are throttled to when RocksDb needs to slow the writer down. 0 lets RocksDb choose.</summary>
    public ulong DelayedWriteRate
    {
        get => NativeMethods.rocksdb_options_get_delayed_write_rate(Handle);
        set => NativeMethods.rocksdb_options_set_delayed_write_rate(Handle, value);
    }

    /// <summary>If true, writes to the memtable are rejected. Used by read-only and ingest-only configurations.</summary>
    public bool DisallowMemtableWrites
    {
        get => NativeMethods.rocksdb_options_get_disallow_memtable_writes(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_disallow_memtable_writes(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, blob files are written directly rather than through the regular write path.</summary>
    public bool EnableBlobDirectWrite
    {
        get => NativeMethods.rocksdb_options_get_enable_blob_direct_write(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_enable_blob_direct_write(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, RocksDb tracks per-thread operation status, which is visible through its thread-status API. Adds a small overhead.</summary>
    public bool EnableThreadTracking
    {
        get => NativeMethods.rocksdb_options_get_enable_thread_tracking(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_enable_thread_tracking(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, RocksDb enforces the rule that a single delete matches at most one put. Violations become errors rather than undefined behaviour.</summary>
    public bool EnforceSingleDelContracts
    {
        get => NativeMethods.rocksdb_options_get_enforce_single_del_contracts(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_enforce_single_del_contracts(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, the write buffer manager memory limit is enforced while recovering from the WAL, not only during normal operation.</summary>
    public bool EnforceWriteBufferManagerDuringRecovery
    {
        get => NativeMethods.rocksdb_options_get_enforce_write_buffer_manager_during_recovery(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_enforce_write_buffer_manager_during_recovery(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, SST files are opened with less upfront validation, trading open-time checking for speed.</summary>
    public bool FastSstOpen
    {
        get => NativeMethods.rocksdb_options_get_fast_sst_open(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_fast_sst_open(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, flush verifies that the number of entries written matches the memtable count, catching silent data loss.</summary>
    public bool FlushVerifyMemtableCount
    {
        get => NativeMethods.rocksdb_options_get_flush_verify_memtable_count(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_flush_verify_memtable_count(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Number of times a follower instance retries catching up to the leader before giving up.</summary>
    public ulong FollowerCatchupRetryCount
    {
        get => NativeMethods.rocksdb_options_get_follower_catchup_retry_count(Handle);
        set => NativeMethods.rocksdb_options_set_follower_catchup_retry_count(Handle, value);
    }

    /// <summary>Milliseconds a follower waits between catch-up attempts.</summary>
    public ulong FollowerCatchupRetryWaitMs
    {
        get => NativeMethods.rocksdb_options_get_follower_catchup_retry_wait_ms(Handle);
        set => NativeMethods.rocksdb_options_set_follower_catchup_retry_wait_ms(Handle, value);
    }

    /// <summary>Milliseconds between a follower refreshing its view of the leader.</summary>
    public ulong FollowerRefreshCatchupPeriodMs
    {
        get => NativeMethods.rocksdb_options_get_follower_refresh_catchup_period_ms(Handle);
        set => NativeMethods.rocksdb_options_set_follower_refresh_catchup_period_ms(Handle, value);
    }

    /// <summary>If true, RocksDb checks LSM structure consistency and fails the operation on a violation rather than continuing with a corrupt view. On by default in recent versions.</summary>
    public bool ForceConsistencyChecks
    {
        get => NativeMethods.rocksdb_options_get_force_consistency_checks(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_force_consistency_checks(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Storage temperature for files in the last level, which typically hold the coldest data.</summary>
    public Temperature LastLevelTemperature
    {
        get => (Temperature)NativeMethods.rocksdb_options_get_last_level_temperature(Handle);
        set => NativeMethods.rocksdb_options_set_last_level_temperature(Handle, (int)value);
    }

    /// <summary>Readahead size in bytes used when reading the WAL. 0 lets RocksDb choose.</summary>
    public ulong LogReadaheadSize
    {
        get => (ulong)NativeMethods.rocksdb_options_get_log_readahead_size(Handle);
        set => NativeMethods.rocksdb_options_set_log_readahead_size(Handle, (nuint)value);
    }

    /// <summary>The lowest cache tier reads are allowed to use. Restricting this keeps reads out of slower tiers.</summary>
    public CacheTier LowestUsedCacheTier
    {
        get => (CacheTier)NativeMethods.rocksdb_options_get_lowest_used_cache_tier(Handle);
        set => NativeMethods.rocksdb_options_set_lowest_used_cache_tier(Handle, (int)value);
    }

    /// <summary>Maximum number of automatic attempts to recover from a background error. 0 disables automatic recovery.</summary>
    public int MaxBgErrorResumeCount
    {
        get => NativeMethods.rocksdb_options_get_max_bgerror_resume_count(Handle);
        set => NativeMethods.rocksdb_options_set_max_bgerror_resume_count(Handle, value);
    }

    /// <summary>Maximum seconds RocksDb sleeps before re-checking whether a compaction should start.</summary>
    public ulong MaxCompactionTriggerWakeupSeconds
    {
        get => NativeMethods.rocksdb_options_get_max_compaction_trigger_wakeup_seconds(Handle);
        set => NativeMethods.rocksdb_options_set_max_compaction_trigger_wakeup_seconds(Handle, value);
    }

    /// <summary>Manifest space amplification limit as a percentage, above which the manifest is rewritten.</summary>
    public int MaxManifestSpaceAmpPct
    {
        get => NativeMethods.rocksdb_options_get_max_manifest_space_amp_pct(Handle);
        set => NativeMethods.rocksdb_options_set_max_manifest_space_amp_pct(Handle, value);
    }

    /// <summary>Maximum combined size in bytes of write batches grouped into a single write.</summary>
    public ulong MaxWriteBatchGroupSizeBytes
    {
        get => NativeMethods.rocksdb_options_get_max_write_batch_group_size_bytes(Handle);
        set => NativeMethods.rocksdb_options_set_max_write_batch_group_size_bytes(Handle, value);
    }

    /// <summary>If true, multi-key lookups against the memtable use a batched path.</summary>
    public bool MemtableBatchLookupOptimization
    {
        get => NativeMethods.rocksdb_options_get_memtable_batch_lookup_optimization(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_memtable_batch_lookup_optimization(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Number of range deletions in a memtable that triggers a flush. 0 means no limit.</summary>
    public uint MemtableMaxRangeDeletions
    {
        get => NativeMethods.rocksdb_options_get_memtable_max_range_deletions(Handle);
        set => NativeMethods.rocksdb_options_set_memtable_max_range_deletions(Handle, value);
    }

    /// <summary>Per-key checksum bytes added to memtable entries to detect in-memory corruption. 0 disables it.</summary>
    public uint MemtableProtectionBytesPerKey
    {
        get => NativeMethods.rocksdb_options_get_memtable_protection_bytes_per_key(Handle);
        set => NativeMethods.rocksdb_options_set_memtable_protection_bytes_per_key(Handle, value);
    }

    /// <summary>If true, memtable per-key checksums are verified on seek as well as on read.</summary>
    public bool MemtableVerifyPerKeyChecksumOnSeek
    {
        get => NativeMethods.rocksdb_options_get_memtable_verify_per_key_checksum_on_seek(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_memtable_verify_per_key_checksum_on_seek(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Storage temperature for newly written metadata files such as the manifest.</summary>
    public Temperature MetadataWriteTemperature
    {
        get => (Temperature)NativeMethods.rocksdb_options_get_metadata_write_temperature(Handle);
        set => NativeMethods.rocksdb_options_set_metadata_write_temperature(Handle, (int)value);
    }

    /// <summary>Number of adjacent tombstones before RocksDb converts them into a range deletion.</summary>
    public uint MinTombstonesForRangeConversion
    {
        get => NativeMethods.rocksdb_options_get_min_tombstones_for_range_conversion(Handle);
        set => NativeMethods.rocksdb_options_set_min_tombstones_for_range_conversion(Handle, value);
    }

    /// <summary>If true, the manifest is written in a form that makes recovery faster.</summary>
    public bool OptimizeManifestForRecovery
    {
        get => NativeMethods.rocksdb_options_get_optimize_manifest_for_recovery(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_optimize_manifest_for_recovery(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, RocksDb re-reads and validates each file it writes. Catches storage problems early at a significant cost in write throughput.</summary>
    public bool ParanoidFileChecks
    {
        get => NativeMethods.rocksdb_options_get_paranoid_file_checks(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_paranoid_file_checks(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, RocksDb performs extra validation of in-memory structures.</summary>
    public bool ParanoidMemoryChecks
    {
        get => NativeMethods.rocksdb_options_get_paranoid_memory_checks(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_paranoid_memory_checks(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, statistics are persisted to a hidden column family so they survive a restart.</summary>
    public bool PersistStatsToDisk
    {
        get => NativeMethods.rocksdb_options_get_persist_stats_to_disk(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_persist_stats_to_disk(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, user-defined timestamps are written to SST files. Setting this false discards them during compaction.</summary>
    public bool PersistUserDefinedTimestamps
    {
        get => NativeMethods.rocksdb_options_get_persist_user_defined_timestamps(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_persist_user_defined_timestamps(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Data written within this many seconds is kept out of the last level, so recent data stays on faster storage. 0 disables it.</summary>
    public ulong PrecludeLastLevelDataSeconds
    {
        get => NativeMethods.rocksdb_options_get_preclude_last_level_data_seconds(Handle);
        set => NativeMethods.rocksdb_options_set_preclude_last_level_data_seconds(Handle, value);
    }

    /// <summary>If true, prefix seek behaviour applies only when a read explicitly opts in, rather than being inferred from the prefix extractor.</summary>
    public bool PrefixSeekOptInOnly
    {
        get => NativeMethods.rocksdb_options_get_prefix_seek_opt_in_only(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_prefix_seek_opt_in_only(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>How many seconds of write-time information RocksDb retains, which enables time-aware features such as temperature placement. 0 disables it.</summary>
    public ulong PreserveInternalTimeSeconds
    {
        get => NativeMethods.rocksdb_options_get_preserve_internal_time_seconds(Handle);
        set => NativeMethods.rocksdb_options_set_preserve_internal_time_seconds(Handle, value);
    }

    /// <summary>Number of threads used by the read I/O executor. 0 lets RocksDb choose.</summary>
    public int ReadIoExecutorThreads
    {
        get => NativeMethods.rocksdb_options_get_read_io_executor_threads(Handle);
        set => NativeMethods.rocksdb_options_set_read_io_executor_threads(Handle, value);
    }

    /// <summary>Fraction of a file that must be read before reads alone trigger its compaction.</summary>
    public double ReadTriggeredCompactionThreshold
    {
        get => NativeMethods.rocksdb_options_get_read_triggered_compaction_threshold(Handle);
        set => NativeMethods.rocksdb_options_set_read_triggered_compaction_threshold(Handle, value);
    }

    /// <summary>If true, an existing manifest is appended to rather than rewritten at open, making open faster.</summary>
    public bool ReuseManifestOnOpen
    {
        get => NativeMethods.rocksdb_options_get_reuse_manifest_on_open(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_reuse_manifest_on_open(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Sample one in this many blocks to measure how well they compress. 0 disables sampling.</summary>
    public ulong SampleForCompression
    {
        get => NativeMethods.rocksdb_options_get_sample_for_compression(Handle);
        set => NativeMethods.rocksdb_options_set_sample_for_compression(Handle, value);
    }

    /// <summary>Bytes of in-memory statistics history to retain.</summary>
    public ulong StatsHistoryBufferSize
    {
        get => (ulong)NativeMethods.rocksdb_options_get_stats_history_buffer_size(Handle);
        set => NativeMethods.rocksdb_options_set_stats_history_buffer_size(Handle, (nuint)value);
    }

    /// <summary>If true, BytesPerSync and WalBytesPerSync are treated as hard limits rather than hints, giving more predictable I/O at some cost in throughput.</summary>
    public bool StrictBytesPerSync
    {
        get => NativeMethods.rocksdb_options_get_strict_bytes_per_sync(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_strict_bytes_per_sync(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, MaxSuccessiveMerges is enforced strictly, even when doing so requires extra work on the read path.</summary>
    public bool StrictMaxSuccessiveMerges
    {
        get => NativeMethods.rocksdb_options_get_strict_max_successive_merges(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_strict_max_successive_merges(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, TargetFileSizeBase is an upper bound rather than a target, so files never exceed it.</summary>
    public bool TargetFileSizeIsUpperBound
    {
        get => NativeMethods.rocksdb_options_get_target_file_size_is_upper_bound(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_target_file_size_is_upper_bound(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, WAL files are tracked in the manifest and verified at open, so a missing or truncated WAL is detected rather than silently ignored.</summary>
    public bool TrackAndVerifyWals
    {
        get => NativeMethods.rocksdb_options_get_track_and_verify_wals(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_track_and_verify_wals(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, WAL writes and memtable writes use separate queues, which improves throughput for two-phase commit workloads.</summary>
    public bool TwoWriteQueues
    {
        get => NativeMethods.rocksdb_options_get_two_write_queues(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_two_write_queues(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>How aggressively blocks belonging to deleted files are evicted from the block cache. 0 leaves them to age out normally.</summary>
    public uint UncacheAggressiveness
    {
        get => NativeMethods.rocksdb_options_get_uncache_aggressiveness(Handle);
        set => NativeMethods.rocksdb_options_set_uncache_aggressiveness(Handle, value);
    }

    /// <summary>If true, compaction reads bypass the OS page cache. Note this is the column-family level setting, distinct from the database-wide flush and compaction option.</summary>
    public bool UseDirectIoForCompactionReads
    {
        get => NativeMethods.rocksdb_options_get_use_direct_io_for_compaction_reads(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_use_direct_io_for_compaction_reads(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, the manifest is read back and verified when the database is closed.</summary>
    public bool VerifyManifestContentOnClose
    {
        get => NativeMethods.rocksdb_options_get_verify_manifest_content_on_close(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_verify_manifest_content_on_close(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Bit flags selecting which compaction output verifications to run. The individual flag values are defined by RocksDb and are not exposed as an enum.</summary>
    public int VerifyOutputFlags
    {
        get => NativeMethods.rocksdb_options_get_verify_output_flags(Handle);
        set => NativeMethods.rocksdb_options_set_verify_output_flags(Handle, value);
    }

    /// <summary>If true, each SST file unique identifier is checked against the manifest at open, detecting a file that has been swapped or truncated.</summary>
    public bool VerifySstUniqueIdInManifest
    {
        get => NativeMethods.rocksdb_options_get_verify_sst_unique_id_in_manifest(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_verify_sst_unique_id_in_manifest(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Storage temperature for newly written WAL files.</summary>
    public Temperature WalWriteTemperature
    {
        get => (Temperature)NativeMethods.rocksdb_options_get_wal_write_temperature(Handle);
        set => NativeMethods.rocksdb_options_set_wal_write_temperature(Handle, (int)value);
    }

    /// <summary>Microseconds a writer spins yielding to other threads before blocking.</summary>
    public ulong WriteThreadMaxYieldUsec
    {
        get => NativeMethods.rocksdb_options_get_write_thread_max_yield_usec(Handle);
        set => NativeMethods.rocksdb_options_set_write_thread_max_yield_usec(Handle, value);
    }

    /// <summary>Microseconds above which a yield is considered slow, which makes the writer stop spinning and block instead.</summary>
    public ulong WriteThreadSlowYieldUsec
    {
        get => NativeMethods.rocksdb_options_get_write_thread_slow_yield_usec(Handle);
        set => NativeMethods.rocksdb_options_set_write_thread_slow_yield_usec(Handle, value);
    }

    /// <summary>
    /// Sets the generator RocksDb uses to compute a whole-file checksum for each
    /// SST file it writes.
    /// </summary>
    /// <remarks>
    /// Required for <see cref="RocksDb.VerifyFileChecksums()"/>, which fails
    /// outright when no generator is configured. RocksDb copies the underlying
    /// shared pointer rather than taking ownership, so the caller keeps
    /// responsibility for disposing <paramref name="factory"/> and must keep it
    /// alive while the database is open.
    /// </remarks>
    public DbOptions SetFileChecksumGenFactory(FileChecksumGenFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        NativeMethods.rocksdb_options_set_file_checksum_gen_factory(Handle, factory.Handle);
        return this;
    }

    // ── Checksum handoff file types ──────────────────────────────────────────
    // A set rather than a single value: checksum handoff is enabled per file
    // kind, so the C API exposes add / remove / contains / count / clear
    // instead of a getter and setter pair.

    /// <summary>
    /// Enables checksum handoff for <paramref name="fileType"/>, asking the
    /// filesystem to verify the checksum RocksDb computed. Only takes effect on
    /// a filesystem that supports it.
    /// </summary>
    public DbOptions AddChecksumHandoffFileType(FileType fileType)
    {
        NativeMethods.rocksdb_options_checksum_handoff_file_types_add(Handle, (int)fileType);
        return this;
    }

    /// <summary>Disables checksum handoff for <paramref name="fileType"/>.</summary>
    public DbOptions RemoveChecksumHandoffFileType(FileType fileType)
    {
        NativeMethods.rocksdb_options_checksum_handoff_file_types_remove(Handle, (int)fileType);
        return this;
    }

    /// <summary>Whether checksum handoff is enabled for <paramref name="fileType"/>.</summary>
    public bool ContainsChecksumHandoffFileType(FileType fileType)
        => NativeMethods.rocksdb_options_checksum_handoff_file_types_contains(Handle, (int)fileType) != 0;

    /// <summary>The number of file kinds checksum handoff is enabled for.</summary>
    public int ChecksumHandoffFileTypeCount
        => checked((int)NativeMethods.rocksdb_options_checksum_handoff_file_types_count(Handle));

    /// <summary>Disables checksum handoff for every file kind.</summary>
    public DbOptions ClearChecksumHandoffFileTypes()
    {
        NativeMethods.rocksdb_options_checksum_handoff_file_types_clear(Handle);
        return this;
    }

    // ── SST write lifetime hint compaction styles ────────────────────────────

    /// <summary>
    /// Asks RocksDb to calculate an SST write lifetime hint for
    /// <paramref name="compactionStyle"/>. The hint is passed to the filesystem,
    /// which may use it to place data.
    /// </summary>
    public DbOptions AddCalculateSstWriteLifetimeHint(CompactionStyle compactionStyle)
    {
        NativeMethods.rocksdb_options_calculate_sst_write_lifetime_hint_set_add(Handle, (int)compactionStyle);
        return this;
    }

    /// <summary>Stops calculating the write lifetime hint for <paramref name="compactionStyle"/>.</summary>
    public DbOptions RemoveCalculateSstWriteLifetimeHint(CompactionStyle compactionStyle)
    {
        NativeMethods.rocksdb_options_calculate_sst_write_lifetime_hint_set_remove(Handle, (int)compactionStyle);
        return this;
    }

    /// <summary>Whether the write lifetime hint is calculated for <paramref name="compactionStyle"/>.</summary>
    public bool ContainsCalculateSstWriteLifetimeHint(CompactionStyle compactionStyle)
        => NativeMethods.rocksdb_options_calculate_sst_write_lifetime_hint_set_contains(Handle, (int)compactionStyle) != 0;

    /// <summary>The number of compaction styles the write lifetime hint is calculated for.</summary>
    public int CalculateSstWriteLifetimeHintCount
        => checked((int)NativeMethods.rocksdb_options_calculate_sst_write_lifetime_hint_set_count(Handle));

    /// <summary>Stops calculating the write lifetime hint for every compaction style.</summary>
    public DbOptions ClearCalculateSstWriteLifetimeHints()
    {
        NativeMethods.rocksdb_options_calculate_sst_write_lifetime_hint_set_clear(Handle);
        return this;
    }


    // ── Dispose ──────────────────────────────────

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_options_destroy(Handle);
    }

    public override void DisposeUnmanagedResources()
    {
        base.DisposeUnmanagedResources();

        // Dispose all owned handles (e.g., RateLimiter, CompactionFilter, etc.)
        foreach (var handle in _ownedHandles)
        {
            handle.Dispose();
        }
        _ownedHandles.Clear();
    }
}
