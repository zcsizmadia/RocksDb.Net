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

    /// <summary>
    /// If true, the flush proceeds even when doing so would stall writes. If
    /// false, RocksDb returns an error rather than blocking the writer.
    /// </summary>
    public bool AllowWriteStall
    {
        get => NativeMethods.rocksdb_flushoptions_get_allow_write_stall(Handle) != 0;
        set => NativeMethods.rocksdb_flushoptions_set_allow_write_stall(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, every column family is flushed together as one atomic unit, so
    /// the flushed files share a consistent view even across column families.
    /// </summary>
    public bool ForceAtomicFlush
    {
        get => NativeMethods.rocksdb_flushoptions_get_force_atomic_flush(Handle) != 0;
        set => NativeMethods.rocksdb_flushoptions_set_force_atomic_flush(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, the flush call does not return until the registered
    /// <see cref="EventListener"/> callbacks for it have run. Useful when a test
    /// or a caller needs the notification to have been observed.
    /// </summary>
    public bool ListenerWait
    {
        get => NativeMethods.rocksdb_flushoptions_get_listener_wait(Handle) != 0;
        set => NativeMethods.rocksdb_flushoptions_set_listener_wait(Handle, value ? (byte)1 : (byte)0);
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_flushoptions_destroy(Handle);
    }
}

/// <summary>
/// Options for flushing the write-ahead log.
/// Maps to <c>rocksdb_flushwaloptions_t</c>.
/// </summary>
public sealed class FlushWalOptions : RocksDbHandle
{
    public FlushWalOptions()
        : base(NativeMethods.rocksdb_flushwaloptions_create())
    {
    }

    /// <summary>
    /// If true, the WAL is synced to durable storage, not merely handed to the
    /// operating system.
    /// </summary>
    public bool Sync
    {
        get => NativeMethods.rocksdb_flushwaloptions_get_sync(Handle) != 0;
        set => NativeMethods.rocksdb_flushwaloptions_set_sync(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Priority this flush is given by the rate limiter, if one is configured.</summary>
    public RateLimiterPriority RateLimiterPriority
    {
        get => (RateLimiterPriority)NativeMethods.rocksdb_flushwaloptions_get_rate_limiter_priority(Handle);
        set => NativeMethods.rocksdb_flushwaloptions_set_rate_limiter_priority(Handle, (int)value);
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_flushwaloptions_destroy(Handle);
    }
}