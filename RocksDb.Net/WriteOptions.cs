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

    /// <summary>
    /// Per-key checksum bytes added to the write batch so that corruption in
    /// memory is detected before the data is written. Only 0 and 8 are
    /// supported.
    /// </summary>
    /// <remarks>
    /// Zero, the default, disables the check; eight enables it. RocksDb accepts
    /// no other width, so a value such as 4 is not a weaker setting but an
    /// invalid one.
    /// </remarks>
    public ulong ProtectionBytesPerKey
    {
        get => (ulong)NativeMethods.rocksdb_writeoptions_get_protection_bytes_per_key(Handle);
        set => NativeMethods.rocksdb_writeoptions_set_protection_bytes_per_key(Handle, (nuint)value);
    }

    /// <summary>
    /// Priority this write is given by the rate limiter, if one is configured.
    /// </summary>
    public RateLimiterPriority RateLimiterPriority
    {
        get => (RateLimiterPriority)NativeMethods.rocksdb_writeoptions_get_rate_limiter_priority(Handle);
        set => NativeMethods.rocksdb_writeoptions_set_rate_limiter_priority(Handle, (int)value);
    }

    /// <summary>
    /// Labels the I/O this write performs. Leave this alone unless you have a
    /// reason to override how RocksDb accounts for the operation.
    /// </summary>
    public IoActivity IoActivity
    {
        get => (IoActivity)NativeMethods.rocksdb_writeoptions_get_io_activity(Handle);
        set => NativeMethods.rocksdb_writeoptions_set_io_activity(Handle, (int)value);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_writeoptions_destroy(Handle);
    }
}
