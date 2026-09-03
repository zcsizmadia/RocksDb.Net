namespace RocksDbNet;

/// <summary>
/// How universal compaction decides where to stop adding files to a
/// compaction, mapped from <c>rocksdb::CompactionStopStyle</c> in
/// <c>universal_compaction.h</c>.
/// </summary>
public enum CompactionStopStyle
{
    /// <summary>Stop once the candidate files stop being of similar size.</summary>
    SimilarSize = 0,

    /// <summary>
    /// Stop once the total size of the picked files exceeds the next file's
    /// size.
    /// </summary>
    TotalSize = 1,
}

/// <summary>
/// Tuning for universal compaction. Maps to
/// <c>rocksdb_universal_compaction_options_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only used when <see cref="DbOptions.CompactionStyle"/> is
/// <see cref="RocksDbNet.CompactionStyle.Universal"/>. Universal compaction
/// exists to trade space amplification against write amplification, and these
/// are the settings that make that trade, so selecting the style without
/// setting them leaves the interesting decisions at their defaults.
/// </para>
/// <para>
/// Attach with <see cref="DbOptions.UniversalCompactionOptions"/>. RocksDb
/// copies the values, so this object may be disposed immediately afterwards.
/// </para>
/// </remarks>
public sealed class UniversalCompactionOptions : RocksDbHandle
{
    /// <summary>Creates options with RocksDb's defaults.</summary>
    public UniversalCompactionOptions()
        : base(NativeMethods.rocksdb_universal_compaction_options_create())
    {
    }

    /// <summary>
    /// How much file sizes may differ, as a percentage, and still be compacted
    /// together.
    /// </summary>
    /// <remarks>
    /// A candidate within this percentage of the next file's size is merged
    /// with it. Larger values produce fewer, bigger compactions.
    /// </remarks>
    public int SizeRatio
    {
        get => NativeMethods.rocksdb_universal_compaction_options_get_size_ratio(Handle);
        set => NativeMethods.rocksdb_universal_compaction_options_set_size_ratio(Handle, value);
    }

    /// <summary>Fewest files that will be compacted together.</summary>
    public int MinMergeWidth
    {
        get => NativeMethods.rocksdb_universal_compaction_options_get_min_merge_width(Handle);
        set => NativeMethods.rocksdb_universal_compaction_options_set_min_merge_width(Handle, value);
    }

    /// <summary>Most files that will be compacted together.</summary>
    public int MaxMergeWidth
    {
        get => NativeMethods.rocksdb_universal_compaction_options_get_max_merge_width(Handle);
        set => NativeMethods.rocksdb_universal_compaction_options_set_max_merge_width(Handle, value);
    }

    /// <summary>
    /// How much larger than the live data the database may grow, as a
    /// percentage, before a full compaction is forced.
    /// </summary>
    /// <remarks>
    /// The setting that bounds universal compaction's main cost. Lower values
    /// keep the database smaller and compact more often.
    /// </remarks>
    public int MaxSizeAmplificationPercent
    {
        get => NativeMethods.rocksdb_universal_compaction_options_get_max_size_amplification_percent(Handle);
        set => NativeMethods.rocksdb_universal_compaction_options_set_max_size_amplification_percent(Handle, value);
    }

    /// <summary>
    /// What percentage of the data is compressed, with the newest left
    /// uncompressed. Negative disables the behaviour and compresses everything.
    /// </summary>
    public int CompressionSizePercent
    {
        get => NativeMethods.rocksdb_universal_compaction_options_get_compression_size_percent(Handle);
        set => NativeMethods.rocksdb_universal_compaction_options_set_compression_size_percent(Handle, value);
    }

    /// <summary>Where a compaction stops adding files.</summary>
    public CompactionStopStyle StopStyle
    {
        get => (CompactionStopStyle)NativeMethods.rocksdb_universal_compaction_options_get_stop_style(Handle);
        set => NativeMethods.rocksdb_universal_compaction_options_set_stop_style(Handle, (int)value);
    }

