using System.Runtime.InteropServices;
using System.Text;

namespace RocksDbNet;

/// <summary>Index type for block-based table.</summary>
public enum BlockBasedTableIndexType
{
    BinarySearch = 0,
    HashSearch = 1,
    TwoLevelIndexSearch = 2,
}

/// <summary>
/// Index type used inside a data block, which controls how a key is located
/// once its block has been read.
/// </summary>
public enum DataBlockIndexType
{
    /// <summary>Binary search over the block's restart points.</summary>
    BinarySearch = 0,

    /// <summary>
    /// Binary search plus a hash table, which makes point lookups faster at the
    /// cost of some space. See
    /// <see cref="BlockBasedTableOptions.DataBlockHashTableUtilRatio"/>.
    /// </summary>
    BinarySearchAndHash = 1,
}

/// <summary>Search algorithm used within an index block.</summary>
public enum IndexBlockSearchType
{
    /// <summary>Binary search.</summary>
    Binary = 0,

    /// <summary>Interpolation search, which can beat binary search on uniform keys.</summary>
    Interpolation = 1,

    /// <summary>Let RocksDb choose.</summary>
    Auto = 2,
}

/// <summary>
/// How much of the index key RocksDb may discard to save space.
/// </summary>
/// <remarks>
/// Mirrored from <c>BlockBasedTableOptions::IndexShorteningMode</c> in
/// <c>include/rocksdb/table.h</c> because the C API declares this parameter as a
/// plain <c>int</c>.
/// </remarks>
public enum IndexShortening
{
    /// <summary>Store full keys in the index.</summary>
    NoShortening = 0,

    /// <summary>
    /// Shorten the separator keys between blocks, but keep the last index key
    /// whole since it bounds the file.
    /// </summary>
    ShortenSeparators = 1,

    /// <summary>Shorten both the separators and the key after the last block.</summary>
    ShortenSeparatorsAndSuccessor = 2,
}

/// <summary>
/// Whether newly written blocks are inserted into the block cache before anyone
/// reads them.
/// </summary>
/// <remarks>
/// Mirrored from <c>BlockBasedTableOptions::PrepopulateBlockCache</c> in
/// <c>include/rocksdb/table.h</c> because the C API declares this parameter as a
/// plain <c>int</c>.
/// </remarks>
public enum PrepopulateBlockCache
{
    /// <summary>Do not prepopulate.</summary>
    Disable = 0,

    /// <summary>Prepopulate blocks written by a flush.</summary>
    FlushOnly = 1,

    /// <summary>
    /// Prepopulate blocks written by both flush and compaction. Flush-warmed
    /// blocks enter at low priority and compaction-warmed blocks at bottom
    /// priority.
    /// </summary>
    FlushAndCompaction = 2,
}

