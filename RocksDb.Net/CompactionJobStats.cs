namespace RocksDbNet;

/// <summary>
/// Statistics for a completed compaction job.
/// Maps to <c>rocksdb_compaction_job_stats_t</c>.
/// </summary>
/// <remarks>
/// Like <see cref="TableProperties"/>, the native struct is a borrowed view into
/// the owning job-info object and has no destroy function, so instances of this
/// type are eager copies that are safe to keep beyond the callback.
/// </remarks>
public sealed record CompactionJobStats
{
    /// <summary>Wall-clock time the compaction took, in microseconds.</summary>
    public ulong ElapsedMicros { get; init; }

    /// <summary>CPU time the compaction consumed, in microseconds.</summary>
    public ulong CpuMicros { get; init; }

    /// <summary>
    /// Whether <see cref="NumInputRecords"/> is exact. RocksDb may only estimate it.
    /// </summary>
    public bool HasAccurateNumInputRecords { get; init; }

    /// <summary>Number of input records read.</summary>
    public ulong NumInputRecords { get; init; }

    /// <summary>Number of blobs read from blob files.</summary>
    public ulong NumBlobsRead { get; init; }

    /// <summary>Number of input files.</summary>
    public ulong NumInputFiles { get; init; }

    /// <summary>Number of input files that were moved rather than rewritten.</summary>
    public ulong NumInputFilesTriviallyMoved { get; init; }

    /// <summary>Number of input files that were already at the output level.</summary>
    public ulong NumInputFilesAtOutputLevel { get; init; }

    /// <summary>Number of input files skipped entirely by a compaction filter.</summary>
    public ulong NumFilteredInputFiles { get; init; }

    /// <summary>Number of skipped input files that were at the output level.</summary>
    public ulong NumFilteredInputFilesAtOutputLevel { get; init; }

    /// <summary>Number of records written.</summary>
    public ulong NumOutputRecords { get; init; }

    /// <summary>Number of output files produced.</summary>
    public ulong NumOutputFiles { get; init; }

    /// <summary>Number of output blob files produced.</summary>
    public ulong NumOutputFilesBlob { get; init; }

    /// <summary>Whether the compaction covered every file in the column family.</summary>
    public bool IsFullCompaction { get; init; }

    /// <summary>Whether the compaction was requested by the application.</summary>
    public bool IsManualCompaction { get; init; }

    /// <summary>Whether the compaction ran on a remote worker.</summary>
    public bool IsRemoteCompaction { get; init; }

    /// <summary>Total size of all input files, in bytes.</summary>
    public ulong TotalInputBytes { get; init; }

    /// <summary>Total bytes read from blob files.</summary>
    public ulong TotalBlobBytesRead { get; init; }

    /// <summary>Total size of all output files, in bytes.</summary>
    public ulong TotalOutputBytes { get; init; }

    /// <summary>Total size of all output blob files, in bytes.</summary>
    public ulong TotalOutputBytesBlob { get; init; }

    /// <summary>Total input bytes that were skipped rather than processed.</summary>
    public ulong TotalSkippedInputBytes { get; init; }

    /// <summary>Number of records superseded by a newer version of the same key.</summary>
    public ulong NumRecordsReplaced { get; init; }

    /// <summary>Total size of input keys before compression or encoding, in bytes.</summary>
    public ulong TotalInputRawKeyBytes { get; init; }

    /// <summary>Total size of input values before compression or encoding, in bytes.</summary>
    public ulong TotalInputRawValueBytes { get; init; }

    /// <summary>Number of deletion entries among the inputs.</summary>
    public ulong NumInputDeletionRecords { get; init; }

    /// <summary>Number of deletion entries that were dropped as no longer needed.</summary>
    public ulong NumExpiredDeletionRecords { get; init; }

    /// <summary>Number of keys that could not be parsed.</summary>
    public ulong NumCorruptKeys { get; init; }

    /// <summary>Time spent writing files, in nanoseconds.</summary>
    public ulong FileWriteNanos { get; init; }

    /// <summary>Time spent in range sync, in nanoseconds.</summary>
    public ulong FileRangeSyncNanos { get; init; }

    /// <summary>Time spent in fsync, in nanoseconds.</summary>
    public ulong FileFsyncNanos { get; init; }

    /// <summary>Time spent preparing writes, in nanoseconds.</summary>
    public ulong FilePrepareWriteNanos { get; init; }

    /// <summary>
    /// Prefix of the smallest output key. Raw bytes, since keys are not
    /// necessarily text.
    /// </summary>
    public byte[] SmallestOutputKeyPrefix { get; init; } = [];