    /// <summary>
    /// Whether a file may be moved between levels instead of rewritten when
    /// nothing needs merging.
    /// </summary>
    /// <remarks>
    /// A trivial move is far cheaper than a rewrite, since it only updates
    /// metadata.
    /// </remarks>
    public bool AllowTrivialMove
    {
        get => NativeMethods.rocksdb_universal_compaction_options_get_allow_trivial_move(Handle) != 0;
        set => NativeMethods.rocksdb_universal_compaction_options_set_allow_trivial_move(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether a full compaction is performed in pieces rather than all at
    /// once.
    /// </summary>
    /// <remarks>
    /// Full universal compactions are large. Doing one incrementally keeps its
    /// peak space and time cost down, at the price of taking longer overall.
    /// </remarks>
    public bool Incremental
    {
        get => NativeMethods.rocksdb_universal_compaction_options_get_incremental(Handle) != 0;
        set => NativeMethods.rocksdb_universal_compaction_options_set_incremental(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Most sorted runs a read may have to consult, or a non-positive value to
    /// let RocksDb derive it.
    /// </summary>
    public int MaxReadAmp
    {
        get => NativeMethods.rocksdb_universal_compaction_options_get_max_read_amp(Handle);
        set => NativeMethods.rocksdb_universal_compaction_options_set_max_read_amp(Handle, value);
    }

    /// <summary>Whether file locking is reduced during compaction.</summary>
    public bool ReduceFileLocking
    {
        get => NativeMethods.rocksdb_universal_compaction_options_get_reduce_file_locking(Handle) != 0;
        set => NativeMethods.rocksdb_universal_compaction_options_set_reduce_file_locking(Handle, value ? (byte)1 : (byte)0);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_universal_compaction_options_destroy(Handle);
    }
}

/// <summary>
/// Tuning for FIFO compaction. Maps to
/// <c>rocksdb_fifo_compaction_options_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only used when <see cref="DbOptions.CompactionStyle"/> is
/// <see cref="RocksDbNet.CompactionStyle.Fifo"/>. FIFO keeps a bounded amount
/// of the most recent data and drops the oldest, which suits time-series and
/// logs, so <see cref="MaxTableFilesSize"/> is what determines retention.
/// </para>
/// <para>
/// Attach with <see cref="DbOptions.FifoCompactionOptions"/>. RocksDb copies
/// the values, so this object may be disposed immediately afterwards.
/// </para>
/// </remarks>
public sealed class FifoCompactionOptions : RocksDbHandle
{
    /// <summary>Creates options with RocksDb's defaults.</summary>
    public FifoCompactionOptions()
        : base(NativeMethods.rocksdb_fifo_compaction_options_create())
    {
    }

    /// <summary>
    /// Total size of table files to retain, in bytes. Once exceeded, the oldest
    /// files are dropped.
    /// </summary>
    /// <remarks>
    /// The retention control. Data is deleted rather than compacted away, so
    /// this is a hard bound on how much history the database keeps.
    /// </remarks>
    public ulong MaxTableFilesSize
    {
        get => NativeMethods.rocksdb_fifo_compaction_options_get_max_table_files_size(Handle);
        set => NativeMethods.rocksdb_fifo_compaction_options_set_max_table_files_size(Handle, value);
    }

    /// <summary>Total size of data files to retain, in bytes.</summary>
    public ulong MaxDataFilesSize
    {
        get => NativeMethods.rocksdb_fifo_compaction_options_get_max_data_files_size(Handle);
        set => NativeMethods.rocksdb_fifo_compaction_options_set_max_data_files_size(Handle, value);
    }

    /// <summary>
    /// Whether small files may be compacted together rather than only dropped.
    /// </summary>
    /// <remarks>
    /// Off by default, because FIFO's appeal is that it does almost no
    /// compaction work. Enabling it reduces the file count at the cost of some
    /// write amplification.
    /// </remarks>
    public bool AllowCompaction
    {
        get => NativeMethods.rocksdb_fifo_compaction_options_get_allow_compaction(Handle) != 0;
        set => NativeMethods.rocksdb_fifo_compaction_options_set_allow_compaction(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Age in seconds after which a file is moved to warm storage, or zero to
    /// disable.
    /// </summary>
    /// <remarks>
    /// Works with <see cref="Temperature"/>-aware storage: old files are marked
    /// so that a tiered filesystem can move them off fast media.
    /// </remarks>
    public ulong AgeForWarm
    {
        get => NativeMethods.rocksdb_fifo_compaction_options_get_age_for_warm(Handle);
        set => NativeMethods.rocksdb_fifo_compaction_options_set_age_for_warm(Handle, value);
    }

    /// <summary>
    /// Whether a temperature change may copy a file rather than rewriting it.
    /// </summary>
    public bool AllowTrivialCopyWhenChangeTemperature
    {
        get => NativeMethods.rocksdb_fifo_compaction_options_get_allow_trivial_copy_when_change_temperature(Handle) != 0;
        set => NativeMethods.rocksdb_fifo_compaction_options_set_allow_trivial_copy_when_change_temperature(
            Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Buffer size used for such a copy, in bytes.</summary>
    public ulong TrivialCopyBufferSize
    {
        get => NativeMethods.rocksdb_fifo_compaction_options_get_trivial_copy_buffer_size(Handle);
        set => NativeMethods.rocksdb_fifo_compaction_options_set_trivial_copy_buffer_size(Handle, value);
    }

    /// <summary>
    /// Whether compaction is driven by the ratio of keys to values rather than
    /// by size alone.
    /// </summary>
    public bool UseKvRatioCompaction
    {
        get => NativeMethods.rocksdb_fifo_compaction_options_get_use_kv_ratio_compaction(Handle) != 0;
        set => NativeMethods.rocksdb_fifo_compaction_options_set_use_kv_ratio_compaction(Handle, value ? (byte)1 : (byte)0);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_fifo_compaction_options_destroy(Handle);
    }
}
