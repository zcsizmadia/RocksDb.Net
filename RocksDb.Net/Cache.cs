using RocksDbNet.Native;

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

    /// <summary>Creates a HyperClock cache.</summary>
    public static Cache CreateHyperClock(ulong capacityBytes, ulong estimatedEntryChargeBytes)
        => new(NativeMethods.rocksdb_cache_create_hyper_clock((nuint)capacityBytes, (nuint)estimatedEntryChargeBytes));

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
    
    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_cache_destroy(Handle);
    }
}