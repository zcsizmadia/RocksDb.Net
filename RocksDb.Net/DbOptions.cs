using System.Text;

using RocksDbNet.Native;

using static System.Net.WebRequestMethods;

namespace RocksDbNet;

/// <summary>Compression algorithm used by RocksDB.</summary>
public enum Compression
{
    None    = 0,
    Snappy  = 1,
    Zlib    = 2,
    Bz2     = 3,
    Lz4     = 4,
    Lz4Hc   = 5,
    Xpress  = 6,
    Zstd    = 7,
}

/// <summary>Compaction style.</summary>
public enum CompactionStyle
{
    Level     = 0,
    Universal = 1,
    Fifo      = 2,
}

/// <summary>WAL recovery mode.</summary>
public enum WalRecoveryMode
{
    TolerateCorruptedTailRecords = 0,
    AbsoluteConsistency          = 1,
    PointInTime                  = 2,
    SkipAnyCorruptedRecords      = 3,
}

/// <summary>
/// Options used when opening a <see cref="RocksDb"/> instance.
/// Maps to <c>rocksdb_options_t</c>.
/// </summary>
public sealed class DbOptions : RocksDbHandle
{
    public DbOptions()
    {
        Handle = NativeMethods.rocksdb_options_create();
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

    /// <summary>If true, RocksDB dynamically adjusts the files sizes in each level.</summary>
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

    /// <summary>Sets the directory where RocksDB writes log files. Defaults to the DB path.</summary>

    public string DbLogDir
    {
        set
        {
            unsafe
            {
                fixed (byte* p = Encoding.UTF8.GetBytes(value + '\0'))
                    NativeMethods.rocksdb_options_set_db_log_dir(Handle, p);
            }
        }
    }

    /// <summary>Sets the directory where WAL files are stored.</summary>

    public string WalDir
    {
        set
        {
            unsafe
            {
                fixed (byte* p = Encoding.UTF8.GetBytes(value + '\0'))
                    NativeMethods.rocksdb_options_set_wal_dir(Handle, p);
            }
        }
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
            value.TransferOwnership();
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
        }
    }

    /// <summary>
    /// Attaches a compaction filter factory. RocksDB calls
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
        }
    }

    // ── Event listener ──────────────────────────────────

    /// <summary>Adds an event listener to receive database event notifications.</summary>
    public EventListener EventListener
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_add_eventlistener(Handle, value.Handle);
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

    // ── Merge operator ──────────────────────────────────

    /// <summary>Sets the built-in UInt64Add merge operator, which treats values as little-endian 64-bit integers and adds them.</summary>
    public DbOptions SetUInt64AddMergeOperator()
    {
        NativeMethods.rocksdb_options_set_uint64add_merge_operator(Handle);
        return this;
    }

    // ── Compatibility with rocksdb-sharp ──────────────────────────────────

    [Obsolete("Use BlockBasedTableFactory property instead.")]
    public DbOptions SetBlockBasedTableFactory(BlockBasedTableOptions tableOptions)
    {
        BlockBasedTableFactory = tableOptions;
        return this;
    }

    [Obsolete("Use RowCache property instead.")]
    public DbOptions SetRowCache(Cache cache)
    {
        RowCache = cache;
        return this;
    }

    [Obsolete("Use RateLimiter property instead.")]
    public DbOptions SetRateLimiter(RateLimiter limiter)
    {
        RateLimiter = limiter;
        return this;
    }

    [Obsolete("Use PrefixExtractor property instead.")]
    public DbOptions SetPrefixExtractor(SliceTransform transform)
    {
        PrefixExtractor = transform;
        return this;
    }

    [Obsolete("Use CompactionFilter property instead.")]
    public DbOptions SetCompactionFilter(CompactionFilter filter)
    {
        CompactionFilter = filter;
        return this;
    }

    [Obsolete("Use CompactionFilterFactory property instead.")]
    public DbOptions SetCompactionFilterFactory(CompactionFilterFactory factory)
    {
        CompactionFilterFactory = factory;
        return this;
    }

    [Obsolete("Use MergeOperator property instead.")]
    public DbOptions SetMergeOperator(MergeOperator mergeOperator)
    {
        MergeOperator = mergeOperator;
        return this;
    }

    [Obsolete("Use Comparator property instead.")]
    public DbOptions SetComparator(Comparator comparator)
    {
        Comparator = comparator;
        return this;
    }

    [Obsolete("Use InfoLog property instead.")]
    public DbOptions SetInfoLog(Logger logger)
    {
        InfoLog = logger;
        return this;
    }

    [Obsolete("Use EventListener or EventListeners properties instead.")]
    public DbOptions AddEventListener(EventListener eventListener)
    {
        EventListener = eventListener;
        return this;
    }

    [Obsolete("Use WalDir property instead.")]
    public DbOptions SetWalDir(string path)
    {
        WalDir = path;
        return this;
    }
    
    [Obsolete("Use DbLogDir property instead.")]
    public DbOptions SetDbLogDir(string path)
    {
        DbLogDir = path;
        return this;
    }

    // ── Dispose ──────────────────────────────────

    public override void DisposeUnmanagedResources()
    {
        NativeMethods.rocksdb_options_destroy(Handle);
    }
}
