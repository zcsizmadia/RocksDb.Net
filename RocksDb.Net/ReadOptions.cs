using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// Options that control read operations.
/// Maps to <c>rocksdb_readoptions_t</c>.
/// </summary>
public sealed class ReadOptions : RocksDbHandle
{
    // Iteration bounds are stored by RocksDb as a Slice pointing at the caller's
    // buffer, and are dereferenced on every Seek/Next for as long as these options
    // are in use. Managed memory cannot satisfy that: a `fixed` pin ends when the
    // setter returns and the GC is then free to move or collect the buffer. So keep
    // an unmanaged copy per bound, owned by this instance.
    private NativeBound _upperBound;
    private NativeBound _lowerBound;

    public ReadOptions()
        : base(NativeMethods.rocksdb_readoptions_create())
    {
    }

    /// <summary>An unmanaged copy of an iteration bound, owned by the enclosing options.</summary>
    private readonly struct NativeBound(nint pointer, nuint length)
    {
        public nint Pointer { get; } = pointer;

        public nuint Length { get; } = length;

        public void Free()
        {
            if (Pointer != nint.Zero)
            {
                Marshal.FreeHGlobal(Pointer);
            }
        }
    }

    /// <summary>
    /// Copies <paramref name="key"/> into freshly allocated unmanaged memory and
    /// releases <paramref name="previous"/>. An empty key allocates nothing, which
    /// makes the native setter clear the bound.
    /// </summary>
    private static unsafe NativeBound CopyBound(ReadOnlySpan<byte> key, NativeBound previous)
    {
        NativeBound bound = default;

        if (!key.IsEmpty)
        {
            nint pointer = Marshal.AllocHGlobal(key.Length);
            try
            {
                key.CopyTo(new Span<byte>((void*)pointer, key.Length));
            }
            catch
            {
                Marshal.FreeHGlobal(pointer);
                throw;
            }

            bound = new NativeBound(pointer, (nuint)key.Length);
        }

        // Only release the old copy once the new one is in place, so a failed
        // allocation leaves the existing bound intact.
        previous.Free();
        return bound;
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
    /// </summary>
    /// <remarks>
    /// The key is copied into unmanaged memory owned by this <see cref="ReadOptions"/>
    /// instance, so the caller does not need to keep <paramref name="key"/> alive or
    /// pinned. The copy is released when the bound is replaced and when this instance
    /// is disposed. Passing an empty span clears the bound.
    /// </remarks>
    public unsafe ReadOptions SetIterateUpperBound(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        _upperBound = CopyBound(key, _upperBound);
        NativeMethods.rocksdb_readoptions_set_iterate_upper_bound(Handle, (byte*)_upperBound.Pointer, _upperBound.Length);
        return this;
    }

    /// <summary>
    /// Sets the lower bound for iteration; the iterator will not return keys &lt; this key.
    /// </summary>
    /// <remarks>
    /// The key is copied into unmanaged memory owned by this <see cref="ReadOptions"/>
    /// instance, so the caller does not need to keep <paramref name="key"/> alive or
    /// pinned. The copy is released when the bound is replaced and when this instance
    /// is disposed. Passing an empty span clears the bound.
    /// </remarks>
    public unsafe ReadOptions SetIterateLowerBound(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        _lowerBound = CopyBound(key, _lowerBound);
        NativeMethods.rocksdb_readoptions_set_iterate_lower_bound(Handle, (byte*)_lowerBound.Pointer, _lowerBound.Length);
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

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_readoptions_destroy(Handle);
    }

    public override void DisposeUnmanagedResources()
    {
        // Destroy the native options first: they hold Slices into the bound
        // buffers, so the buffers must outlive them.
        base.DisposeUnmanagedResources();

        _upperBound.Free();
        _upperBound = default;

        _lowerBound.Free();
        _lowerBound = default;
    }
}
