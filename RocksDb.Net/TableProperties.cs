namespace RocksDbNet;

/// <summary>
/// Properties of a single SST table file, as reported by RocksDb.
/// Maps to <c>rocksdb_table_properties_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Instances are eager snapshots. The native <c>rocksdb_table_properties_t</c> has
/// no create or destroy function: it is a view over a field inside the owning
/// event-info object, and is only valid while that object is alive, which for a
/// listener means the duration of the callback. Everything is therefore copied
/// out at construction and this object is safe to keep and to hand to other
/// threads.
/// </para>
/// <para>
/// Values that RocksDb does not populate for a given file come back as zero or
/// empty rather than as an error.
/// </para>
/// </remarks>
public sealed record TableProperties
{
    /// <summary>The file number of the original SST file, or 0 if unknown.</summary>
    public ulong OrigFileNumber { get; init; }

    /// <summary>Total size of all data blocks, in bytes.</summary>
    public ulong DataSize { get; init; }

    /// <summary>Total uncompressed size of all data blocks, in bytes.</summary>
    public ulong UncompressedDataSize { get; init; }

    /// <summary>Total size of the index block, in bytes.</summary>
    public ulong IndexSize { get; init; }

    /// <summary>Number of index partitions, for a partitioned index.</summary>
    public ulong IndexPartitions { get; init; }

    /// <summary>Size of the top-level index, in bytes, for a partitioned index.</summary>
    public ulong TopLevelIndexSize { get; init; }

    /// <summary>Whether index keys are user keys rather than internal keys.</summary>
    public bool IndexKeyIsUserKey { get; init; }

    /// <summary>Whether index values are delta encoded.</summary>
    public bool IndexValueIsDeltaEncoded { get; init; }

    /// <summary>Whether a user-defined index is the primary index for this file.</summary>
    public bool UdiIsPrimaryIndex { get; init; }

    /// <summary>Total size of the filter block, in bytes.</summary>
    public ulong FilterSize { get; init; }

    /// <summary>Total size of all keys before compression or encoding, in bytes.</summary>
    public ulong RawKeySize { get; init; }

    /// <summary>Total size of all values before compression or encoding, in bytes.</summary>
    public ulong RawValueSize { get; init; }

    /// <summary>Number of data blocks in the file.</summary>
    public ulong NumDataBlocks { get; init; }

    /// <summary>Number of data blocks whose compressed form was rejected as not worthwhile.</summary>
    public ulong NumDataBlocksCompressionRejected { get; init; }

    /// <summary>Number of data blocks for which compression was skipped entirely.</summary>
    public ulong NumDataBlocksCompressionBypassed { get; init; }

    /// <summary>Number of blocks holding uniformly sized entries.</summary>
    public ulong NumUniformBlocks { get; init; }

    /// <summary>Number of entries in the file, including deletions and merge operands.</summary>
    public ulong NumEntries { get; init; }

    /// <summary>Number of entries added to the filter.</summary>
    public ulong NumFilterEntries { get; init; }

    /// <summary>Number of deletion entries (tombstones) in the file.</summary>
    public ulong NumDeletions { get; init; }

    /// <summary>Number of merge operands in the file.</summary>
    public ulong NumMergeOperands { get; init; }

    /// <summary>Number of range-deletion entries in the file.</summary>
    public ulong NumRangeDeletions { get; init; }

    /// <summary>The SST format version this file was written with.</summary>
    public ulong FormatVersion { get; init; }

    /// <summary>The fixed key length, or 0 when keys are variable length.</summary>
    public ulong FixedKeyLen { get; init; }

    /// <summary>Identifier of the column family this file belongs to.</summary>
    public ulong ColumnFamilyId { get; init; }

    /// <summary>Unix timestamp at which the oldest data in this file was written, or 0 if unknown.</summary>
    public ulong CreationTime { get; init; }

    /// <summary>Unix timestamp of the oldest key in this file, or 0 if unknown.</summary>
    public ulong OldestKeyTime { get; init; }

    /// <summary>Unix timestamp of the newest key in this file, or 0 if unknown.</summary>
    public ulong NewestKeyTime { get; init; }

    /// <summary>Unix timestamp at which this file was created, or 0 if unknown.</summary>
    public ulong FileCreationTime { get; init; }

