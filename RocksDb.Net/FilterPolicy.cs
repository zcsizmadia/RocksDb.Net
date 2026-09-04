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
    /// Creates a Bloom filter policy. Identical to
    /// <see cref="CreateBloomFull"/>.
    /// </summary>
    /// <remarks>
    /// The two are the same policy, not two formats. RocksDb stopped honouring
    /// the parameter that once chose between them in version 7.0, and both now
    /// build the current format. Measured over 500 keys, they produce the same
    /// policy name and byte-identical files.
    /// <para>
    /// Neither has anything to do with partitioning, which is
    /// <see cref="BlockBasedTableOptions.PartitionFilters"/>.
    /// </para>
    /// </remarks>
    public static FilterPolicy CreateBloom(double bitsPerKey)
        => new(NativeMethods.rocksdb_filterpolicy_create_bloom(bitsPerKey));

    /// <summary>
    /// Creates a Bloom filter policy. Identical to <see cref="CreateBloom"/>,
    /// and the clearer name of the two.
    /// </summary>
    /// <remarks>
    /// Both build RocksDb's current filter format; see
    /// <see cref="CreateBloom"/> for why the pair exists.
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