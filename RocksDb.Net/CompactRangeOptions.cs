namespace RocksDbNet;

/// <summary>Options for <c>CompactRange</c> operations.</summary>
public sealed class CompactRangeOptions : RocksDbHandle
{
    public CompactRangeOptions()
        : base(NativeMethods.rocksdb_compactoptions_create())
    {
    }

    /// <summary>If true, no other compaction will run at the same time as this one.</summary>
    public bool ExclusiveManualCompaction
    {
        // The C API has a getter for this, so there was no reason for the
        // property to be write-only. Every other write-only property on this
        // type has since gained a getter too; the ones that remain write-only
        // elsewhere in the library are the ones the C API genuinely cannot
        // read back.
        get => NativeMethods.rocksdb_compactoptions_get_exclusive_manual_compaction(Handle) != 0;
        set => NativeMethods.rocksdb_compactoptions_set_exclusive_manual_compaction(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// How the bottommost level should be treated during a manual compaction.
    /// </summary>
    /// <remarks>
    /// This was a <see langword="bool"/> over a four-value native setting, and
    /// the mapping did not mean what it read like: <c>true</c> selected value 1,
    /// which is the default, so it changed nothing, and
    /// <see cref="BottommostLevelCompaction.Force"/> could not be reached at
    /// all.
    /// </remarks>
    public BottommostLevelCompaction BottommostLevelCompaction
    {
        get => (BottommostLevelCompaction)NativeMethods.rocksdb_compactoptions_get_bottommost_level_compaction(Handle);
        set => NativeMethods.rocksdb_compactoptions_set_bottommost_level_compaction(Handle, checked((byte)value));
    }

    /// <summary>If true, allow compaction to change the output level.</summary>
    public bool ChangeLevel
    {
        get => NativeMethods.rocksdb_compactoptions_get_change_level(Handle) != 0;
        set => NativeMethods.rocksdb_compactoptions_set_change_level(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Target output level for the compacted files.</summary>
    public int TargetLevel
    {
        get => NativeMethods.rocksdb_compactoptions_get_target_level(Handle);
        set => NativeMethods.rocksdb_compactoptions_set_target_level(Handle, value);
    }

    /// <summary>Maximum number of subcompactions for this compaction.</summary>
    /// <remarks>
    /// Unsigned, matching <see cref="CompactFilesOptions.MaxSubcompactions"/> and
    /// the <c>uint32_t</c> RocksDb keeps it in. This was an <c>int</c>, so the
    /// same setting had two types in two sibling classes and a negative value
    /// reached RocksDb as an enormous one. The C header spells the parameter
    /// <c>int</c>, which is why the call casts.
    /// </remarks>
    public uint MaxSubcompactions
    {
        get => checked((uint)NativeMethods.rocksdb_compactoptions_get_max_subcompactions(Handle));
        set => NativeMethods.rocksdb_compactoptions_set_max_subcompactions(Handle, checked((int)value));
    }

    /// <summary>
    /// Whether the compaction may proceed even if it would stall writes.
    /// Default is <see langword="false"/>, which makes RocksDb wait instead.
    /// </summary>
    /// <remarks>
    /// A manual compaction competes with incoming writes for the same write
    /// buffers. Left false, RocksDb defers the compaction rather than blocking
    /// writers; set true when finishing the compaction matters more than write
    /// latency.
    /// </remarks>
    public bool AllowWriteStall
    {
        get => NativeMethods.rocksdb_compactoptions_get_allow_write_stall(Handle) != 0;
        set => NativeMethods.rocksdb_compactoptions_set_allow_write_stall(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Which of the configured database paths the output is written to, by
    /// index. Default is zero, the first path.
    /// </summary>
    /// <remarks>
    /// Only meaningful when several paths are configured through
    /// <see cref="DbOptions.SetDbPaths(System.Collections.Generic.IReadOnlyList{DbPath})"/>.
    /// It is how a caller moves compacted output onto a chosen device.
    /// <para>
    /// An index into that list, so unsigned. It was an <c>int</c>, in which a
    /// negative value meant nothing and reached RocksDb as an enormous index.
    /// </para>
    /// </remarks>
    public uint TargetPathId
    {
        get => checked((uint)NativeMethods.rocksdb_compactoptions_get_target_path_id(Handle));
        set => NativeMethods.rocksdb_compactoptions_set_target_path_id(Handle, checked((int)value));
    }

    /// <summary>
    /// Whether this compaction collects blob-file garbage, overriding the column
    /// family setting. Defaults to <see cref="RocksDbNet.BlobGarbageCollectionPolicy.UseDefault"/>.
    /// </summary>
    public BlobGarbageCollectionPolicy BlobGarbageCollectionPolicy
    {
        get => (BlobGarbageCollectionPolicy)NativeMethods.rocksdb_compactoptions_get_blob_garbage_collection_policy(Handle);
        set => NativeMethods.rocksdb_compactoptions_set_blob_garbage_collection_policy(Handle, (int)value);
    }

    /// <summary>
    /// The fraction of the oldest blob files considered for garbage collection,
    /// from 0.0 to 1.0. A negative value falls back to the column family setting.
    /// </summary>
    public double BlobGarbageCollectionAgeCutoff
    {
        get => NativeMethods.rocksdb_compactoptions_get_blob_garbage_collection_age_cutoff(Handle);
        set => NativeMethods.rocksdb_compactoptions_set_blob_garbage_collection_age_cutoff(Handle, value);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_compactoptions_destroy(Handle);
    }
}