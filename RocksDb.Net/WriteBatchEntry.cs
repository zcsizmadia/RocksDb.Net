namespace RocksDbNet;

/// <summary>What kind of operation a <see cref="WriteBatchEntry"/> records.</summary>
public enum WriteBatchEntryKind
{
    /// <summary>A key was written.</summary>
    Put,

    /// <summary>A key was deleted.</summary>
    Delete,

    /// <summary>A merge operand was queued for a key.</summary>
    Merge,

    /// <summary>
    /// An application blob written with
    /// <see cref="WriteBatch.PutLogData(System.ReadOnlySpan{byte})"/>. It is
    /// carried in the write-ahead log but never stored against a key.
    /// </summary>
    LogData,
}

/// <summary>
/// One operation read back out of a <see cref="WriteBatch"/>.
/// </summary>
/// <param name="Kind">Which operation this is.</param>
/// <param name="ColumnFamilyId">
/// The numeric id of the column family the operation applies to. Zero is the
/// default family. Match it against
/// <see cref="ColumnFamilyHandle.Id"/>, since the batch records ids rather than
/// names. Meaningless when <paramref name="Kind"/> is
/// <see cref="WriteBatchEntryKind.LogData"/>.
/// </param>
/// <param name="Key">
/// The key, or empty for <see cref="WriteBatchEntryKind.LogData"/>.
/// </param>
/// <param name="Value">
/// The value for a put, the operand for a merge, the blob for log data, and
/// <see langword="null"/> for a delete.
/// </param>
public readonly record struct WriteBatchEntry(
    WriteBatchEntryKind Kind,
    uint ColumnFamilyId,
    byte[] Key,
    byte[]? Value);
