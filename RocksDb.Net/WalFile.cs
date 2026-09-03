using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>Whether a WAL file is still being written to or has been archived.</summary>
/// <remarks>Values are defined by <c>c.h</c>.</remarks>
public enum WalFileType
{
    /// <summary>The file has been archived and is no longer written to.</summary>
    ArchivedLog = 0,

    /// <summary>The file is live and RocksDb may still append to it.</summary>
    AliveLog = 1,
}

/// <summary>
/// Describes one write-ahead log file.
/// Copied from <c>rocksdb_wal_file_t</c>.
/// </summary>
/// <remarks>
/// This is a snapshot rather than a handle, deliberately. RocksDb's ownership of
/// WAL file objects is asymmetric: entries from
/// <see cref="RocksDb.GetSortedWalFiles"/> are borrowed from a vector and must
/// not be freed individually, while
/// <see cref="RocksDb.GetCurrentWalFile"/> returns one the caller owns. Copying
/// the values out means callers never have to know which they hold.
/// </remarks>
public sealed record WalFile
{
    /// <summary>Path of the file, relative to the WAL directory.</summary>
    public string? PathName { get; init; }

    /// <summary>The log number, which increases as WAL files are rotated.</summary>
    public ulong LogNumber { get; init; }

    /// <summary>Whether the file is live or archived.</summary>
    public WalFileType Type { get; init; }

    /// <summary>Sequence number of the first record in the file.</summary>
    public ulong StartSequence { get; init; }

    /// <summary>Size of the file in bytes.</summary>
    public ulong SizeFileBytes { get; init; }

    /// <summary>Copies one entry out of a borrowed or owned native WAL file.</summary>
    internal static unsafe WalFile? Copy(nint file)
        => file == nint.Zero
            ? null
            : new WalFile
            {
                PathName = Marshal.PtrToStringUTF8((nint)NativeMethods.rocksdb_wal_file_path_name(file)),
                LogNumber = NativeMethods.rocksdb_wal_file_log_number(file),
                Type = (WalFileType)NativeMethods.rocksdb_wal_file_type(file),
                StartSequence = NativeMethods.rocksdb_wal_file_start_sequence(file),
                SizeFileBytes = NativeMethods.rocksdb_wal_file_size_file_bytes(file),
            };
}

/// <summary>
/// Options for reading the write-ahead log through
/// <see cref="RocksDb.GetUpdatesSince(ulong, WalReadOptions)"/>.
/// Maps to <c>rocksdb_wal_readoptions_t</c>.
/// </summary>
public sealed class WalReadOptions : RocksDbHandle
{
    public WalReadOptions()
        : base(NativeMethods.rocksdb_wal_readoptions_create())
    {
    }

    /// <summary>
    /// If true, each WAL record's checksum is verified as it is read, so a
    /// corrupt log is reported rather than replayed.
    /// </summary>
    public bool VerifyChecksums
    {
        get => NativeMethods.rocksdb_wal_readoptions_get_verify_checksums(Handle) != 0;
        set => NativeMethods.rocksdb_wal_readoptions_set_verify_checksums(Handle, value ? (byte)1 : (byte)0);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_wal_readoptions_destroy(Handle);
    }
}

/// <summary>
/// Walks the write-ahead log from a given sequence number, yielding the batches
/// that were written.
/// Maps to <c>rocksdb_wal_iterator_t</c>.
/// </summary>
/// <remarks>
/// Useful for replication and change-data-capture: each step gives the batch and
/// the sequence number it started at. Only records still present in the WAL are
/// visible, so a sequence number older than the oldest retained log is an error
/// rather than an empty result.
/// </remarks>
public sealed class WalIterator : RocksDbHandle
{
    internal WalIterator(nint handle)
        : base(handle)
    {
    }

    /// <summary>Whether the iterator currently points at a record.</summary>
    public bool IsValid() => NativeMethods.rocksdb_wal_iter_valid(Handle) != 0;

    /// <summary>Advances to the next record.</summary>
    public void Next() => NativeMethods.rocksdb_wal_iter_next(Handle);

    /// <summary>Throws if the iterator has encountered an error.</summary>
    /// <exception cref="RocksDbException">Reading the log failed.</exception>
    public void CheckForError()
    {
        nint err = default;
        NativeMethods.rocksdb_wal_iter_status(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Returns the batch at the current position, along with the sequence number
    /// of its first record.
    /// </summary>
    /// <remarks>
    /// RocksDb builds a fresh batch for each call, so the returned
    /// <see cref="WriteBatch"/> belongs to the caller and must be disposed.
    /// </remarks>
    public unsafe (WriteBatch Batch, ulong Sequence) GetBatch()
    {
        ulong sequence = 0;
        nint batch = NativeMethods.rocksdb_wal_iter_get_batch(Handle, &sequence);
        return (new WriteBatch(batch), sequence);
    }

    /// <summary>
    /// Enumerates the remaining records, disposing each batch once the consumer
    /// has moved past it.
    /// </summary>
    /// <remarks>
    /// Do not hold on to a batch beyond its iteration step. Use
    /// <see cref="GetBatch"/> directly if you need to keep one.
    /// </remarks>
    public IEnumerable<(WriteBatch Batch, ulong Sequence)> AsEnumerable()
    {
        while (IsValid())
        {
            (WriteBatch batch, ulong sequence) = GetBatch();
            try
            {
                yield return (batch, sequence);
            }
            finally
            {
                batch.Dispose();
            }

            Next();
        }

        CheckForError();
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_wal_iter_destroy(Handle);
    }
}