    /// <summary>Estimated data size had the slower compression been used, in bytes.</summary>
    public ulong SlowCompressionEstimatedDataSize { get; init; }

    /// <summary>Estimated data size had the faster compression been used, in bytes.</summary>
    public ulong FastCompressionEstimatedDataSize { get; init; }

    /// <summary>Offset of the global sequence number for an ingested external file.</summary>
    public ulong ExternalSstFileGlobalSeqnoOffset { get; init; }

    /// <summary>Offset at which the file's metadata tail begins, in bytes.</summary>
    public ulong TailStartOffset { get; init; }

    /// <summary>Whether user-defined timestamps are persisted in this file.</summary>
    public bool UserDefinedTimestampsPersisted { get; init; }

    /// <summary>
    /// The largest sequence number in the file. Only meaningful when
    /// <see cref="HasKeyLargestSeqno"/> is <c>true</c>.
    /// </summary>
    public ulong KeyLargestSeqno { get; init; }

    /// <summary>
    /// The smallest sequence number in the file. Only meaningful when
    /// <see cref="HasKeySmallestSeqno"/> is <c>true</c>.
    /// </summary>
    public ulong KeySmallestSeqno { get; init; }

    /// <summary>Restart interval used for data blocks.</summary>
    public ulong DataBlockRestartInterval { get; init; }

    /// <summary>Restart interval used for index blocks.</summary>
    public ulong IndexBlockRestartInterval { get; init; }

    /// <summary>Whether keys and values are stored separately within data blocks.</summary>
    public bool SeparateKeyValueInDataBlock { get; init; }

    /// <summary>Whether <see cref="KeyLargestSeqno"/> holds a meaningful value.</summary>
    public bool HasKeyLargestSeqno { get; init; }

    /// <summary>Whether <see cref="KeySmallestSeqno"/> holds a meaningful value.</summary>
    public bool HasKeySmallestSeqno { get; init; }

    /// <summary>Identifier of the database that produced this file.</summary>
    public string? DbId { get; init; }

    /// <summary>Identifier of the database session that produced this file.</summary>
    public string? DbSessionId { get; init; }

    /// <summary>Host identifier of the machine that produced this file.</summary>
    public string? DbHostId { get; init; }

    /// <summary>Name of the column family this file belongs to.</summary>
    public string? ColumnFamilyName { get; init; }

    /// <summary>Name of the filter policy used, or empty when no filter was written.</summary>
    public string? FilterPolicyName { get; init; }

    /// <summary>Name of the comparator the keys are ordered by.</summary>
    public string? ComparatorName { get; init; }

    /// <summary>Name of the merge operator, or empty when none was configured.</summary>
    public string? MergeOperatorName { get; init; }

    /// <summary>Name of the prefix extractor, or empty when none was configured.</summary>
    public string? PrefixExtractorName { get; init; }

    /// <summary>Names of the table-properties collectors that ran, as reported by RocksDb.</summary>
    public string? PropertyCollectorsNames { get; init; }

    /// <summary>Name of the compression algorithm used for data blocks.</summary>
    public string? CompressionName { get; init; }

    /// <summary>Compression options in effect, in RocksDb's textual form.</summary>
    public string? CompressionOptions { get; init; }

    /// <summary>
    /// Encoded mapping from sequence numbers to write times. Exposed as raw bytes
    /// because the encoding is internal to RocksDb.
    /// </summary>
    public byte[] SeqnoToTimeMapping { get; init; } = [];

    /// <summary>
    /// Properties written by table-properties collectors, keyed by collector-defined
    /// name. Values are raw bytes, since collectors may store binary data. RocksDb
    /// contributes some entries of its own.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> UserCollectedProperties { get; init; }
        = new Dictionary<string, byte[]>();

