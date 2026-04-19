using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>
/// Options that control read operations.
/// Maps to <c>rocksdb_readoptions_t</c>.
/// </summary>
public sealed class ReadOptions : RocksDbHandle
{
    public ReadOptions()
    {
        Handle = NativeMethods.rocksdb_readoptions_create();
    }

    /// <summary>If true, all data read from underlying storage will be verified against checksums.</summary>
    public bool VerifyChecksums
    {
        get => NativeMethods.rocksdb_readoptions_get_verify_checksums(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_verify_checksums(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, the returned data block is added to the block cache.</summary>
    public bool FillCache
    {
        get => NativeMethods.rocksdb_readoptions_get_fill_cache(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_fill_cache(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Attaches a snapshot so reads reflect a consistent point-in-time view.</summary>
    public ReadOptions SetSnapshot(Snapshot? snapshot)
    {
        NativeMethods.rocksdb_readoptions_set_snapshot(Handle, snapshot?.Handle ?? nint.Zero);
        return this;
    }

    /// <summary>
    /// Sets the upper bound for iteration; the iterator will not return keys &gt;= this key.
    /// The span must remain valid for the lifetime of the <see cref="ReadOptions"/>.
    /// </summary>
    public unsafe ReadOptions SetIterateUpperBound(ReadOnlySpan<byte> key)
    {
        fixed (byte* ptr = key)
            NativeMethods.rocksdb_readoptions_set_iterate_upper_bound(Handle, ptr, (nuint)key.Length);
        return this;
    }

    /// <summary>
    /// Sets the lower bound for iteration.
    /// The span must remain valid for the lifetime of the <see cref="ReadOptions"/>.
    /// </summary>
    public unsafe ReadOptions SetIterateLowerBound(ReadOnlySpan<byte> key)
    {
        fixed (byte* ptr = key)
            NativeMethods.rocksdb_readoptions_set_iterate_lower_bound(Handle, ptr, (nuint)key.Length);
        return this;
    }

    /// <summary>
    /// Specify if this read request should process data that ALREADY resides on a
    /// particular cache. If the required data is not found at the specified cache tier,
    /// an empty value is returned.
    /// 0 = read all tiers, 1 = block cache only, 2 = persisted tier.
    /// </summary>
    public int ReadTier
    {
        get => NativeMethods.rocksdb_readoptions_get_read_tier(Handle);
        set => NativeMethods.rocksdb_readoptions_set_read_tier(Handle, value);
    }

    /// <summary>Specify to create a non-snapshot-based tailing iterator.</summary>
    public bool Tailing
    {
        get => NativeMethods.rocksdb_readoptions_get_tailing(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_tailing(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Size of readahead for compaction reads, in bytes (0 = default).</summary>
    public ulong ReadaheadSize
    {
        get => (ulong)NativeMethods.rocksdb_readoptions_get_readahead_size(Handle);
        set => NativeMethods.rocksdb_readoptions_set_readahead_size(Handle, (nuint)value);
    }

    /// <summary>If true, all returned keys must share the same prefix as the seek key.</summary>
    public bool PrefixSameAsStart
    {
        get => NativeMethods.rocksdb_readoptions_get_prefix_same_as_start(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_prefix_same_as_start(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, returned Pinnable slices will pin data in the block cache.</summary>
    public bool PinData
    {
        get => NativeMethods.rocksdb_readoptions_get_pin_data(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_pin_data(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, bypass prefix-based iteration and use total order (sorted) iteration.</summary>
    public bool TotalOrderSeek
    {
        get => NativeMethods.rocksdb_readoptions_get_total_order_seek(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_total_order_seek(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, enable asynchronous I/O during iteration.</summary>
    public bool AsyncIo
    {
        get => NativeMethods.rocksdb_readoptions_get_async_io(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_async_io(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, range deletion tombstones are ignored during reads.</summary>
    public bool IgnoreRangeDeletions
    {
        get => NativeMethods.rocksdb_readoptions_get_ignore_range_deletions(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_ignore_range_deletions(Handle, value ? (byte)1 : (byte)0);
    }

    public override void DisposeUnmanagedResources()
    {
        NativeMethods.rocksdb_readoptions_destroy(Handle);
    }
}
