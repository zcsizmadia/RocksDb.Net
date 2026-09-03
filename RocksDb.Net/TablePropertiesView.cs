namespace RocksDbNet;

/// <summary>
/// A read-only window onto an SST file's properties, valid only for the duration
/// of the callback it was handed to.
/// </summary>
/// <remarks>
/// <para>
/// This exists for <see cref="ReadOptions.SetTableFilter"/>, which RocksDb
/// invokes once per SST file per read. Copying all 58 properties on every one of
/// those calls would be wasteful when a filter typically looks at one or two, so
/// this reads each value straight from the native structure instead.
/// </para>
/// <para>
/// The cost of that is lifetime: the underlying pointer belongs to RocksDb and
/// dies when the callback returns. Using the view after that throws
/// <see cref="InvalidOperationException"/> rather than reading freed memory. To
/// keep the data, call <see cref="ToSnapshot"/> while the view is still live.
/// </para>
/// <para>
/// Only the properties a filter is likely to want are exposed directly.
/// <see cref="ToSnapshot"/> gives the full set.
/// </para>
/// </remarks>
public sealed class TablePropertiesView
{
    private nint _props;

    internal TablePropertiesView(nint props) => _props = props;

    /// <summary>Marks the view dead once the callback that owns it has returned.</summary>
    internal void Invalidate() => _props = nint.Zero;

    private nint Props => _props != nint.Zero
        ? _props
        : throw new InvalidOperationException(
            $"This {nameof(TablePropertiesView)} is no longer valid. RocksDb owns the underlying " +
            $"table properties and frees them when the callback returns. Call {nameof(ToSnapshot)}() " +
            "inside the callback if you need to keep the values.");

    /// <summary>Whether the view still refers to live table properties.</summary>
    public bool IsValid => _props != nint.Zero;

    /// <summary>Number of entries in the file, including deletions and merge operands.</summary>
    public ulong NumEntries => NativeMethods.rocksdb_table_properties_num_entries(Props);

    /// <summary>Number of deletion entries (tombstones) in the file.</summary>
    public ulong NumDeletions => NativeMethods.rocksdb_table_properties_num_deletions(Props);

    /// <summary>Number of range-deletion entries in the file.</summary>
    public ulong NumRangeDeletions => NativeMethods.rocksdb_table_properties_num_range_deletions(Props);

    /// <summary>Number of merge operands in the file.</summary>
    public ulong NumMergeOperands => NativeMethods.rocksdb_table_properties_num_merge_operands(Props);

    /// <summary>Total size of all data blocks, in bytes.</summary>
    public ulong DataSize => NativeMethods.rocksdb_table_properties_data_size(Props);

    /// <summary>Total size of all keys before compression or encoding, in bytes.</summary>
    public ulong RawKeySize => NativeMethods.rocksdb_table_properties_raw_key_size(Props);

    /// <summary>Total size of all values before compression or encoding, in bytes.</summary>
    public ulong RawValueSize => NativeMethods.rocksdb_table_properties_raw_value_size(Props);

    /// <summary>Unix timestamp at which the oldest data in this file was written, or 0 if unknown.</summary>
    public ulong CreationTime => NativeMethods.rocksdb_table_properties_creation_time(Props);

    /// <summary>Unix timestamp of the oldest key in this file, or 0 if unknown.</summary>
    public ulong OldestKeyTime => NativeMethods.rocksdb_table_properties_oldest_key_time(Props);

    /// <summary>Unix timestamp of the newest key in this file, or 0 if unknown.</summary>
    public ulong NewestKeyTime => NativeMethods.rocksdb_table_properties_newest_key_time(Props);

    /// <summary>Unix timestamp at which this file was created, or 0 if unknown.</summary>
    public ulong FileCreationTime => NativeMethods.rocksdb_table_properties_file_creation_time(Props);

    /// <summary>Identifier of the column family this file belongs to.</summary>
    public ulong ColumnFamilyId => NativeMethods.rocksdb_table_properties_column_family_id(Props);

    /// <summary>Whether <see cref="KeySmallestSeqno"/> holds a meaningful value.</summary>
    public bool HasKeySmallestSeqno => NativeMethods.rocksdb_table_properties_has_key_smallest_seqno(Props) != 0;

    /// <summary>Whether <see cref="KeyLargestSeqno"/> holds a meaningful value.</summary>
    public bool HasKeyLargestSeqno => NativeMethods.rocksdb_table_properties_has_key_largest_seqno(Props) != 0;

    /// <summary>
    /// The smallest sequence number in the file. Only meaningful when
    /// <see cref="HasKeySmallestSeqno"/> is <c>true</c>.
    /// </summary>
    public ulong KeySmallestSeqno => NativeMethods.rocksdb_table_properties_key_smallest_seqno(Props);

    /// <summary>
    /// The largest sequence number in the file. Only meaningful when
    /// <see cref="HasKeyLargestSeqno"/> is <c>true</c>.
    /// </summary>
    public ulong KeyLargestSeqno => NativeMethods.rocksdb_table_properties_key_largest_seqno(Props);

    /// <summary>Name of the column family this file belongs to.</summary>
    public unsafe string? ColumnFamilyName
    {
        get
        {
            byte* ptr = NativeMethods.rocksdb_table_properties_column_family_name(Props, out nuint length);
            return ptr is null ? null : NativeMethods.PtrToStringUTF8(ptr, length);
        }
    }

    /// <summary>
    /// Copies every property into a <see cref="TableProperties"/> that outlives
    /// the callback. Call this only while the view is valid.
    /// </summary>
    public TableProperties ToSnapshot() => TableProperties.Copy(Props)!;
}
