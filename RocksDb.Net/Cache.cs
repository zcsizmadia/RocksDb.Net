namespace RocksDbNet;

/// <summary>
/// An LRU or HyperClock block/row cache.
/// Maps to <c>rocksdb_cache_t</c>.
/// </summary>
public sealed class Cache : RocksDbHandle
{
    private Cache(nint handle)
        : base(handle)
    {
    }

    /// <summary>Creates an LRU cache with the specified capacity (bytes).</summary>
    public static Cache CreateLru(ulong capacityBytes)
        => new(NativeMethods.rocksdb_cache_create_lru((nuint)capacityBytes));

    /// <summary>Creates an LRU cache that enforces strict capacity limits.</summary>
    public static Cache CreateLruWithStrictCapacityLimit(ulong capacityBytes)
        => new(NativeMethods.rocksdb_cache_create_lru_with_strict_capacity_limit((nuint)capacityBytes));

    /// <summary>Creates an LRU cache from <paramref name="options"/>.</summary>
    /// <remarks>
    /// Use this when the shard count matters. RocksDb copies the options, so
    /// they may be disposed straight afterwards.
    /// </remarks>
    public static Cache CreateLru(LruCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new Cache(NativeMethods.rocksdb_cache_create_lru_opts(options.Handle));
    }

    /// <summary>Creates a HyperClock cache.</summary>
    /// <param name="capacityBytes">Total capacity in bytes.</param>
    /// <param name="estimatedEntryChargeBytes">
    /// Rough average entry size, or zero to let RocksDb adapt.
    /// </param>
    /// <remarks>
    /// HyperClock replaces the LRU cache's per-shard locking with a fixed-size
    /// table, which scales better under concurrent readers. In exchange it wants
    /// an estimate of the average entry size to size that table; passing zero
    /// lets RocksDb work it out.
    /// </remarks>
    public static Cache CreateHyperClock(ulong capacityBytes, ulong estimatedEntryChargeBytes)
        => new(NativeMethods.rocksdb_cache_create_hyper_clock((nuint)capacityBytes, (nuint)estimatedEntryChargeBytes));

    /// <summary>Creates a HyperClock cache from <paramref name="options"/>.</summary>
    /// <inheritdoc cref="CreateLru(LruCacheOptions)" path="/remarks"/>
    public static Cache CreateHyperClock(HyperClockCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new Cache(NativeMethods.rocksdb_cache_create_hyper_clock_opts(options.Handle));
    }

    /// <summary>Gets or sets the cache capacity in bytes.</summary>
    public ulong Capacity
    {
        get => (ulong)NativeMethods.rocksdb_cache_get_capacity(Handle);
        set => NativeMethods.rocksdb_cache_set_capacity(Handle, (nuint)value);
    }

    /// <summary>Current memory usage of the cache in bytes.</summary>
    public ulong Usage => (ulong)NativeMethods.rocksdb_cache_get_usage(Handle);

    /// <summary>Current pinned memory usage of the cache in bytes.</summary>
    public ulong PinnedUsage => (ulong)NativeMethods.rocksdb_cache_get_pinned_usage(Handle);

    /// <summary>How many entries the cache currently holds.</summary>
    /// <remarks>
    /// Together with <see cref="Usage"/> this gives the average entry size,
    /// which is what <see cref="HyperClockCacheOptions.EstimatedEntryCharge"/>
    /// wants. Measuring it on a running database beats guessing.
    /// </remarks>
    public ulong OccupancyCount => (ulong)NativeMethods.rocksdb_cache_get_occupancy_count(Handle);

    /// <summary>How many slots the cache's hash table has.</summary>
    /// <remarks>
    /// Compare against <see cref="OccupancyCount"/> to see how full the table
    /// is. Chiefly of interest for a HyperClock cache, whose table is fixed at
    /// creation.
    /// </remarks>
    public ulong TableAddressCount => (ulong)NativeMethods.rocksdb_cache_get_table_address_count(Handle);

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_cache_destroy(Handle);
    }
}