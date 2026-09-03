namespace RocksDbNet;

/// <summary>
/// Settings for a least-recently-used block cache. Maps to
/// <c>rocksdb_lru_cache_options_t</c>.
/// </summary>
/// <remarks>
/// Only needed to control sharding. <see cref="Cache.CreateLru(ulong)"/> covers
/// the common case. RocksDb copies these when the cache is created, so this
/// object may be disposed straight afterwards.
/// </remarks>
public sealed class LruCacheOptions : RocksDbHandle
{
    /// <summary>Creates options with RocksDb's defaults.</summary>
    public LruCacheOptions()
        : base(NativeMethods.rocksdb_lru_cache_options_create())
    {
    }

    /// <summary>Total cache capacity in bytes. Write-only.</summary>
    public nuint Capacity
    {
        set => NativeMethods.rocksdb_lru_cache_options_set_capacity(Handle, value);
    }

    /// <summary>
    /// Base-2 logarithm of the shard count. Negative lets RocksDb choose.
    /// Write-only.
    /// </summary>
    /// <remarks>
    /// More shards reduce lock contention between threads but waste capacity,
    /// because each shard gets an equal slice and they fill unevenly. Worth
    /// raising only when cache contention is measurably the problem.
    /// </remarks>
    public int NumShardBits
    {
        set => NativeMethods.rocksdb_lru_cache_options_set_num_shard_bits(Handle, value);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_lru_cache_options_destroy(Handle);
    }
}

/// <summary>
/// Settings for a HyperClock block cache. Maps to
/// <c>rocksdb_hyper_clock_cache_options_t</c>.
/// </summary>
/// <remarks>
/// See <see cref="Cache.CreateHyperClock(ulong, ulong)"/> for what distinguishes
/// this cache from the LRU one. RocksDb copies these when the cache is
/// created, so this object may be disposed straight afterwards.
/// </remarks>
public sealed class HyperClockCacheOptions : RocksDbHandle
{
    /// <summary>Creates options for a cache of the given size.</summary>
    /// <param name="capacityBytes">Total cache capacity in bytes.</param>
    /// <param name="estimatedEntryCharge">
    /// Rough average size of a cached entry in bytes, or zero to let RocksDb
    /// adapt. See <see cref="EstimatedEntryCharge"/>.
    /// </param>
    public HyperClockCacheOptions(ulong capacityBytes, ulong estimatedEntryCharge = 0)
        : base(NativeMethods.rocksdb_hyper_clock_cache_options_create(
            (nuint)capacityBytes, (nuint)estimatedEntryCharge))
    {
    }

    /// <summary>Total cache capacity in bytes. Write-only.</summary>
    public nuint Capacity
    {
        set => NativeMethods.rocksdb_hyper_clock_cache_options_set_capacity(Handle, value);
    }

    /// <summary>
    /// Rough average size of a cached entry in bytes, or zero to let RocksDb
    /// adapt. Write-only.
    /// </summary>
    /// <remarks>
    /// HyperClock trades the LRU cache's per-shard locking for a fixed-size
    /// table, and it needs an entry-size estimate to size that table. A wildly
    /// wrong estimate wastes capacity or causes premature eviction, so zero,
    /// which lets RocksDb work it out, is the safer default.
    /// </remarks>
    public nuint EstimatedEntryCharge
    {
        set => NativeMethods.rocksdb_hyper_clock_cache_options_set_estimated_entry_charge(Handle, value);
    }

    /// <summary>
    /// Base-2 logarithm of the shard count. Negative lets RocksDb choose.
    /// Write-only.
    /// </summary>
    public int NumShardBits
    {
        set => NativeMethods.rocksdb_hyper_clock_cache_options_set_num_shard_bits(Handle, value);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_hyper_clock_cache_options_destroy(Handle);
    }
}

/// <summary>
/// Forces SST file boundaries to fall on key-prefix boundaries. Maps to
/// <c>rocksdb_sst_partitioner_factory_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Without a partitioner, a compaction splits files wherever the size target
/// falls, so one prefix's data can straddle several files and each file can
/// hold several prefixes. That makes prefix-scoped work less effective: a
/// range delete over one prefix cannot drop whole files, and a prefix scan
/// touches more files than it needs.
/// </para>
/// <para>
/// Attach with <see cref="DbOptions.SstPartitionerFactory"/>. RocksDb takes a
/// shared reference, so this object may be disposed once assigned.
/// </para>
/// </remarks>
public sealed class SstPartitionerFactory : RocksDbHandle
{
    private SstPartitionerFactory(nint handle)
        : base(handle)
    {
    }

    /// <summary>
    /// Creates a factory that partitions on the first
    /// <paramref name="prefixLength"/> bytes of each key.
    /// </summary>
    /// <param name="prefixLength">
    /// How many leading bytes make up the prefix. Must be greater than zero.
    /// </param>
    public static SstPartitionerFactory CreateFixedPrefix(int prefixLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(prefixLength);

        return new SstPartitionerFactory(
            NativeMethods.rocksdb_sst_partitioner_fixed_prefix_factory_create((nuint)prefixLength));
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_sst_partitioner_factory_destroy(Handle);
    }
}
