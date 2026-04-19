using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>Options for <c>CompactRange</c> operations.</summary>
public sealed class CompactRangeOptions : RocksDbHandle
{
    public CompactRangeOptions()
    {
        Handle = NativeMethods.rocksdb_compactoptions_create();
    }

    /// <summary>If true, no other compaction will run at the same time as this one.</summary>
    public bool ExclusiveManualCompaction
    {
        set => NativeMethods.rocksdb_compactoptions_set_exclusive_manual_compaction(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, include the bottommost level in the compaction.</summary>
    public bool BottommostLevelCompaction
    {
        set => NativeMethods.rocksdb_compactoptions_set_bottommost_level_compaction(Handle, value ? (byte)1 : (byte)0);
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

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_compactoptions_destroy(Handle);
    }
}