using System.Collections.Concurrent;
using System.Text;

namespace RocksDbNet;

/// <summary>Compression algorithm used by RocksDb.</summary>
public enum Compression
{
    /// <summary>No compression. Fastest, largest on disk.</summary>
    None = 0,

    /// <summary>
    /// Snappy. Fast with modest ratios, and the traditional default.
    /// </summary>
    Snappy = 1,

    /// <summary>
    /// Zlib. Better ratios than <see cref="Snappy"/> at a noticeably higher
    /// CPU cost.
    /// </summary>
    Zlib = 2,

    /// <summary>
    /// Bzip2. Strong ratios but slow enough that it is rarely the right
    /// choice for live data.
    /// </summary>
    Bz2 = 3,

    /// <summary>
    /// LZ4. Comparable ratios to <see cref="Snappy"/> and usually faster;
    /// a good default for the upper levels.
    /// </summary>
    Lz4 = 4,

    /// <summary>
    /// LZ4 in high-compression mode. Compresses harder and slower than
    /// <see cref="Lz4"/> while decompressing just as fast, which suits data
    /// written once and read often.
    /// </summary>
    Lz4Hc = 5,

    /// <summary>
    /// Microsoft Xpress. Available only where the platform provides it, so
    /// not portable.
    /// </summary>
    Xpress = 6,

    /// <summary>
    /// Zstandard. The usual choice for the bottommost level: ratios near
    /// <see cref="Zlib"/> with decompression closer to <see cref="Lz4"/>.
    /// </summary>
    Zstd = 7,
}

// Whether a build actually supports a given algorithm depends on how the
// native library was compiled. Selecting one that is missing fails at open
// time rather than falling back silently.

/// <summary>Compaction style.</summary>
public enum CompactionStyle
{
    /// <summary>
    /// Levelled compaction, the default. Keeps read amplification low and
    /// space overhead small, at the cost of writing data several times as it
    /// moves down the levels.
    /// </summary>
    Level = 0,

    /// <summary>
    /// Universal compaction. Writes each piece of data far fewer times, so
    /// write-heavy workloads go faster, but it can transiently need close to
    /// twice the database size in free space and reads touch more files.
    /// </summary>
    Universal = 1,

    /// <summary>
    /// First-in, first-out. Never really compacts; it deletes the oldest
    /// files once a size or age bound is passed. For time-series and cache
    /// data where losing the oldest entries is acceptable, and wrong for
    /// anything that must be kept.
    /// </summary>
    Fifo = 2,
}

/// <summary>
/// Which verifications RocksDb runs over the files a compaction produces, and
/// when.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>VerifyOutputFlags</c> in
/// <c>include/rocksdb/advanced_options.h</c> at the pinned version.
/// </para>
/// <para>
/// The bits come in two groups, and a value needs at least one from each to
/// do anything: the first three say <em>what</em> to verify, and the two
/// <c>EnableFor</c> bits say <em>which</em> compactions to verify it on. That
/// is why <see cref="BlockChecksum"/> on its own has no observable effect.
/// </para>
/// <para>
/// This was a plain <c>int</c>, described as flags RocksDb does not expose.
/// The C API takes and returns <c>int</c> while RocksDb keeps a
/// <c>uint32_t</c>, which is why <see cref="All"/> arrives there as -1 and why
/// setting -1 through the old <c>int</c> property was, unintentionally, asking
/// for everything.
/// </para>
/// </remarks>
[Flags]
public enum VerifyOutputFlags : uint
{
    /// <summary>Verify nothing. The default.</summary>
    None = 0,

    /// <summary>Verify the block checksums of each output file.</summary>
    BlockChecksum = 1 << 0,

    /// <summary>
    /// Read each output file back and compare a hash of every key and value
    /// against what was written into it.
    /// </summary>
    /// <remarks>The most thorough and the most expensive of the three.</remarks>
    Iteration = 1 << 1,

    /// <summary>Verify the file-level checksum of each output file.</summary>
    FileChecksum = 1 << 2,

    /// <summary>Run the selected verifications on compactions this process performs.</summary>
    EnableForLocalCompaction = 1 << 10,

    /// <summary>Run the selected verifications on compactions performed remotely.</summary>
    EnableForRemoteCompaction = 1 << 11,

    /// <summary>Every verification, on every kind of compaction.</summary>
    /// <remarks>
    /// Every bit, including ones RocksDb has not defined yet, so a later
    /// version may make this mean more than it does today.
    /// </remarks>
    All = 0xFFFFFFFF,
}

/// <summary>WAL recovery mode.</summary>
public enum WalRecoveryMode
{
    /// <summary>
    /// Tolerates an incomplete final record in any log, which is what a crash
    /// mid-write leaves behind, and refuses to open if corruption is found
    /// anywhere else.
    /// </summary>
    /// <remarks>
    /// Choose this when an applied update must never be rolled back, even by
    /// a crash. The difference from <see cref="PointInTime"/> is what happens
    /// on real corruption: this refuses to open, rather than recovering up to
    /// the damage and discarding the rest.
    /// </remarks>
    TolerateCorruptedTailRecords = 0,

    /// <summary>
    /// Expects a clean shutdown and treats any corruption at all, including an
    /// incomplete tail record, as a failure to open.
    /// </summary>
    /// <remarks>
    /// The strictest mode. Good for tests and for applications that would
    /// rather fail loudly than start with less data than they wrote.
    /// </remarks>
    AbsoluteConsistency = 1,

    /// <summary>
    /// Replays the log until the first inconsistency and stops there,
    /// discarding everything after it. RocksDb's default.
    /// </summary>
    /// <remarks>
    /// The recovered state is a real point in time, so it is consistent, but
    /// writes after the damage are silently lost. Suited to storage that can
    /// lose a tail of writes, such as a disk with a volatile write cache.
    /// </remarks>
    PointInTime = 2,

    /// <summary>
    /// Ignores corruption wherever it appears and salvages whatever records
    /// can still be read.
    /// </summary>
    /// <remarks>
    /// A last resort. The result is not a point in time: records from after a
    /// corrupt stretch can be applied while earlier ones are lost, so the
    /// database can come back in a state no writer ever produced. Use it to
    /// rescue data, not to run on.
    /// </remarks>
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

