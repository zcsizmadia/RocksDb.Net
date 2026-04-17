using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>Options for <c>CompactRange</c> operations.</summary>
public sealed class CompactRangeOptions : RocksDbHandle
{
    public CompactRangeOptions()
    {
        Handle = NativeMethods.rocksdb_compactoptions_create();
    }

    public bool ExclusiveManualCompaction
    {
        set => NativeMethods.rocksdb_compactoptions_set_exclusive_manual_compaction(Handle, value ? (byte)1 : (byte)0);
    }

    public bool BottommostLevelCompaction
    {
        set => NativeMethods.rocksdb_compactoptions_set_bottommost_level_compaction(Handle, value ? (byte)1 : (byte)0);
    }

    public bool ChangeLevel
    {
        set => NativeMethods.rocksdb_compactoptions_set_change_level(Handle, value ? (byte)1 : (byte)0);
    }

    public int TargetLevel
    {
        set => NativeMethods.rocksdb_compactoptions_set_target_level(Handle, value);
    }

    public int MaxSubcompactions
    {
        set => NativeMethods.rocksdb_compactoptions_set_max_subcompactions(Handle, value);
    }

    public override void DisposeUnmanagedResources()
    {
        NativeMethods.rocksdb_compactoptions_destroy(Handle);
    }
}