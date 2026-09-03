namespace RocksDbNet;

/// <summary>
/// Options for <see cref="RocksDb.GetLiveFilesStorageInfo(LiveFilesStorageInfoOptions)"/>.
/// Maps to <c>rocksdb_livefiles_storage_info_options_t</c>.
/// </summary>
public sealed class LiveFilesStorageInfoOptions : RocksDbHandle
{
    public LiveFilesStorageInfoOptions()
        : base(NativeMethods.rocksdb_livefiles_storage_info_options_create())
    {
    }

    /// <summary>
    /// If true, each entry carries its checksum and the name of the function
    /// that produced it, which a copy tool can verify against.
    /// </summary>
    public bool IncludeChecksumInfo
    {
        get => NativeMethods.rocksdb_livefiles_storage_info_options_get_include_checksum_info(Handle) != 0;
        set => NativeMethods.rocksdb_livefiles_storage_info_options_set_include_checksum_info(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Flush memtables when the WAL exceeds this many bytes, so the reported set
    /// of files is more complete.
    /// </summary>
    /// <remarks>
    /// The default of 0 means "always flush", so calling
    /// <see cref="RocksDb.GetLiveFilesStorageInfo(LiveFilesStorageInfoOptions)"/>
    /// flushes by default. Set this high to avoid that.
    /// </remarks>
    public ulong WalSizeForFlush
    {
        get => NativeMethods.rocksdb_livefiles_storage_info_options_get_wal_size_for_flush(Handle);
        set => NativeMethods.rocksdb_livefiles_storage_info_options_set_wal_size_for_flush(Handle, value);
    }

    /// <summary>
    /// If true, any flush this triggers covers every column family atomically.
    /// </summary>
    public bool AtomicFlush
    {
        get => NativeMethods.rocksdb_livefiles_storage_info_options_get_atomic_flush(Handle) != 0;
        set => NativeMethods.rocksdb_livefiles_storage_info_options_set_atomic_flush(Handle, value ? (byte)1 : (byte)0);
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_livefiles_storage_info_options_destroy(Handle);
    }
}

/// <summary>
/// One file belonging to a consistent snapshot of the database, described in
/// enough detail to copy it elsewhere.
/// Maps to one entry of <c>rocksdb_livefiles_storage_info_t</c>.
/// </summary>
/// <remarks>
/// Everything needed to reproduce the file is here: where it lives, what to call
/// it in the copy, how many of its bytes are actually live, and for small
/// metadata files the content to write instead of copying.
/// </remarks>
public sealed record LiveFileStorageInfo
{
    /// <summary>Name the file should have in the copy, relative to its directory.</summary>
    public string? RelativeFilename { get; init; }

    /// <summary>Directory the file currently lives in.</summary>
    public string? Directory { get; init; }

    /// <summary>Size in bytes.</summary>
    public ulong Size { get; init; }

    /// <summary>The kind of file this is.</summary>
    public FileType FileType { get; init; }

    /// <summary>
    /// RocksDb's file number for this file, or 0 for files that do not have one.
    /// </summary>
    public ulong FileNumber { get; init; }

    /// <summary>Storage temperature RocksDb has assigned to the file.</summary>
    public Temperature Temperature { get; init; }

    /// <summary>
    /// Whether only the first <see cref="Size"/> bytes are live, so a copy should
    /// be truncated to that length. True for files RocksDb is still appending to,
    /// such as the current WAL.
    /// </summary>
    public bool TrimToSize { get; init; }

    /// <summary>
    /// The file's checksum, or empty when
    /// <see cref="LiveFilesStorageInfoOptions.IncludeChecksumInfo"/> was not set
    /// or no checksum was recorded. Raw bytes, since a checksum is not text.
    /// </summary>
    public byte[] FileChecksum { get; init; } = [];

    /// <summary>
    /// Name of the function that produced <see cref="FileChecksum"/>, or empty
    /// when there is none.
    /// </summary>
    public string? FileChecksumFuncName { get; init; }

    /// <summary>
    /// Content to write into the copy instead of reading the original, or empty
    /// when the file should simply be copied. RocksDb supplies this for small
    /// metadata files such as CURRENT.
    /// </summary>
    public byte[] ReplacementContents { get; init; } = [];
}
