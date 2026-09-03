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

    /// <summary>
    /// Creates a Bloom filter in RocksDb's original on-disk format.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="CreateBloomFull"/>. Both produce a Bloom filter of the
    /// same size and accuracy and neither has anything to do with
    /// partitioning, which is <see cref="BlockBasedTableOptions.PartitionFilters"/>.
    /// The only difference is the legacy record format, kept for compatibility
    /// with databases written by very old RocksDb versions.
    /// </remarks>
    public static FilterPolicy CreateBloom(double bitsPerKey)
        => new(NativeMethods.rocksdb_filterpolicy_create_bloom(bitsPerKey));

    /// <summary>
    /// Creates a Bloom filter in RocksDb's current on-disk format. This is the
    /// one to use.
    /// </summary>
    /// <remarks>
    /// Differs from <see cref="CreateBloom"/> only in the on-disk record
    /// format, not in partitioning or in filter quality.
    /// </remarks>
    public static FilterPolicy CreateBloomFull(double bitsPerKey)
        => new(NativeMethods.rocksdb_filterpolicy_create_bloom_full(bitsPerKey));

    /// <summary>Creates a Ribbon filter.</summary>
    public static FilterPolicy CreateRibbon(double bloomEquivalentBitsPerKey)
        => new(NativeMethods.rocksdb_filterpolicy_create_ribbon(bloomEquivalentBitsPerKey));

    /// <summary>Creates a Ribbon filter that falls back to Bloom for SST files at or below <paramref name="bloomBeforeLevel"/>.</summary>
    public static FilterPolicy CreateRibbonHybrid(double bloomEquivalentBitsPerKey, int bloomBeforeLevel = 0)
        => new(NativeMethods.rocksdb_filterpolicy_create_ribbon_hybrid(bloomEquivalentBitsPerKey, bloomBeforeLevel));

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_filterpolicy_destroy(Handle);
    }
}