    /// <summary>
    /// Creates a copy of this options object that shares its attached callback
    /// objects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a deep copy, which is what this used to claim. The native call behind
    /// it copies the options struct, so the comparator, compaction filter, env
    /// and WAL filter are copied as pointers and the merge operator, rate
    /// limiter, logger and listeners as shared references. Both objects end up
    /// pointing at the same callback instances.
    /// </para>
    /// <para>
    /// The clone therefore registers itself as another holder of each of them,
    /// so disposing either options object no longer destroys what the other, or
    /// a database opened from it, is still calling. Before that, the clone's
    /// owned-handle set was empty and the original's disposal took the
    /// comparator and logger with it.
    /// </para>
    /// </remarks>
    public DbOptions Clone()
    {
        var clone = new DbOptions(NativeMethods.rocksdb_options_create_copy(Handle));

        foreach (RocksDbHandle handle in _ownedHandles)
        {
            handle.AddHolder();
            clone._ownedHandles.Add(handle);
        }

        return clone;
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

    /// <summary>
    /// If true, allow ingesting files below the existing data. Deprecated by
    /// RocksDb; prefer <see cref="CfAllowIngestBehind"/>.
    /// </summary>
    /// <remarks>
    /// This is the old database-wide form. RocksDb has deprecated it in favour
    /// of the per-column-family setting, so use
    /// <see cref="CfAllowIngestBehind"/> for new code.
    /// </remarks>
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
            value.AddHolder();
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

    /// <summary>
    /// Size cap in MB on the <em>archive</em> of obsolete write-ahead logs.
    /// Once the archive exceeds it, the oldest archived logs are deleted until
    /// it fits.
    /// </summary>
    /// <remarks>
    /// This governs deletion, not archiving. Archiving is what happens when
    /// either this or <see cref="WalTtlSeconds"/> is non-zero: while both are
    /// zero, an obsolete log is deleted immediately and never archived at all.
    /// </remarks>
    public ulong WalSizeLimitMb
    {
        get => NativeMethods.rocksdb_options_get_WAL_size_limit_MB(Handle);
        set => NativeMethods.rocksdb_options_set_WAL_size_limit_MB(Handle, value);
    }

    /// <summary>
    /// Same as <see cref="BytesPerSync"/> but for write-ahead log files. Zero
    /// turns incremental syncing off, which is the default.
    /// </summary>
    /// <remarks>
    /// Zero means off, not "sync after every write". For durability per write,
    /// use <see cref="WriteOptions.Sync"/> instead.
    /// </remarks>
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
    /// <remarks>
    /// Applies to the logger RocksDb creates for itself. It does not filter a
    /// logger supplied through <see cref="InfoLog"/>: measured over a database
    /// open, write and flush, setting this to <see cref="InfoLogLevel.Warn"/>
    /// changed neither the number of messages a custom logger received nor
    /// their levels. The level such a logger is constructed with is the only
    /// one that has any effect on it, and even that lets through messages
    /// RocksDb logs without a level. See issue #129.
    /// </remarks>
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

    /// <summary>
    /// Asks the operating system to sync this many bytes of an SST file
    /// incrementally while it is being written. Zero turns incremental syncing
    /// off, which is the default.
    /// </summary>
    /// <remarks>
    /// Zero means off, not "sync everything". The point of the setting is to
    /// spread write-back over time so a large file does not arrive at the disk
    /// in one burst at the end. Enabling a rate limiter raises this to 1 MB on
    /// its own. Does not apply to write-ahead log files; see
    /// <see cref="WalBytesPerSync"/> for those.
    /// </remarks>
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

    /// <summary>Time-to-live for data in seconds.</summary>
    /// <remarks>
    /// What expiry means depends on the compaction style, and only one of them
    /// deletes anything. Under FIFO compaction, files older than this are
    /// dropped. Under level and universal compaction, reaching this age only
    /// schedules the file to be rewritten, which refreshes it rather than
    /// removing its entries. Use a <see cref="CompactionFilter"/> if you need
    /// entries themselves to expire.
    /// </remarks>
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
    public bool EnableBlobGarbageCollection
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
    /// <remarks>
    /// RocksDb copies the shared pointer, so the limiter may be shared between
    /// options objects and reused by a database opened later. Assigning
    /// registers no hold, exactly as a cache does not: destroying the handle
    /// only drops this library's reference, and RocksDb's own copy keeps the
    /// limiter alive for as long as it needs it.
    /// </remarks>
    public RateLimiter RateLimiter
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_ratelimiter(Handle, value.Handle);
        }
    }

    /// <summary>
    /// A cache for blob values, separate from the block cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only meaningful with <see cref="EnableBlobFiles"/>. Blobs live outside
    /// the SST files, so the block cache never holds them and a blob read goes
    /// to the file system every time until one of these exists. Giving blobs
    /// their own cache also keeps them from evicting index and filter blocks,
    /// which is the reason it is a separate cache rather than a share of the
    /// block cache.
    /// </para>
    /// <para>
    /// <see cref="PrepopulateBlobCache"/> does nothing without this. There is
    /// no cache to prepopulate, so a flush has nowhere to put the blobs it
    /// just wrote.
    /// </para>
    /// <para>
    /// RocksDb copies the shared pointer, so the cache may be shared with other
    /// options objects and with the block cache, and reused by a database opened
    /// later. Assigning registers no hold, exactly as
    /// <see cref="BlockBasedTableOptions.SetBlockCache"/> does not: destroying
    /// the handle only drops this library's reference, and RocksDb's own copy
    /// keeps the cache alive for as long as it needs it. Disposing the cache
    /// under a live database is therefore safe and immediate, verified over two
    /// hundred reads and a compaction after the fact.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// The value is <see langword="null"/>. Unlike
    /// <see cref="BlockBasedTableOptions.SetBlockCache"/>, which the C API lets
    /// through as a no-op, the blob-cache setter dereferences what it is given
    /// without checking, so a null would be an access violation rather than
    /// nothing happening.
    /// </exception>
    public Cache BlobCache
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_blob_cache(Handle, value.Handle);
        }
    }

    /// <summary>
    /// Attaches a disk-space governor, capping how much space the database may
    /// use and how fast it may delete files.
    /// </summary>
    /// <remarks>
    /// RocksDb takes a shared reference rather than ownership, so the instance
    /// may be disposed once assigned, and the same one may be given to several
    /// databases to place them under a common budget. That is why it is not
    /// added to the owned handles.
    /// </remarks>
    public SstFileManager SstFileManager
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_sst_file_manager(Handle, value.Handle);
        }
    }

    /// <summary>
    /// Aligns SST file boundaries with key prefixes.
    /// </summary>
    /// <remarks>
    /// Without one, a compaction splits files wherever the size target falls, so
    /// a prefix's data can straddle several files and each file can hold several
    /// prefixes. That blunts prefix-scoped work: a range delete cannot drop whole
    /// files and a prefix scan reads more of them than it needs. RocksDb takes a
    /// shared reference, so the factory may be disposed once assigned.
    /// </remarks>
    public SstPartitionerFactory SstPartitionerFactory
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_sst_partitioner_factory(Handle, value.Handle);
        }
    }

    /// <summary>
    /// Spreads the database across several directories, each with a size
    /// target.
    /// </summary>
    /// <param name="paths">
    /// The directories, in the order RocksDb should fill them. The last should
    /// be the one with room to spare, since data overflows forward.
    /// </param>
    /// <remarks>
    /// The usual reason is mixed storage: give a fast device a modest target so
    /// the newest levels live there, and let the rest overflow onto slower,
    /// larger media. RocksDb copies the values, so the
    /// <see cref="DbPath"/> objects stay yours to dispose.
    /// </remarks>
    public unsafe DbOptions SetDbPaths(IReadOnlyList<DbPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
        {
            throw new ArgumentException("At least one path is required.", nameof(paths));
        }

        nint[] handles = new nint[paths.Count];
        for (int i = 0; i < paths.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(paths[i]);
            handles[i] = paths[i].Handle;
        }

        fixed (nint* p = handles)
            NativeMethods.rocksdb_options_set_db_paths(Handle, p, (nuint)paths.Count);

        return this;
    }

    /// <summary>
    /// Parses RocksDb's own options syntax and applies it on top of these
    /// options.
    /// </summary>
    /// <param name="optionsString">
    /// Settings in RocksDb's <c>name=value;name=value</c> form, as accepted by
    /// its own tools.
    /// </param>
    /// <returns>A new options object: this one is left unchanged.</returns>
    /// <remarks>
    /// For configuration-driven callers that would rather carry a string than a
    /// list of property assignments. Unknown or malformed settings throw rather
    /// than being ignored.
    /// </remarks>
    public unsafe DbOptions WithOptionsFromString(string optionsString)
    {
        ArgumentException.ThrowIfNullOrEmpty(optionsString);

        var result = new DbOptions();
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(optionsString + '\0');
            nint err = default;

            fixed (byte* s = bytes)
                NativeMethods.rocksdb_get_options_from_string(Handle, s, result.Handle, ref err);

            NativeMethods.ThrowOnError(err);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Attaches tuning for universal compaction.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="CompactionStyle"/> is
    /// <see cref="RocksDbNet.CompactionStyle.Universal"/>. RocksDb copies the
    /// values, so the instance may be disposed immediately afterwards.
    /// </remarks>
    public UniversalCompactionOptions UniversalCompactionOptions
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_universal_compaction_options(Handle, value.Handle);
        }
    }

    /// <summary>
    /// Attaches tuning for FIFO compaction.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="CompactionStyle"/> is
    /// <see cref="RocksDbNet.CompactionStyle.Fifo"/>. RocksDb copies the values,
    /// so the instance may be disposed immediately afterwards.
    /// </remarks>
    public FifoCompactionOptions FifoCompactionOptions
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_fifo_compaction_options(Handle, value.Handle);
        }
    }

    /// <summary>
    /// Attaches a memtable memory budget shared across column families, and
    /// across databases if the same instance is given to each.
    /// </summary>
    /// <remarks>
    /// <see cref="WriteBufferSize"/> bounds one memtable; this bounds their
    /// total. RocksDb takes a shared reference rather than ownership, so the
    /// instance may be disposed once assigned.
    /// </remarks>
    public WriteBufferManager WriteBufferManager
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.rocksdb_options_set_write_buffer_manager(Handle, value.Handle);
        }
    }

    /// <summary>Attaches a prefix extractor (slice transform).</summary>

    public SliceTransform PrefixExtractor
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            // Checked before the native call, so a rejected second attachment
            // does not leave RocksDb holding a pointer it believes it owns.
            value.AttachExclusively(nameof(PrefixExtractor));
            NativeMethods.rocksdb_options_set_prefix_extractor(Handle, value.Handle);
        }
    }

    // ── Compaction filter ──────────────────────────────────

    /// <summary>
    /// Attaches a compaction filter. The filter is invoked for every key-value
    /// pair during table-file creation (compaction and flush).
    /// </summary>
    /// <remarks>
    /// Disposing the filter is safe at any point. Attaching it registers a
    /// hold, so a <c>using</c> block that ends while the database is still open
    /// defers the release rather than performing it, and the native object goes
    /// when the last holder lets go. See the ownership guide.
    /// </remarks>

    public CompactionFilter CompactionFilter
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            value.AddHolder();
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
            // Checked before the native call, so a rejected second attachment
            // does not leave RocksDb holding a pointer it believes it owns.
            value.AttachExclusively(nameof(CompactionFilterFactory));
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
            // Checked before the native call, so a rejected second attachment
            // does not leave RocksDb holding a pointer it believes it owns.
            value.AttachExclusively(nameof(MergeOperator));
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
            value.AddHolder();
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
            value.AddHolder();
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

        filter.AddHolder();
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
    /// <remarks>
    /// <para>
    /// Adds, and never removes or replaces. Call it twice and both listeners
    /// receive every event; RocksDb offers no way to take one back off. This
    /// was a property setter, which made a call that accumulates look like an
    /// assignment that replaces, so <c>options.EventListener = a;</c> followed
    /// by <c>options.EventListener = b;</c> left both installed and no way to
    /// undo it.
    /// </para>
    /// <para>
    /// Ownership of the listener transfers to these options.
    /// </para>
    /// </remarks>
    /// <param name="listener">The listener to add.</param>
    /// <returns>These options, for chaining.</returns>
    public DbOptions AddEventListener(EventListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        listener.AttachExclusively(nameof(AddEventListener));
        NativeMethods.rocksdb_options_add_eventlistener(Handle, listener.Handle);

        return this;
    }

    /// <summary>Adds several event listeners.</summary>
    /// <remarks>Adds, as <see cref="AddEventListener"/> does.</remarks>
    /// <param name="listeners">The listeners to add.</param>
    /// <returns>These options, for chaining.</returns>
    public DbOptions AddEventListeners(IEnumerable<EventListener> listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);

        foreach (EventListener listener in listeners)
        {
            AddEventListener(listener);
        }

        return this;
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

    /// <summary>Returns the current value of a counter from the statistics subsystem.</summary>
    /// <param name="ticker">Which counter to read.</param>
    /// <remarks>
    /// Zero unless <see cref="EnableStatistics"/> was called before the database
    /// was opened. This took a bare <see cref="uint"/>, so callers passed the
    /// numeric value of a counter they had to look up in RocksDb's header.
    /// </remarks>
    public ulong GetTickerCount(Ticker ticker)
        => NativeMethods.rocksdb_options_statistics_get_ticker_count(Handle, (uint)ticker);

    /// <summary>
    /// Returns histogram data for a statistics histogram type. Returns
    /// all-zero data, not <see langword="null"/>, when no statistics object is
    /// attached.
    /// </summary>
    /// <remarks>
    /// An all-zero result is therefore ambiguous: it means either "no samples
    /// recorded" or "statistics were never enabled". Attach a statistics object
    /// with <see cref="EnableStatistics"/> before relying on the numbers.
    /// </remarks>
    /// <param name="histogram">Which distribution to read.</param>
    public HistogramData? GetHistogramData(Histogram histogram)
    {
        nint dataHandle = NativeMethods.rocksdb_statistics_histogram_data_create();

        // Kept as it was: the native accessor takes the id as a plain integer.
        if (dataHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            NativeMethods.rocksdb_options_statistics_get_histogram_data(Handle, (uint)histogram, dataHandle);
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

    // ── Table properties collectors ─────────────────────────────────────────

    // ── Previously unreachable settings ─────────────────────────────────────

    /// <summary>
    /// Whether to hint the operating system that reads will be random when the
    /// database opens. Default is <see langword="true"/>.
    /// </summary>
    public bool AdviseRandomOnOpen
    {
        get => NativeMethods.rocksdb_options_get_advise_random_on_open(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_advise_random_on_open(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Block size the memtable allocates in, in bytes. Zero, the default, lets
    /// RocksDb derive it from <see cref="WriteBufferSize"/>.
    /// </summary>
    /// <remarks>
    /// RocksDb keeps this in a <c>size_t</c>. Every size option on this library
    /// is <see cref="ulong"/> regardless, so the same concept has one type
    /// everywhere rather than <see cref="ulong"/> on some members and
    /// <see cref="nuint"/> on others. On a 32-bit process a value above
    /// <see cref="uint.MaxValue"/> cannot be represented and the setter throws
    /// <see cref="OverflowException"/> rather than truncating to something
    /// smaller than asked for.
    /// </remarks>
    public ulong ArenaBlockSize
    {
        get => (ulong)NativeMethods.rocksdb_options_get_arena_block_size(Handle);
        set => NativeMethods.rocksdb_options_set_arena_block_size(Handle, checked((nuint)value));
    }

    /// <summary>
    /// Whether background threads should defer slow work such as deleting
    /// obsolete files, rather than doing it inline. Default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// For latency-sensitive callers. When enabled it overrides
    /// <see cref="ReadOptions.BackgroundPurgeOnIteratorCleanup"/>.
    /// </remarks>
    public bool AvoidUnnecessaryBlockingIo
    {
        get => NativeMethods.rocksdb_options_get_avoid_unnecessary_blocking_io(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_avoid_unnecessary_blocking_io(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Readahead size used when a compaction reads blob files, in bytes.</summary>
    public ulong BlobCompactionReadaheadSize
    {
        get => NativeMethods.rocksdb_options_get_blob_compaction_readahead_size(Handle);
        set => NativeMethods.rocksdb_options_set_blob_compaction_readahead_size(Handle, value);
    }

    /// <summary>Compression applied to blob files.</summary>
    public Compression BlobCompression
    {
        get => (Compression)NativeMethods.rocksdb_options_get_blob_compression_type(Handle);
        set => NativeMethods.rocksdb_options_set_blob_compression_type(Handle, (int)value);
    }

    /// <summary>
    /// The lowest level at which values are written to blob files rather than
    /// inline. Default is zero, meaning every level.
    /// </summary>
    /// <remarks>
    /// Raising this keeps the hot upper levels inline, where the extra
    /// indirection of a blob read costs most.
    /// </remarks>
    public int BlobFileStartingLevel
    {
        get => NativeMethods.rocksdb_options_get_blob_file_starting_level(Handle);
        set => NativeMethods.rocksdb_options_set_blob_file_starting_level(Handle, value);
    }

    /// <summary>
    /// The fraction of the oldest blob files that garbage collection considers,
    /// between 0 and 1.
    /// </summary>
    public double BlobGarbageCollectionAgeCutoff
    {
        get => NativeMethods.rocksdb_options_get_blob_gc_age_cutoff(Handle);
        set => NativeMethods.rocksdb_options_set_blob_gc_age_cutoff(Handle, value);
    }

    /// <summary>
    /// The garbage fraction at which a blob file is collected regardless of its
    /// age, between 0 and 1.
    /// </summary>
    public double BlobGarbageCollectionForceThreshold
    {
        get => NativeMethods.rocksdb_options_get_blob_gc_force_threshold(Handle);
        set => NativeMethods.rocksdb_options_set_blob_gc_force_threshold(Handle, value);
    }

    /// <summary>
    /// How many cache lines a Bloom filter probe is confined to. Zero, the
    /// default, spreads probes across the filter.
    /// </summary>
    /// <remarks>
    /// A non-zero value trades a slightly higher false-positive rate for fewer
    /// cache misses per lookup.
    /// </remarks>
    public uint BloomLocality
    {
        get => NativeMethods.rocksdb_options_get_bloom_locality(Handle);
        set => NativeMethods.rocksdb_options_set_bloom_locality(Handle, value);
    }

    /// <summary>
    /// Whether the bottommost level's zstd dictionary is trained rather than
    /// sampled.
    /// </summary>
    /// <remarks>
    /// Read-only because the native setter takes a second argument that this
    /// getter cannot report. Use
    /// <see cref="SetBottommostCompressionOptionsUseZstdDictTrainer"/>.
    /// </remarks>
    public bool BottommostCompressionOptionsUseZstdDictTrainer
        => NativeMethods.rocksdb_options_get_bottommost_compression_options_use_zstd_dict_trainer(Handle) != 0;

    /// <summary>
    /// Sets whether the bottommost level's zstd dictionary is trained, and
    /// whether the bottommost compression options apply at all.
    /// </summary>
    /// <param name="useZstdDictTrainer">
    /// Train the dictionary rather than sampling it.
    /// </param>
    /// <param name="enabled">
    /// Whether the bottommost compression options are used. Setting the first
    /// argument has no effect while this is false.
    /// </param>
    /// <remarks>
    /// A method rather than a property because the native setter writes two
    /// fields at once, and the matching getter reports only the first.
    /// </remarks>
    public DbOptions SetBottommostCompressionOptionsUseZstdDictTrainer(bool useZstdDictTrainer, bool enabled)
    {
        NativeMethods.rocksdb_options_set_bottommost_compression_options_use_zstd_dict_trainer(
            Handle, useZstdDictTrainer ? (byte)1 : (byte)0, enabled ? (byte)1 : (byte)0);

        return this;
    }

    /// <summary>
    /// How RocksDb chooses the next file to compact within a level. Default is
    /// <see cref="RocksDbNet.CompactionPri.MinOverlappingRatio"/>.
    /// </summary>
    public CompactionPri CompactionPri
    {
        get => (CompactionPri)NativeMethods.rocksdb_options_get_compaction_pri(Handle);
        set => NativeMethods.rocksdb_options_set_compaction_pri(Handle, (int)value);
    }

    /// <summary>
    /// Maximum bytes buffered while building a compression dictionary. Zero
    /// disables the limit.
    /// </summary>
    public ulong CompressionOptionsMaxDictBufferBytes
    {
        get => NativeMethods.rocksdb_options_get_compression_options_max_dict_buffer_bytes(Handle);
        set => NativeMethods.rocksdb_options_set_compression_options_max_dict_buffer_bytes(Handle, value);
    }

    /// <summary>Threads a single block's compression may use.</summary>
    public int CompressionOptionsParallelThreads
    {
        get => NativeMethods.rocksdb_options_get_compression_options_parallel_threads(Handle);
        set => NativeMethods.rocksdb_options_set_compression_options_parallel_threads(Handle, value);
    }

    /// <summary>
    /// Whether the zstd dictionary is trained rather than sampled. Training
    /// costs more to build and usually compresses better.
    /// </summary>
    public bool CompressionOptionsUseZstdDictTrainer
    {
        get => NativeMethods.rocksdb_options_get_compression_options_use_zstd_dict_trainer(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_compression_options_use_zstd_dict_trainer(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Bytes of sample data used to train a zstd dictionary.</summary>
    public int CompressionOptionsZstdMaxTrainBytes
    {
        get => NativeMethods.rocksdb_options_get_compression_options_zstd_max_train_bytes(Handle);
        set => NativeMethods.rocksdb_options_set_compression_options_zstd_max_train_bytes(Handle, value);
    }

    /// <summary>
    /// How often obsolete files are swept, in microseconds. Zero disables the
    /// periodic sweep, leaving deletion to happen alongside compaction.
    /// </summary>
    public ulong DeleteObsoleteFilesPeriodMicros
    {
        get => NativeMethods.rocksdb_options_get_delete_obsolete_files_period_micros(Handle);
        set => NativeMethods.rocksdb_options_set_delete_obsolete_files_period_micros(Handle, value);
    }

    /// <summary>
    /// Whether the write-ahead log write and the memtable insert run on
    /// separate threads. Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Raises write throughput under concurrency at the cost of some latency on
    /// an individual write.
    /// </remarks>
    public bool EnablePipelinedWrite
    {
        get => NativeMethods.rocksdb_options_get_enable_pipelined_write(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_enable_pipelined_write(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether writer threads spin briefly before yielding. Default is
    /// <see langword="true"/>.
    /// </summary>
    public bool EnableWriteThreadAdaptiveYield
    {
        get => NativeMethods.rocksdb_options_get_enable_write_thread_adaptive_yield(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_enable_write_thread_adaptive_yield(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Threshold for the experimental memtable purge, as a multiple of the
    /// write buffer size. Zero disables it.
    /// </summary>
    /// <remarks>
    /// RocksDb marks this experimental. It discards memtable entries that later
    /// writes have already superseded, avoiding a flush.
    /// </remarks>
    public double ExperimentalMempurgeThreshold
    {
        get => NativeMethods.rocksdb_options_get_experimental_mempurge_threshold(Handle);
        set => NativeMethods.rocksdb_options_set_experimental_mempurge_threshold(Handle, value);
    }

    /// <summary>
    /// Pending compaction bytes at which writes are stopped outright, rather
    /// than slowed. Zero disables the limit.
    /// </summary>
    /// <remarks>
    /// The harder counterpart to
    /// <see cref="SoftPendingCompactionBytesLimit"/>. Reaching this means
    /// compaction has fallen far enough behind that RocksDb would rather block
    /// writers than let the backlog grow.
    /// </remarks>
    public ulong HardPendingCompactionBytesLimit
    {
        get => (ulong)NativeMethods.rocksdb_options_get_hard_pending_compaction_bytes_limit(Handle);
        set => NativeMethods.rocksdb_options_set_hard_pending_compaction_bytes_limit(Handle, checked((nuint)value));
    }

    /// <summary>
    /// How many locks guard in-place memtable updates. Only used when
    /// <see cref="InplaceUpdateSupport"/> is enabled.
    /// </summary>
    public ulong InplaceUpdateNumLocks
    {
        get => (ulong)NativeMethods.rocksdb_options_get_inplace_update_num_locks(Handle);
        set => NativeMethods.rocksdb_options_set_inplace_update_num_locks(Handle, checked((nuint)value));
    }

    /// <summary>
    /// Whether a write may overwrite an existing memtable entry in place rather
    /// than appending a new version. Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Saves memory for workloads that overwrite the same keys repeatedly, but
    /// it is incompatible with snapshots and merge operators, because the
    /// superseded version is gone.
    /// </remarks>
    public bool InplaceUpdateSupport
    {
        get => NativeMethods.rocksdb_options_get_inplace_update_support(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_inplace_update_support(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether file descriptors are closed in child processes. Default is
    /// <see langword="true"/>.
    /// </summary>
    public bool IsFdCloseOnExec
    {
        get => NativeMethods.rocksdb_options_get_is_fd_close_on_exec(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_is_fd_close_on_exec(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// How often the info log is rolled, in seconds. Zero disables time-based
    /// rolling, leaving only the size limit.
    /// </summary>
    public ulong LogFileTimeToRoll
    {
        get => (ulong)NativeMethods.rocksdb_options_get_log_file_time_to_roll(Handle);
        set => NativeMethods.rocksdb_options_set_log_file_time_to_roll(Handle, checked((nuint)value));
    }

    /// <summary>Bytes preallocated for the manifest file.</summary>
    public ulong ManifestPreallocationSize
    {
        get => (ulong)NativeMethods.rocksdb_options_get_manifest_preallocation_size(Handle);
        set => NativeMethods.rocksdb_options_set_manifest_preallocation_size(Handle, checked((nuint)value));
    }

    /// <summary>Threads used to open files when the database starts.</summary>
    public int MaxFileOpeningThreads
    {
        get => NativeMethods.rocksdb_options_get_max_file_opening_threads(Handle);
        set => NativeMethods.rocksdb_options_set_max_file_opening_threads(Handle, value);
    }

    /// <summary>
    /// Size at which the manifest is rolled, in bytes.
    /// </summary>
    /// <remarks>
    /// The manifest records every change to the file set, so it grows with
    /// activity rather than with data. Left unbounded it can become the largest
    /// thing in the directory.
    /// </remarks>
    public ulong MaxManifestFileSize
    {
        get => (ulong)NativeMethods.rocksdb_options_get_max_manifest_file_size(Handle);
        set => NativeMethods.rocksdb_options_set_max_manifest_file_size(Handle, checked((nuint)value));
    }

    /// <summary>
    /// How many superseded versions of a key an iterator skips before it
    /// reseeks rather than stepping.
    /// </summary>
    public ulong MaxSequentialSkipInIterations
    {
        get => NativeMethods.rocksdb_options_get_max_sequential_skip_in_iterations(Handle);
        set => NativeMethods.rocksdb_options_set_max_sequential_skip_in_iterations(Handle, value);
    }

    /// <summary>
    /// How many merge operands for one key accumulate in the memtable before
    /// they are combined eagerly. Zero, the default, never combines early.
    /// </summary>
    /// <remarks>
    /// Bounds the cost of reading a key that has been merged many times, at the
    /// price of doing merge work on the write path.
    /// </remarks>
    public ulong MaxSuccessiveMerges
    {
        get => (ulong)NativeMethods.rocksdb_options_get_max_successive_merges(Handle);
        set => NativeMethods.rocksdb_options_set_max_successive_merges(Handle, checked((nuint)value));
    }

    /// <summary>
    /// Average operations scanned per memtable entry above which a flush is
    /// triggered. Zero disables it.
    /// </summary>
    public uint MemtableAvgOpScanFlushTrigger
    {
        get => NativeMethods.rocksdb_options_get_memtable_avg_op_scan_flush_trigger(Handle);
        set => NativeMethods.rocksdb_options_set_memtable_avg_op_scan_flush_trigger(Handle, value);
    }

    /// <summary>
    /// Huge page size to allocate the memtable with, in bytes. Zero, the
    /// default, uses ordinary pages.
    /// </summary>
    public ulong MemtableHugePageSize
    {
        get => (ulong)NativeMethods.rocksdb_options_get_memtable_huge_page_size(Handle);
        set => NativeMethods.rocksdb_options_set_memtable_huge_page_size(Handle, checked((nuint)value));
    }

    /// <summary>
    /// Operations scanned in the memtable above which a flush is triggered.
    /// Zero disables it.
    /// </summary>
    public uint MemtableOpScanFlushTrigger
    {
        get => NativeMethods.rocksdb_options_get_memtable_op_scan_flush_trigger(Handle);
        set => NativeMethods.rocksdb_options_set_memtable_op_scan_flush_trigger(Handle, value);
    }

    /// <summary>
    /// Whether files are opened asynchronously when the database starts.
    /// Default is <see langword="false"/>.
    /// </summary>
    public bool OpenFilesAsync
    {
        get => NativeMethods.rocksdb_options_get_open_files_async(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_open_files_async(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether Bloom filters are omitted from the bottommost level. Default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Worth enabling when almost every read finds its key. The bottommost
    /// level holds most of the data, so its filters are most of the filter
    /// memory, and a filter only pays for itself on lookups that miss.
    /// </para>
    /// <para>
    /// The native setter takes an int while its getter returns a byte. Both are
    /// treated as a boolean here, which is what RocksDb means by them.
    /// </para>
    /// </remarks>
    public bool OptimizeFiltersForHits
    {
        get => NativeMethods.rocksdb_options_get_optimize_filters_for_hits(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_optimize_filters_for_hits(Handle, value ? 1 : 0);
    }

    /// <summary>
    /// Whether newly written blobs are put straight into the blob cache.
    /// Default is <see cref="RocksDbNet.PrepopulateBlobCache.Disable"/>.
    /// </summary>
    public PrepopulateBlobCache PrepopulateBlobCache
    {
        get => (PrepopulateBlobCache)NativeMethods.rocksdb_options_get_prepopulate_blob_cache(Handle);
        set => NativeMethods.rocksdb_options_set_prepopulate_blob_cache(Handle, (int)value);
    }

    /// <summary>
    /// How many write-ahead log files are kept and reused rather than deleted
    /// and recreated. Zero, the default, recreates them.
    /// </summary>
    /// <remarks>
    /// Reusing a file avoids the filesystem metadata work of creating one, which
    /// shows up on write-heavy workloads with frequent log rolls.
    /// </remarks>
    public ulong RecycleLogFileNum
    {
        get => (ulong)NativeMethods.rocksdb_options_get_recycle_log_file_num(Handle);
        set => NativeMethods.rocksdb_options_set_recycle_log_file_num(Handle, checked((nuint)value));
    }

    /// <summary>
    /// Whether background I/O is accounted per operation. Default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// As with <see cref="OptimizeFiltersForHits"/>, the native setter takes an
    /// int and its getter returns a byte; both mean a boolean.
    /// </remarks>
    public bool ReportBgIoStats
    {
        get => NativeMethods.rocksdb_options_get_report_bg_io_stats(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_report_bg_io_stats(Handle, value ? 1 : 0);
    }

    /// <summary>
    /// Whether opening the database skips gathering file statistics. Default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Speeds up opening a database with many files, at the cost of compaction
    /// making worse decisions until the statistics are rebuilt.
    /// </remarks>
    public bool SkipStatsUpdateOnDbOpen
    {
        get => NativeMethods.rocksdb_options_get_skip_stats_update_on_db_open(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_skip_stats_update_on_db_open(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Pending compaction bytes at which writes start being slowed down. Zero
    /// disables the limit.
    /// </summary>
    /// <remarks>
    /// The gentler counterpart to
    /// <see cref="HardPendingCompactionBytesLimit"/>: writers are throttled so
    /// that compaction can catch up, rather than stopped.
    /// </remarks>
    public ulong SoftPendingCompactionBytesLimit
    {
        get => (ulong)NativeMethods.rocksdb_options_get_soft_pending_compaction_bytes_limit(Handle);
        set => NativeMethods.rocksdb_options_set_soft_pending_compaction_bytes_limit(Handle, checked((nuint)value));
    }

    /// <summary>
    /// How much detail statistics collect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Call <see cref="EnableStatistics"/> first.</b> RocksDb keeps the level
    /// on the statistics object rather than on the options, so both the setter
    /// and the getter here are silent no-ops until one exists: setting does
    /// nothing and reading returns <see cref="StatsLevel.DisableAll"/> whatever
    /// was assigned.
    /// </para>
    /// <para>
    /// Values outside the enum are clamped natively rather than rejected.
    /// </para>
    /// </remarks>
    public StatsLevel StatisticsLevel
    {
        get => (StatsLevel)NativeMethods.rocksdb_options_get_statistics_level(Handle);
        set => NativeMethods.rocksdb_options_set_statistics_level(Handle, (int)value);
    }

    /// <summary>
    /// How often statistics are persisted to the in-memory history buffer, in
    /// seconds. Zero disables it. Defaults to 600.
    /// </summary>
    /// <remarks>
    /// Not the info-log dump, which is <see cref="StatsDumpPeriodSec"/>. This
    /// one feeds the statistics history RocksDb keeps in memory.
    /// </remarks>
    public uint StatsPersistPeriodSec
    {
        get => NativeMethods.rocksdb_options_get_stats_persist_period_sec(Handle);
        set => NativeMethods.rocksdb_options_set_stats_persist_period_sec(Handle, value);
    }

    /// <summary>
    /// Base-2 logarithm of the number of shards in the table cache. Negative
    /// lets RocksDb choose.
    /// </summary>
    public int TableCacheNumShardBits
    {
        get => NativeMethods.rocksdb_options_get_table_cache_numshardbits(Handle);
        set => NativeMethods.rocksdb_options_set_table_cache_numshardbits(Handle, value);
    }

    /// <summary>
    /// Factor by which the target file size grows with each level. Default is
    /// 1, meaning every level uses the same file size.
    /// </summary>
    public int TargetFileSizeMultiplier
    {
        get => NativeMethods.rocksdb_options_get_target_file_size_multiplier(Handle);
        set => NativeMethods.rocksdb_options_set_target_file_size_multiplier(Handle, value);
    }

    /// <summary>
    /// Whether write-ahead log files are recorded in the manifest and verified
    /// on recovery. Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Catches a log file that has gone missing, which would otherwise be
    /// indistinguishable from one that never existed.
    /// </remarks>
    public bool TrackAndVerifyWalsInManifest
    {
        get => NativeMethods.rocksdb_options_get_track_and_verify_wals_in_manifest(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_track_and_verify_wals_in_manifest(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether writes may be applied out of order. Default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Raises write throughput but weakens the guarantees: snapshots and
    /// read-your-own-writes no longer hold as they otherwise would. Read
    /// RocksDb's own notes before enabling it.
    /// </remarks>
    public bool UnorderedWrite
    {
        get => NativeMethods.rocksdb_options_get_unordered_write(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_unordered_write(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether mutexes spin before sleeping. Default is
    /// <see langword="false"/>.
    /// </summary>
    public bool UseAdaptiveMutex
    {
        get => NativeMethods.rocksdb_options_get_use_adaptive_mutex(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_use_adaptive_mutex(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Largest buffer used when writing a file, in bytes.</summary>
    public ulong WritableFileMaxBufferSize
    {
        get => NativeMethods.rocksdb_options_get_writable_file_max_buffer_size(Handle);
        set => NativeMethods.rocksdb_options_set_writable_file_max_buffer_size(Handle, value);
    }

    /// <summary>
    /// Whether the database's unique identifier is stored in the manifest.
    /// Default is <see langword="true"/>, which is what RocksDb prefers.
    /// </summary>
    public bool WriteDbIdToManifest
    {
        get => NativeMethods.rocksdb_options_get_write_dbid_to_manifest(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_write_dbid_to_manifest(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether the database's unique identifier is also written to a separate
    /// IDENTITY file, which RocksDb keeps for compatibility.
    /// </summary>
    public bool WriteIdentityFile
    {
        get => NativeMethods.rocksdb_options_get_write_identity_file(Handle) != 0;
        set => NativeMethods.rocksdb_options_set_write_identity_file(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Marks an SST file for compaction when it accumulates too many
    /// tombstones, so that deleted data is reclaimed sooner than the ordinary
    /// compaction schedule would manage.
    /// </summary>
    /// <param name="windowSize">
    /// How many consecutive entries the sliding window covers. A file is marked
    /// when any window of this many entries holds at least
    /// <paramref name="deletionTrigger"/> deletions.
    /// </param>
    /// <param name="deletionTrigger">
    /// How many deletions within a window trigger the mark.
    /// </param>
    /// <param name="deletionRatio">
    /// An additional whole-file test: a file whose deleted fraction reaches this
    /// is marked regardless of how the deletions are distributed. Zero, the
    /// default, disables it.
    /// </param>
    /// <param name="minFileSize">
    /// Files smaller than this are exempt from the
    /// <paramref name="deletionRatio"/> test. Zero, the default, exempts none.
    /// </param>
    /// <remarks>
    /// <para>
    /// Aimed at workloads that delete in bursts, such as queues and anything
    /// with a time-to-live. Without it, a file full of tombstones sits until
    /// compaction reaches it on size grounds, and every read through that key
    /// range pays for walking them.
    /// </para>
    /// <para>
    /// Marking a file makes it eligible; RocksDb still decides when to act, and
    /// the reason surfaces as
    /// <see cref="CompactionReason.FilesMarkedForCompaction"/> on an
    /// <see cref="EventListener"/>. Only honoured at open, like most options.
    /// </para>
    /// <para>
    /// Repeated calls add collectors rather than replacing the previous one.
    /// </para>
    /// <para>
    /// This is the only table properties collector reachable from .NET. RocksDb's
    /// C API declares the factory type and how to attach one, but offers no
    /// function that creates one, so a user-defined collector cannot be built
    /// and <see cref="TableProperties.ReadableProperties"/> stays empty. This
    /// collector does not populate it either; it marks files instead.
    /// </para>
    /// </remarks>
    public DbOptions AddCompactOnDeletionCollector(
        nuint windowSize, nuint deletionTrigger, double deletionRatio = 0, nuint minFileSize = 0)
    {
        NativeMethods.rocksdb_options_add_compact_on_deletion_collector_factory_min_file_size(
            Handle, windowSize, deletionTrigger, deletionRatio, minFileSize);

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

    /// <summary>
    /// Number of partitions used when writing blob files directly. Requires
    /// <see cref="EnableBlobDirectWrite"/>.
    /// </summary>
    /// <remarks>
    /// Without direct blob writing enabled this setting does nothing, so set
    /// both or neither.
    /// </remarks>
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

    /// <summary>
    /// If true, this column family permits ingesting files below the existing
    /// data, which is required by
    /// <see cref="IngestExternalFileOptions.IngestBehind"/>. This is the
    /// setting to use, in preference to <see cref="AllowIngestBehind"/>.
    /// </summary>
    /// <remarks>
    /// It has to be set from the moment the column family is created, or at
    /// least before anything is written to it. Turning it on for a family that
    /// already holds data does not make ingest-behind work for that family.
    /// </remarks>
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

    /// <summary>
    /// If true, writes to this column family's memtable are rejected, which
    /// guards a family that is only ever populated by file ingestion. Not
    /// supported on the default column family.
    /// </summary>
    /// <remarks>
    /// RocksDb rejects this setting on the <c>"default"</c> column family
    /// because of the error-handling difficulties it creates there, so it is
    /// only usable on a family you created yourself.
    /// </remarks>
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

    /// <summary>
    /// If true, file system metadata for SST files is recorded in the manifest
    /// and reused to speed up reopening them. Experimental.
    /// </summary>
    /// <remarks>
    /// No validation is skipped, and nothing is traded away for the speed. The
    /// saving comes from not having to ask the file system again for metadata
    /// already known at write time, which matters most on remote storage where
    /// those calls are slow. Requires file system support; without it the
    /// setting simply does nothing.
    /// </remarks>
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

    /// <summary>
    /// How much of a file has to be read, relative to its size, before reads
    /// alone mark it for compaction.
    /// </summary>
    /// <remarks>
    /// The numerator is the bytes read through collapsible reads rather than
    /// bytes read once: repeatedly reading the same part of a file counts each
    /// time. So this is not a fraction of the file bounded by one, and a value
    /// above one is meaningful.
    /// </remarks>
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

    /// <summary>
    /// If true, compaction reads its input SST files with O_DIRECT, bypassing
    /// the OS page cache, while ordinary user reads stay buffered.
    /// </summary>
    /// <remarks>
    /// A database-level option, not a column-family one. It exists so that the
    /// long sequential reads a compaction performs do not evict the working set
    /// that user reads depend on. It is the read-side counterpart to
    /// <see cref="UseDirectIoForFlushAndCompaction"/>, and the two are often set
    /// together.
    /// </remarks>
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

    /// <summary>
    /// Which verifications RocksDb runs over the files a compaction produces.
    /// </summary>
    /// <remarks>
    /// Needs a bit from each of the two groups in
    /// <see cref="RocksDbNet.VerifyOutputFlags"/> to have any effect. Unchecked
    /// on the way out because <see cref="RocksDbNet.VerifyOutputFlags.All"/> is
    /// every bit set, which the C API takes as -1.
    /// </remarks>
    public VerifyOutputFlags VerifyOutputFlags
    {
        get => (VerifyOutputFlags)unchecked((uint)NativeMethods.rocksdb_options_get_verify_output_flags(Handle));
        set => NativeMethods.rocksdb_options_set_verify_output_flags(Handle, unchecked((int)value));
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

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_options_destroy(Handle);
    }

    protected override void DisposeUnmanagedResources()
    {
        base.DisposeUnmanagedResources();

        // Release rather than dispose. These objects can be attached to more
        // than one options object, and to a database opened from one, so the
        // native release belongs to whichever holder lets go last.
        foreach (RocksDbHandle handle in _ownedHandles)
        {
            handle.ReleaseHolder();
        }

        // Not cleared. Clearing a ConcurrentBag reads a ThreadLocal, and a
        // ThreadLocal is itself finalizable: on the finalizer path it may already
        // be gone, and the ObjectDisposedException that comes back is unhandled
        // and takes the process with it. This ran on every options object that
        // was collected rather than disposed, which is the case the holds above
        // exist to make safe, so the safety net was the thing crashing.
        //
        // Nothing needs the clear: Dispose(bool) compare-exchanges its way to
        // running this once, and the bag is unreachable immediately after.
        // Enumerating is fine, because that freezes the bag under its own lock
        // without touching the thread-local.
    }
}
