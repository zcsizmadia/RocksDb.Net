namespace RocksDbNet;

/// <summary>Options for <see cref="RocksDb.WaitForCompact"/>.</summary>
public sealed class WaitForCompactOptions : RocksDbHandle
{
    public WaitForCompactOptions()
        : base(NativeMethods.rocksdb_wait_for_compact_options_create())
    {
    }

    /// <summary>If true, abort on pause during the wait.</summary>
    public bool AbortOnPause
    {
        get => NativeMethods.rocksdb_wait_for_compact_options_get_abort_on_pause(Handle) != 0;
        set => NativeMethods.rocksdb_wait_for_compact_options_set_abort_on_pause(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, flush before waiting for compaction.</summary>
    public bool Flush
    {
        get => NativeMethods.rocksdb_wait_for_compact_options_get_flush(Handle) != 0;
        set => NativeMethods.rocksdb_wait_for_compact_options_set_flush(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, close the database after the wait completes.</summary>
    public bool CloseDb
    {
        get => NativeMethods.rocksdb_wait_for_compact_options_get_close_db(Handle) != 0;
        set => NativeMethods.rocksdb_wait_for_compact_options_set_close_db(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Maximum time to wait for compaction, in microseconds.</summary>
    public ulong TimeoutMicros
    {
        get => NativeMethods.rocksdb_wait_for_compact_options_get_timeout(Handle);
        set => NativeMethods.rocksdb_wait_for_compact_options_set_timeout(Handle, value);
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_wait_for_compact_options_destroy(Handle);
    }
}