    /// <summary>
    /// The same properties as <see cref="UserCollectedProperties"/>, rendered by
    /// RocksDb into human-readable strings.
    /// </summary>
    public IReadOnlyDictionary<string, string> ReadableProperties { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Copies every property out of a borrowed native pointer.
    /// </summary>
    /// <param name="props">
    /// A <c>rocksdb_table_properties_t*</c> that is valid for the duration of the
    /// call. <see cref="nint.Zero"/> yields <c>null</c>, which is what RocksDb
    /// reports when a file has no properties attached.
    /// </param>
    internal static unsafe TableProperties? Copy(nint props)
    {
        if (props == nint.Zero)
        {
            return null;
        }

        return new TableProperties
        {
            OrigFileNumber = NativeMethods.rocksdb_table_properties_orig_file_number(props),
            DataSize = NativeMethods.rocksdb_table_properties_data_size(props),
            UncompressedDataSize = NativeMethods.rocksdb_table_properties_uncompressed_data_size(props),
            IndexSize = NativeMethods.rocksdb_table_properties_index_size(props),
            IndexPartitions = NativeMethods.rocksdb_table_properties_index_partitions(props),
            TopLevelIndexSize = NativeMethods.rocksdb_table_properties_top_level_index_size(props),
            IndexKeyIsUserKey = NativeMethods.rocksdb_table_properties_index_key_is_user_key(props) != 0,
            IndexValueIsDeltaEncoded = NativeMethods.rocksdb_table_properties_index_value_is_delta_encoded(props) != 0,
            UdiIsPrimaryIndex = NativeMethods.rocksdb_table_properties_udi_is_primary_index(props) != 0,
            FilterSize = NativeMethods.rocksdb_table_properties_filter_size(props),
            RawKeySize = NativeMethods.rocksdb_table_properties_raw_key_size(props),
            RawValueSize = NativeMethods.rocksdb_table_properties_raw_value_size(props),
            NumDataBlocks = NativeMethods.rocksdb_table_properties_num_data_blocks(props),
            NumDataBlocksCompressionRejected = NativeMethods.rocksdb_table_properties_num_data_blocks_compression_rejected(props),
            NumDataBlocksCompressionBypassed = NativeMethods.rocksdb_table_properties_num_data_blocks_compression_bypassed(props),
            NumUniformBlocks = NativeMethods.rocksdb_table_properties_num_uniform_blocks(props),
            NumEntries = NativeMethods.rocksdb_table_properties_num_entries(props),
            NumFilterEntries = NativeMethods.rocksdb_table_properties_num_filter_entries(props),
            NumDeletions = NativeMethods.rocksdb_table_properties_num_deletions(props),
            NumMergeOperands = NativeMethods.rocksdb_table_properties_num_merge_operands(props),
            NumRangeDeletions = NativeMethods.rocksdb_table_properties_num_range_deletions(props),
            FormatVersion = NativeMethods.rocksdb_table_properties_format_version(props),
            FixedKeyLen = NativeMethods.rocksdb_table_properties_fixed_key_len(props),
            ColumnFamilyId = NativeMethods.rocksdb_table_properties_column_family_id(props),
            CreationTime = NativeMethods.rocksdb_table_properties_creation_time(props),
            OldestKeyTime = NativeMethods.rocksdb_table_properties_oldest_key_time(props),
            NewestKeyTime = NativeMethods.rocksdb_table_properties_newest_key_time(props),
            FileCreationTime = NativeMethods.rocksdb_table_properties_file_creation_time(props),
            SlowCompressionEstimatedDataSize = NativeMethods.rocksdb_table_properties_slow_compression_estimated_data_size(props),
            FastCompressionEstimatedDataSize = NativeMethods.rocksdb_table_properties_fast_compression_estimated_data_size(props),
            ExternalSstFileGlobalSeqnoOffset = NativeMethods.rocksdb_table_properties_external_sst_file_global_seqno_offset(props),
            TailStartOffset = NativeMethods.rocksdb_table_properties_tail_start_offset(props),
            UserDefinedTimestampsPersisted = NativeMethods.rocksdb_table_properties_user_defined_timestamps_persisted(props) != 0,
            KeyLargestSeqno = NativeMethods.rocksdb_table_properties_key_largest_seqno(props),
            KeySmallestSeqno = NativeMethods.rocksdb_table_properties_key_smallest_seqno(props),
            DataBlockRestartInterval = NativeMethods.rocksdb_table_properties_data_block_restart_interval(props),
            IndexBlockRestartInterval = NativeMethods.rocksdb_table_properties_index_block_restart_interval(props),
            SeparateKeyValueInDataBlock = NativeMethods.rocksdb_table_properties_separate_key_value_in_data_block(props) != 0,
            HasKeyLargestSeqno = NativeMethods.rocksdb_table_properties_has_key_largest_seqno(props) != 0,
            HasKeySmallestSeqno = NativeMethods.rocksdb_table_properties_has_key_smallest_seqno(props) != 0,

            DbId = ReadString(NativeMethods.rocksdb_table_properties_db_id(props, out nuint dbIdLen), dbIdLen),
            DbSessionId = ReadString(NativeMethods.rocksdb_table_properties_db_session_id(props, out nuint sessionLen), sessionLen),
            DbHostId = ReadString(NativeMethods.rocksdb_table_properties_db_host_id(props, out nuint hostLen), hostLen),
            ColumnFamilyName = ReadString(NativeMethods.rocksdb_table_properties_column_family_name(props, out nuint cfLen), cfLen),
            FilterPolicyName = ReadString(NativeMethods.rocksdb_table_properties_filter_policy_name(props, out nuint filterLen), filterLen),
            ComparatorName = ReadString(NativeMethods.rocksdb_table_properties_comparator_name(props, out nuint cmpLen), cmpLen),
            MergeOperatorName = ReadString(NativeMethods.rocksdb_table_properties_merge_operator_name(props, out nuint mergeLen), mergeLen),
            PrefixExtractorName = ReadString(NativeMethods.rocksdb_table_properties_prefix_extractor_name(props, out nuint prefixLen), prefixLen),
            PropertyCollectorsNames = ReadString(NativeMethods.rocksdb_table_properties_property_collectors_names(props, out nuint collectorsLen), collectorsLen),
            CompressionName = ReadString(NativeMethods.rocksdb_table_properties_compression_name(props, out nuint compressionLen), compressionLen),
            CompressionOptions = ReadString(NativeMethods.rocksdb_table_properties_compression_options(props, out nuint compressionOptsLen), compressionOptsLen),
            SeqnoToTimeMapping = ReadBytes(NativeMethods.rocksdb_table_properties_seqno_to_time_mapping(props, out nuint seqnoLen), seqnoLen),

            UserCollectedProperties = ReadUserCollectedProperties(props),
            ReadableProperties = ReadReadableProperties(props),
        };
    }

    private static unsafe string? ReadString(byte* ptr, nuint len)
        => ptr is null ? null : NativeMethods.PtrToStringUTF8(ptr, len);

    private static unsafe byte[] ReadBytes(byte* ptr, nuint len)
        => ptr is null || len == 0 ? [] : new ReadOnlySpan<byte>(ptr, checked((int)len)).ToArray();

    private static unsafe Dictionary<string, byte[]> ReadUserCollectedProperties(nint props)
    {
        nuint count = NativeMethods.rocksdb_table_properties_user_collected_properties_count(props);
        var result = new Dictionary<string, byte[]>(checked((int)count));

        // Both accessors index into a std::map, which is O(n) per lookup on the
        // native side, so walk each position exactly once.
        for (nuint i = 0; i < count; i++)
        {
            byte* keyPtr = NativeMethods.rocksdb_table_properties_user_collected_properties_key_at(props, i, out nuint keyLen);
            string? key = ReadString(keyPtr, keyLen);
            if (key is null)
            {
                continue;
            }

            byte* valuePtr = NativeMethods.rocksdb_table_properties_user_collected_properties_value_at(props, i, out nuint valueLen);
            result[key] = ReadBytes(valuePtr, valueLen);
        }

        return result;
    }

    private static unsafe Dictionary<string, string> ReadReadableProperties(nint props)
    {
        nuint count = NativeMethods.rocksdb_table_properties_readable_properties_count(props);
        var result = new Dictionary<string, string>(checked((int)count));

        for (nuint i = 0; i < count; i++)
        {
            byte* keyPtr = NativeMethods.rocksdb_table_properties_readable_properties_key_at(props, i, out nuint keyLen);
            string? key = ReadString(keyPtr, keyLen);
            if (key is null)
            {
                continue;
            }

            byte* valuePtr = NativeMethods.rocksdb_table_properties_readable_properties_value_at(props, i, out nuint valueLen);
            result[key] = ReadString(valuePtr, valueLen) ?? string.Empty;
        }

        return result;
    }
}
