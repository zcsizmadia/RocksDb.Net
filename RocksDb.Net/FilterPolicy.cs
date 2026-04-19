using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>
/// A bloom filter or ribbon filter policy for block-based tables.
/// Maps to <c>rocksdb_filterpolicy_t</c>.
/// </summary>
public sealed class FilterPolicy : RocksDbHandle
{
    private FilterPolicy(nint handle)
        : base(handle)
    {
    }

    /// <summary>Creates a partitioned block-based Bloom filter.</summary>
    public static FilterPolicy CreateBloom(double bitsPerKey)
        => new(NativeMethods.rocksdb_filterpolicy_create_bloom(bitsPerKey));

    /// <summary>Creates a full (non-partitioned) Bloom filter.</summary>
    public static FilterPolicy CreateBloomFull(double bitsPerKey)
        => new(NativeMethods.rocksdb_filterpolicy_create_bloom_full(bitsPerKey));

    /// <summary>Creates a Ribbon filter.</summary>
    public static FilterPolicy CreateRibbon(double bloomEquivalentBitsPerKey)
        => new(NativeMethods.rocksdb_filterpolicy_create_ribbon(bloomEquivalentBitsPerKey));

    /// <summary>Creates a Ribbon filter that falls back to Bloom for SST files at or below <paramref name="bloomBeforeLevel"/>.</summary>
    public static FilterPolicy CreateRibbonHybrid(double bloomEquivalentBitsPerKey, int bloomBeforeLevel = 0)
        => new(NativeMethods.rocksdb_filterpolicy_create_ribbon_hybrid(bloomEquivalentBitsPerKey, bloomBeforeLevel));

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_filterpolicy_destroy(Handle);
    }
}