    /// <summary>
    /// Prefix of the largest output key. Raw bytes, since keys are not
    /// necessarily text.
    /// </summary>
    public byte[] LargestOutputKeyPrefix { get; init; } = [];

    /// <summary>Number of single deletions that found no matching put.</summary>
    public ulong NumSingleDelFallthru { get; init; }

    /// <summary>Number of single deletions that did not match the expected put.</summary>
    public ulong NumSingleDelMismatch { get; init; }

    /// <summary>
    /// Copies every statistic out of a borrowed native pointer, which is only
    /// valid for the duration of the call.
    /// </summary>
    internal static unsafe CompactionJobStats? Copy(nint stats)
    {
        if (stats == nint.Zero)
        {
            return null;
        }

        byte* smallest = NativeMethods.rocksdb_compaction_job_stats_smallest_output_key_prefix(stats, out nuint smallestLen);
        byte* largest = NativeMethods.rocksdb_compaction_job_stats_largest_output_key_prefix(stats, out nuint largestLen);

        return new CompactionJobStats
        {
            ElapsedMicros = NativeMethods.rocksdb_compaction_job_stats_elapsed_micros(stats),
            CpuMicros = NativeMethods.rocksdb_compaction_job_stats_cpu_micros(stats),
            HasAccurateNumInputRecords = NativeMethods.rocksdb_compaction_job_stats_has_accurate_num_input_records(stats) != 0,
            NumInputRecords = NativeMethods.rocksdb_compaction_job_stats_num_input_records(stats),
            NumBlobsRead = NativeMethods.rocksdb_compaction_job_stats_num_blobs_read(stats),
            NumInputFiles = NativeMethods.rocksdb_compaction_job_stats_num_input_files(stats),
            NumInputFilesTriviallyMoved = NativeMethods.rocksdb_compaction_job_stats_num_input_files_trivially_moved(stats),
            NumInputFilesAtOutputLevel = NativeMethods.rocksdb_compaction_job_stats_num_input_files_at_output_level(stats),
            NumFilteredInputFiles = NativeMethods.rocksdb_compaction_job_stats_num_filtered_input_files(stats),
            NumFilteredInputFilesAtOutputLevel = NativeMethods.rocksdb_compaction_job_stats_num_filtered_input_files_at_output_level(stats),
            NumOutputRecords = NativeMethods.rocksdb_compaction_job_stats_num_output_records(stats),
            NumOutputFiles = NativeMethods.rocksdb_compaction_job_stats_num_output_files(stats),
            NumOutputFilesBlob = NativeMethods.rocksdb_compaction_job_stats_num_output_files_blob(stats),
            IsFullCompaction = NativeMethods.rocksdb_compaction_job_stats_is_full_compaction(stats) != 0,
            IsManualCompaction = NativeMethods.rocksdb_compaction_job_stats_is_manual_compaction(stats) != 0,
            IsRemoteCompaction = NativeMethods.rocksdb_compaction_job_stats_is_remote_compaction(stats) != 0,
            TotalInputBytes = NativeMethods.rocksdb_compaction_job_stats_total_input_bytes(stats),
            TotalBlobBytesRead = NativeMethods.rocksdb_compaction_job_stats_total_blob_bytes_read(stats),
            TotalOutputBytes = NativeMethods.rocksdb_compaction_job_stats_total_output_bytes(stats),
            TotalOutputBytesBlob = NativeMethods.rocksdb_compaction_job_stats_total_output_bytes_blob(stats),
            TotalSkippedInputBytes = NativeMethods.rocksdb_compaction_job_stats_total_skipped_input_bytes(stats),
            NumRecordsReplaced = NativeMethods.rocksdb_compaction_job_stats_num_records_replaced(stats),
            TotalInputRawKeyBytes = NativeMethods.rocksdb_compaction_job_stats_total_input_raw_key_bytes(stats),
            TotalInputRawValueBytes = NativeMethods.rocksdb_compaction_job_stats_total_input_raw_value_bytes(stats),
            NumInputDeletionRecords = NativeMethods.rocksdb_compaction_job_stats_num_input_deletion_records(stats),
            NumExpiredDeletionRecords = NativeMethods.rocksdb_compaction_job_stats_num_expired_deletion_records(stats),
            NumCorruptKeys = NativeMethods.rocksdb_compaction_job_stats_num_corrupt_keys(stats),
            FileWriteNanos = NativeMethods.rocksdb_compaction_job_stats_file_write_nanos(stats),
            FileRangeSyncNanos = NativeMethods.rocksdb_compaction_job_stats_file_range_sync_nanos(stats),
            FileFsyncNanos = NativeMethods.rocksdb_compaction_job_stats_file_fsync_nanos(stats),
            FilePrepareWriteNanos = NativeMethods.rocksdb_compaction_job_stats_file_prepare_write_nanos(stats),
            SmallestOutputKeyPrefix = smallest is null || smallestLen == 0 ? [] : new ReadOnlySpan<byte>(smallest, checked((int)smallestLen)).ToArray(),
            LargestOutputKeyPrefix = largest is null || largestLen == 0 ? [] : new ReadOnlySpan<byte>(largest, checked((int)largestLen)).ToArray(),
            NumSingleDelFallthru = NativeMethods.rocksdb_compaction_job_stats_num_single_del_fallthru(stats),
            NumSingleDelMismatch = NativeMethods.rocksdb_compaction_job_stats_num_single_del_mismatch(stats),
        };
    }
}

