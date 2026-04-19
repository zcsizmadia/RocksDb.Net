using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>Index type for block-based table.</summary>
public enum BlockBasedTableIndexType
{
    BinarySearch       = 0,
    HashSearch         = 1,
    TwoLevelIndexSearch = 2,
}

/// <summary>
/// Options for the block-based table format.
/// Configure and then pass to <see cref="DbOptions.SetBlockBasedTableFactory"/>.
/// Maps to <c>rocksdb_block_based_table_options_t</c>.
/// </summary>
public sealed class BlockBasedTableOptions : RocksDbHandle
{
    public BlockBasedTableOptions()
    {
        Handle = NativeMethods.rocksdb_block_based_options_create();
    }

    /// <summary>Sets the block cache to use for this table. Pass <c>null</c> to disable.</summary>
    public BlockBasedTableOptions SetBlockCache(Cache? cache)
    {
        NativeMethods.rocksdb_block_based_options_set_block_cache(Handle, cache?.Handle ?? nint.Zero);
        return this;
    }

    /// <summary>Disables the block cache entirely.</summary>
    public bool NoBlockCache
    {
        set => NativeMethods.rocksdb_block_based_options_set_no_block_cache(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Block size (in bytes). Defaults to 4 KB.</summary>
    public ulong BlockSize
    {
        set => NativeMethods.rocksdb_block_based_options_set_block_size(Handle, (nuint)value);
    }

    /// <summary>Attaches a filter policy (e.g. Bloom filter).</summary>
    public BlockBasedTableOptions SetFilterPolicy(FilterPolicy? policy)
    {
        NativeMethods.rocksdb_block_based_options_set_filter_policy(Handle, policy?.Handle ?? nint.Zero);
        policy?.TransferOwnership();
        return this;
    }

    /// <summary>If true, the entire key is used for filtering; otherwise only the prefix.</summary>
    public bool WholeKeyFiltering
    {
        set => NativeMethods.rocksdb_block_based_options_set_whole_key_filtering(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Format version of the SST table. Higher versions offer more features.</summary>
    public int FormatVersion
    {
        set => NativeMethods.rocksdb_block_based_options_set_format_version(Handle, value);
    }

    /// <summary>Type of index used in the block-based table.</summary>
    public BlockBasedTableIndexType IndexType
    {
        set => NativeMethods.rocksdb_block_based_options_set_index_type(Handle, (int)value);
    }

    /// <summary>If true, index and filter blocks are stored in the block cache.</summary>
    public bool CacheIndexAndFilterBlocks
    {
        set => NativeMethods.rocksdb_block_based_options_set_cache_index_and_filter_blocks(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, index and filter blocks are given high priority in the block cache.</summary>
    public bool CacheIndexAndFilterBlocksWithHighPriority
    {
        set => NativeMethods.rocksdb_block_based_options_set_cache_index_and_filter_blocks_with_high_priority(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, pin level-0 index and filter blocks in the block cache.</summary>
    public bool PinL0FilterAndIndexBlocksInCache
    {
        set => NativeMethods.rocksdb_block_based_options_set_pin_l0_filter_and_index_blocks_in_cache(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Block size deviation: block is closed once its size is this % smaller than target. Default: 10.</summary>
    public int BlockSizeDeviation
    {
        set => NativeMethods.rocksdb_block_based_options_set_block_size_deviation(Handle, value);
    }

    /// <summary>Number of keys between restart points in data blocks.</summary>
    public int BlockRestartInterval
    {
        set => NativeMethods.rocksdb_block_based_options_set_block_restart_interval(Handle, value);
    }

    /// <summary>If true, use partitioned full filters (requires index type <see cref="BlockBasedTableIndexType.TwoLevelIndexSearch"/>).</summary>
    public bool PartitionFilters
    {
        set => NativeMethods.rocksdb_block_based_options_set_partition_filters(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Target size of metadata blocks in bytes.</summary>
    public ulong MetadataBlockSize
    {
        set => NativeMethods.rocksdb_block_based_options_set_metadata_block_size(Handle, value);
    }

    /// <summary>If true, delta encoding is used for index values to reduce index size.</summary>
    public bool UseDeltaEncoding
    {
        set => NativeMethods.rocksdb_block_based_options_set_use_delta_encoding(Handle, value ? (byte)1 : (byte)0);
    }

    public override void DisposeUnmanagedResources()
    {
        NativeMethods.rocksdb_block_based_options_destroy(Handle);
    }
}
