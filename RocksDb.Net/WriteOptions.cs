namespace RocksDbNet;

/// <summary>
/// Options that control write operations.
/// Maps to <c>rocksdb_writeoptions_t</c>.
/// </summary>
public sealed class WriteOptions : RocksDbHandle
{
    public WriteOptions()
        : base(NativeMethods.rocksdb_writeoptions_create()) 
    {
    }

    /// <summary>If true, the write will be flushed from the OS buffer cache before the write is considered complete.</summary>
    public bool Sync
    {
        get => NativeMethods.rocksdb_writeoptions_get_sync(Handle) != 0;
        set => NativeMethods.rocksdb_writeoptions_set_sync(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, writes will not first go to the write ahead log and the write may be lost after a crash.</summary>
    public bool DisableWal
    {
        get => NativeMethods.rocksdb_writeoptions_get_disable_WAL(Handle) != 0;
        set => NativeMethods.rocksdb_writeoptions_disable_WAL(Handle, value ? 1 : 0);
    }

    /// <summary>If true, return immediately with a <see cref="RocksDbException"/> if the write request is slowed to prevent OOM errors.</summary>
    public bool NoSlowdown
    {
        get => NativeMethods.rocksdb_writeoptions_get_no_slowdown(Handle) != 0;
        set => NativeMethods.rocksdb_writeoptions_set_no_slowdown(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, this write request is of lower priority.</summary>
    public bool LowPriority
    {
        get => NativeMethods.rocksdb_writeoptions_get_low_pri(Handle) != 0;
        set => NativeMethods.rocksdb_writeoptions_set_low_pri(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, writes to column families that do not exist are ignored rather than failing.</summary>
    public bool IgnoreMissingColumnFamilies
    {
        get => NativeMethods.rocksdb_writeoptions_get_ignore_missing_column_families(Handle) != 0;
        set => NativeMethods.rocksdb_writeoptions_set_ignore_missing_column_families(Handle, value ? (byte)1 : (byte)0);
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_writeoptions_destroy(Handle);
    }
}