/// <summary>
/// Identifies one input or output file of a compaction.
/// Maps to <c>rocksdb_compaction_file_info_t</c>.
/// </summary>
public sealed record CompactionFileInfo
{
    /// <summary>The LSM level the file belongs to.</summary>
    public int Level { get; init; }

    /// <summary>The file number, which identifies the SST file.</summary>
    public ulong FileNumber { get; init; }

    /// <summary>File number of the oldest blob file referenced, or 0 if none.</summary>
    public ulong OldestBlobFileNumber { get; init; }

    internal static CompactionFileInfo? Copy(nint info)
        => info == nint.Zero
            ? null
            : new CompactionFileInfo
            {
                Level = NativeMethods.rocksdb_compaction_file_info_level(info),
                FileNumber = NativeMethods.rocksdb_compaction_file_info_file_number(info),
                OldestBlobFileNumber = NativeMethods.rocksdb_compaction_file_info_oldest_blob_file_number(info),
            };
}

/// <summary>
/// A blob file created by a flush or compaction.
/// Maps to <c>rocksdb_blob_file_addition_info_t</c>.
/// </summary>
public sealed record BlobFileAdditionInfo
{
    /// <summary>Path of the blob file.</summary>
    public string? BlobFilePath { get; init; }

    /// <summary>The blob file number.</summary>
    public ulong BlobFileNumber { get; init; }

    /// <summary>Number of blobs written to the file.</summary>
    public ulong TotalBlobCount { get; init; }

    /// <summary>Total size of the blobs written, in bytes.</summary>
    public ulong TotalBlobBytes { get; init; }

    internal static unsafe BlobFileAdditionInfo? Copy(nint info)
    {
        if (info == nint.Zero)
        {
            return null;
        }

        byte* path = NativeMethods.rocksdb_blob_file_addition_info_blob_file_path(info, out nuint pathLen);

        return new BlobFileAdditionInfo
        {
            BlobFilePath = path is null ? null : NativeMethods.PtrToStringUTF8(path, pathLen),
            BlobFileNumber = NativeMethods.rocksdb_blob_file_addition_info_blob_file_number(info),
            TotalBlobCount = NativeMethods.rocksdb_blob_file_addition_info_total_blob_count(info),
            TotalBlobBytes = NativeMethods.rocksdb_blob_file_addition_info_total_blob_bytes(info),
        };
    }
}

/// <summary>
/// Garbage discovered in a blob file during compaction.
/// Maps to <c>rocksdb_blob_file_garbage_info_t</c>.
/// </summary>
public sealed record BlobFileGarbageInfo
{
    /// <summary>Path of the blob file.</summary>
    public string? BlobFilePath { get; init; }

    /// <summary>The blob file number.</summary>
    public ulong BlobFileNumber { get; init; }

    /// <summary>Number of blobs in the file that are no longer referenced.</summary>
    public ulong GarbageBlobCount { get; init; }

    /// <summary>Total size of the unreferenced blobs, in bytes.</summary>
    public ulong GarbageBlobBytes { get; init; }

    internal static unsafe BlobFileGarbageInfo? Copy(nint info)
    {
        if (info == nint.Zero)
        {
            return null;
        }

        byte* path = NativeMethods.rocksdb_blob_file_garbage_info_blob_file_path(info, out nuint pathLen);

        return new BlobFileGarbageInfo
        {
            BlobFilePath = path is null ? null : NativeMethods.PtrToStringUTF8(path, pathLen),
            BlobFileNumber = NativeMethods.rocksdb_blob_file_garbage_info_blob_file_number(info),
            GarbageBlobCount = NativeMethods.rocksdb_blob_file_garbage_info_garbage_blob_count(info),
            GarbageBlobBytes = NativeMethods.rocksdb_blob_file_garbage_info_garbage_blob_bytes(info),
        };
    }
}
