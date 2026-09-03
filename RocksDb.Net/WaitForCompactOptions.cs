namespace RocksDbNet;

/// <summary>Options for <see cref="RocksDb.WaitForCompact"/>.</summary>
public sealed class WaitForCompactOptions : RocksDbHandle
{
    public WaitForCompactOptions()
        : base(NativeMethods.rocksdb_wait_for_compact_options_create())
    {
    }

    /// <summary>
    /// If true, the wait returns immediately with an error when background work
    /// is paused. Defaults to false.
    /// </summary>
    /// <remarks>
    /// Leaving it false is the trap: if background work is paused and nothing
    /// resumes it, queued jobs may never be scheduled and the wait can block
    /// forever unless <see cref="TimeoutMicros"/> is set. Set this to true, or
    /// make sure background work is resumed.
    /// </remarks>
    public bool AbortOnPause
    {
        get => NativeMethods.rocksdb_wait_for_compact_options_get_abort_on_pause(Handle) != 0;
        set => NativeMethods.rocksdb_wait_for_compact_options_set_abort_on_pause(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, every column family is flushed before the wait begins.
    /// </summary>
    public bool Flush
    {
        get => NativeMethods.rocksdb_wait_for_compact_options_get_flush(Handle) != 0;
        set => NativeMethods.rocksdb_wait_for_compact_options_set_flush(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, the native database is closed once the wait finishes, leaving
    /// the <see cref="RocksDb"/> object unusable.
    /// </summary>
    /// <remarks>
    /// Every later call on that <see cref="RocksDb"/> fails, so treat the wait
    /// as the last thing you do with it. The close can also fail without
    /// closing, if snapshots are still outstanding, in which case the database
    /// stays open.
    /// </remarks>
    public bool CloseDb
    {
        get => NativeMethods.rocksdb_wait_for_compact_options_get_close_db(Handle) != 0;
        set => NativeMethods.rocksdb_wait_for_compact_options_set_close_db(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Maximum time to wait for compaction, in microseconds. Zero, the default,
    /// means wait indefinitely.
    /// </summary>
    /// <remarks>
    /// Zero is not "do not wait"; it waits for as long as there is background
    /// work left. When a non-zero timeout expires the wait fails with a
    /// timeout error rather than returning quietly.
    /// </remarks>
    public ulong TimeoutMicros
    {
        get => NativeMethods.rocksdb_wait_for_compact_options_get_timeout(Handle);
        set => NativeMethods.rocksdb_wait_for_compact_options_set_timeout(Handle, value);
    }

    /// <summary>
    /// If true, the wait also covers purging obsolete files, not just finishing
    /// compaction, so the database directory is fully settled when it returns.
    /// </summary>
    public bool WaitForPurge
    {
        get => NativeMethods.rocksdb_wait_for_compact_options_get_wait_for_purge(Handle) != 0;
        set => NativeMethods.rocksdb_wait_for_compact_options_set_wait_for_purge(Handle, value ? (byte)1 : (byte)0);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_wait_for_compact_options_destroy(Handle);
    }
}
