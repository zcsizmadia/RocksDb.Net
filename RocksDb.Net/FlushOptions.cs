namespace RocksDbNet;

/// <summary>Options for <see cref="RocksDb.Flush(FlushOptions?)"/> operations.</summary>
public sealed class FlushOptions : RocksDbHandle
{
    public FlushOptions()
    {
        Handle = NativeMethods.rocksdb_flushoptions_create();
    }

    /// <summary>If true, the flush will wait until it completes before returning.</summary>
    public bool Wait
    {
        get => NativeMethods.rocksdb_flushoptions_get_wait(Handle) != 0;
        set => NativeMethods.rocksdb_flushoptions_set_wait(Handle, value ? (byte)1 : (byte)0);
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_flushoptions_destroy(Handle);
    }
}