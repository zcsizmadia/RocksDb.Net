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

    /// <summary>Creates a Bloom filter policy.</summary>
    /// <remarks>
    /// There was a <c>CreateBloom</c> beside this one, documented as the legacy
    /// on-disk format against this one as the current format. That difference
    /// was not real: RocksDb stopped honouring the parameter that chose between
    /// them in version 7.0, and measured over 500 keys the two produced the same
    /// policy name and byte-identical files. Keeping both would have been an
    /// invitation to think about a choice that does not exist.
    /// <para>
    /// Nothing here relates to partitioning, which is
    /// <see cref="BlockBasedTableOptions.PartitionFilters"/>.
    /// </para>
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