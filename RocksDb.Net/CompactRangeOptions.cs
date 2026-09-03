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
        set => NativeMethods.rocksdb_compactoptions_set_change_level(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Target output level for the compacted files.</summary>
    public int TargetLevel
    {
        set => NativeMethods.rocksdb_compactoptions_set_target_level(Handle, value);
    }

    /// <summary>Maximum number of subcompactions for this compaction.</summary>
    public int MaxSubcompactions
    {
        set => NativeMethods.rocksdb_compactoptions_set_max_subcompactions(Handle, value);
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