/// <summary>
/// Options for the block-based table format.
/// Configure and then pass to <see cref="DbOptions.BlockBasedTableFactory"/>.
/// Maps to <c>rocksdb_block_based_table_options_t</c>.
/// </summary>
public sealed class BlockBasedTableOptions : RocksDbHandle
{
    public BlockBasedTableOptions()
        : base(NativeMethods.rocksdb_block_based_options_create())
    {
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
        get => NativeMethods.rocksdb_block_based_options_get_no_block_cache(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_no_block_cache(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Block size (in bytes). Defaults to 4 KB.</summary>
    public ulong BlockSize
    {
        get => (ulong)NativeMethods.rocksdb_block_based_options_get_block_size(Handle);
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
        get => NativeMethods.rocksdb_block_based_options_get_whole_key_filtering(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_whole_key_filtering(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Format version of the SST table. Higher versions offer more features.</summary>
    public int FormatVersion
    {
        // The native getter returns uint while the setter takes int.
        get => checked((int)NativeMethods.rocksdb_block_based_options_get_format_version(Handle));
        set => NativeMethods.rocksdb_block_based_options_set_format_version(Handle, value);
    }

    /// <summary>Type of index used in the block-based table.</summary>
    public BlockBasedTableIndexType IndexType
    {
        get => (BlockBasedTableIndexType)NativeMethods.rocksdb_block_based_options_get_index_type(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_index_type(Handle, (int)value);
    }

    /// <summary>If true, index and filter blocks are stored in the block cache.</summary>
    public bool CacheIndexAndFilterBlocks
    {
        get => NativeMethods.rocksdb_block_based_options_get_cache_index_and_filter_blocks(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_cache_index_and_filter_blocks(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, index and filter blocks are given high priority in the block cache.</summary>
    public bool CacheIndexAndFilterBlocksWithHighPriority
    {
        get => NativeMethods.rocksdb_block_based_options_get_cache_index_and_filter_blocks_with_high_priority(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_cache_index_and_filter_blocks_with_high_priority(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, pin level-0 index and filter blocks in the block cache.</summary>
    public bool PinL0FilterAndIndexBlocksInCache
    {
        get => NativeMethods.rocksdb_block_based_options_get_pin_l0_filter_and_index_blocks_in_cache(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_pin_l0_filter_and_index_blocks_in_cache(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Block size deviation: block is closed once its size is this % smaller than target. Default: 10.</summary>
    public int BlockSizeDeviation
    {
        get => NativeMethods.rocksdb_block_based_options_get_block_size_deviation(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_block_size_deviation(Handle, value);
    }

    /// <summary>Number of keys between restart points in data blocks.</summary>
    public int BlockRestartInterval
    {
        get => NativeMethods.rocksdb_block_based_options_get_block_restart_interval(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_block_restart_interval(Handle, value);
    }

    /// <summary>If true, use partitioned full filters (requires index type <see cref="BlockBasedTableIndexType.TwoLevelIndexSearch"/>).</summary>
    public bool PartitionFilters
    {
        get => NativeMethods.rocksdb_block_based_options_get_partition_filters(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_partition_filters(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Target size of metadata blocks in bytes.</summary>
    public ulong MetadataBlockSize
    {
        get => NativeMethods.rocksdb_block_based_options_get_metadata_block_size(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_metadata_block_size(Handle, value);
    }

    /// <summary>If true, delta encoding is used for index values to reduce index size.</summary>
    public bool UseDeltaEncoding
    {
        get => NativeMethods.rocksdb_block_based_options_get_use_delta_encoding(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_use_delta_encoding(Handle, value ? (byte)1 : (byte)0);
    }

    // ── Layout, checksums and index encoding ─────────────────────────────────

    /// <summary>
    /// If true, data blocks are aligned to the block size, which lets the
    /// filesystem read one block without straddling a page boundary.
    /// </summary>
    public bool BlockAlign
    {
        get => NativeMethods.rocksdb_block_based_options_get_block_align(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_block_align(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Checksum algorithm used for block integrity. RocksDb does not publish
    /// these values through the C API, so this stays an <c>int</c>.
    /// </summary>
    public int Checksum
    {
        get => NativeMethods.rocksdb_block_based_options_get_checksum(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_checksum(Handle, checked((sbyte)value));
    }

    /// <summary>Index type used inside a data block.</summary>
    public DataBlockIndexType DataBlockIndexType
    {
        get => (DataBlockIndexType)NativeMethods.rocksdb_block_based_options_get_data_block_index_type(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_data_block_index_type(Handle, (int)value);
    }

    /// <summary>Number of keys between restart points in index blocks.</summary>
    public int IndexBlockRestartInterval
    {
        get => NativeMethods.rocksdb_block_based_options_get_index_block_restart_interval(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_index_block_restart_interval(Handle, value);
    }

    /// <summary>Search algorithm used within an index block.</summary>
    public IndexBlockSearchType IndexBlockSearchType
    {
        get => (IndexBlockSearchType)NativeMethods.rocksdb_block_based_options_get_index_block_search_type(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_index_block_search_type(Handle, (int)value);
    }

    /// <summary>
    /// If true, filters are built to use memory in block-size units, which cuts
    /// the memory a bloom filter wastes at the cost of a slightly higher false
    /// positive rate.
    /// </summary>
    public bool OptimizeFiltersForMemory
    {
        get => NativeMethods.rocksdb_block_based_options_get_optimize_filters_for_memory(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_optimize_filters_for_memory(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, the top-level index of a partitioned index and filter is pinned
    /// in memory rather than left to be evicted from the block cache.
    /// </summary>
    /// <remarks>
    /// The C API models this as a flag, not as one of the pinning-tier values,
    /// so it is exposed as a <c>bool</c>.
    /// </remarks>
    public bool PinTopLevelIndexAndFilter
    {
        get => NativeMethods.rocksdb_block_based_options_get_pin_top_level_index_and_filter(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_pin_top_level_index_and_filter(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, keys and values are stored in separate regions of a data block,
    /// which compresses better for workloads with similar keys.
    /// </summary>
    public bool SeparateKeyValueInDataBlock
    {
        get => NativeMethods.rocksdb_block_based_options_get_separate_key_value_in_data_block(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_separate_key_value_in_data_block(Handle, value ? (byte)1 : (byte)0);
    }

    // ── Readahead, filters and user-defined indexes ──────────────────────────

    /// <summary>How much of the index key RocksDb may discard to save space.</summary>
    public IndexShortening IndexShortening
    {
        get => (IndexShortening)NativeMethods.rocksdb_block_based_options_get_index_shortening(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_index_shortening(Handle, (int)value);
    }

    /// <summary>Whether newly written blocks are inserted into the block cache eagerly.</summary>
    public PrepopulateBlockCache PrepopulateBlockCache
    {
        get => (PrepopulateBlockCache)NativeMethods.rocksdb_block_based_options_get_prepopulate_block_cache(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_prepopulate_block_cache(Handle, (int)value);
    }

    /// <summary>
    /// Readahead size in bytes an iterator starts with before adaptive readahead
    /// grows it. 0 lets RocksDb choose.
    /// </summary>
    public ulong InitialAutoReadaheadSize
    {
        get => (ulong)NativeMethods.rocksdb_block_based_options_get_initial_auto_readahead_size(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_initial_auto_readahead_size(Handle, (nuint)value);
    }

    /// <summary>Upper bound in bytes on how far adaptive readahead will grow.</summary>
    public ulong MaxAutoReadaheadSize
    {
        get => (ulong)NativeMethods.rocksdb_block_based_options_get_max_auto_readahead_size(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_max_auto_readahead_size(Handle, (nuint)value);
    }

    /// <summary>
    /// Number of sequential file reads before adaptive readahead kicks in.
    /// </summary>
    public ulong NumFileReadsForAutoReadahead
    {
        get => NativeMethods.rocksdb_block_based_options_get_num_file_reads_for_auto_readahead(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_num_file_reads_for_auto_readahead(Handle, value);
    }

    /// <summary>
    /// Bytes per bit of the read-amplification estimator. 0 disables it. Must be
    /// a power of two when set.
    /// </summary>
    public uint ReadAmpBytesPerBit
    {
        get => NativeMethods.rocksdb_block_based_options_get_read_amp_bytes_per_bit(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_read_amp_bytes_per_bit(Handle, value);
    }

    /// <summary>If true, index blocks are compressed.</summary>
    public bool EnableIndexCompression
    {
        get => NativeMethods.rocksdb_block_based_options_get_enable_index_compression(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_enable_index_compression(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, each compressed block is decompressed again and compared, so a
    /// broken compression library is caught at write time rather than at read
    /// time. Expensive.
    /// </summary>
    public bool VerifyCompression
    {
        get => NativeMethods.rocksdb_block_based_options_get_verify_compression(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_verify_compression(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, filters are checksummed as they are built, catching corruption
    /// that happens during filter construction.
    /// </summary>
    public bool DetectFilterConstructCorruption
    {
        get => NativeMethods.rocksdb_block_based_options_get_detect_filter_construct_corruption(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_detect_filter_construct_corruption(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, partitioned filters are stored independently of the index, so a
    /// filter partition need not match an index partition.
    /// </summary>
    public bool DecouplePartitionedFilters
    {
        get => NativeMethods.rocksdb_block_based_options_get_decouple_partitioned_filters(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_decouple_partitioned_filters(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Fraction of a data block's space given to its hash table, when
    /// <see cref="DataBlockIndexType"/> is
    /// <see cref="RocksDbNet.DataBlockIndexType.BinarySearchAndHash"/>. Higher
    /// values trade space for faster point lookups.
    /// </summary>
    public double DataBlockHashTableUtilRatio
    {
        get => NativeMethods.rocksdb_block_based_options_get_data_block_hash_table_util_ratio(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_data_block_hash_table_util_ratio(Handle, value);
    }

    /// <summary>Alignment in bytes of the super block. 0 disables alignment.</summary>
    public ulong SuperBlockAlignmentSize
    {
        get => (ulong)NativeMethods.rocksdb_block_based_options_get_super_block_alignment_size(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_super_block_alignment_size(Handle, (nuint)value);
    }

    /// <summary>
    /// Space overhead RocksDb will accept, as a ratio, in exchange for super
    /// block alignment.
    /// </summary>
    public ulong SuperBlockAlignmentSpaceOverheadRatio
    {
        get => (ulong)NativeMethods.rocksdb_block_based_options_get_super_block_alignment_space_overhead_ratio(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_super_block_alignment_space_overhead_ratio(Handle, (nuint)value);
    }

    /// <summary>
    /// Threshold at which blocks of uniformly sized entries get the compact
    /// uniform encoding.
    /// </summary>
    public double UniformCvThreshold
    {
        get => NativeMethods.rocksdb_block_based_options_get_uniform_cv_threshold(Handle);
        set => NativeMethods.rocksdb_block_based_options_set_uniform_cv_threshold(Handle, value);
    }

    /// <summary>
    /// If true, a configured user-defined index is used as the primary index
    /// rather than as an extra one.
    /// </summary>
    public bool UseUdiAsPrimaryIndex
    {
        get => NativeMethods.rocksdb_block_based_options_get_use_udi_as_primary_index(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_use_udi_as_primary_index(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, opening a file that has no user-defined index fails instead of
    /// falling back to the built-in index.
    /// </summary>
    public bool FailIfNoUdiOnOpen
    {
        get => NativeMethods.rocksdb_block_based_options_get_fail_if_no_udi_on_open(Handle) != 0;
        set => NativeMethods.rocksdb_block_based_options_set_fail_if_no_udi_on_open(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Name of the user-defined index factory in use, or <c>null</c> when none is
    /// configured.
    /// </summary>
    public unsafe string? UserDefinedIndexFactoryName
    {
        get
        {
            byte* ptr = NativeMethods.rocksdb_block_based_options_get_user_defined_index_factory_name(Handle, out nuint length);
            return ptr is null ? null : NativeMethods.PtrToStringUTF8(ptr, length);
        }
    }

    /// <summary>
    /// Selects a user-defined index factory by its RocksDb configuration string.
    /// </summary>
    /// <exception cref="RocksDbException">The string does not name a known factory.</exception>
    public unsafe BlockBasedTableOptions SetUserDefinedIndexFactoryFromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        nint err = default;
        fixed (byte* p = bytes)
            NativeMethods.rocksdb_block_based_options_set_user_defined_index_factory_from_string(Handle, p, (nuint)bytes.Length, ref err);
        NativeMethods.ThrowOnError(err);
        return this;
    }

    /// <summary>Removes any user-defined index factory from these options.</summary>
    public BlockBasedTableOptions ClearUserDefinedIndexFactory()
    {
        NativeMethods.rocksdb_block_based_options_clear_user_defined_index_factory(Handle);
        return this;
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_block_based_options_destroy(Handle);
    }
}
