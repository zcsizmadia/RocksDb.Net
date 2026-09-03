namespace RocksDbNet;

/// <summary>
/// Options controlling how external SST files are ingested into the database.
/// </summary>
public sealed class IngestExternalFileOptions : RocksDbHandle
{
    /// <summary>Creates a new <see cref="IngestExternalFileOptions"/> with default settings.</summary>
    public IngestExternalFileOptions()
        : base(NativeMethods.rocksdb_ingestexternalfileoptions_create())
    {
    }

    /// <summary>
    /// When <c>true</c>, the files are moved into the database directory instead of copied.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool MoveFiles
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_move_files(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_move_files(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, a move that fails falls back to copying rather than
    /// failing the ingestion.
    /// </summary>
    public bool FailedMoveFallBackToCopy
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_failed_move_fall_back_to_copy(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_failed_move_fall_back_to_copy(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, files are hard linked into the database directory
    /// instead of copied, which is cheaper but requires the same filesystem.
    /// </summary>
    public bool LinkFiles
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_link_files(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_link_files(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, ingestion verifies snapshot consistency.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool SnapshotConsistency
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_snapshot_consistency(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_snapshot_consistency(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, the global sequence number written in the file is allowed to be modified.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool AllowGlobalSeqno
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_allow_global_seqno(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_allow_global_seqno(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, the assigned global sequence number is written into the
    /// ingested file. When <c>false</c>, it is held in the manifest instead,
    /// which leaves the file itself unmodified.
    /// </summary>
    public bool WriteGlobalSeqno
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_write_global_seqno(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_write_global_seqno(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, the ingest operation may wait and block ongoing flushes.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool AllowBlockingFlush
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_allow_blocking_flush(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_allow_blocking_flush(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, the files are ingested below the existing data rather
    /// than on top of it. Requires <see cref="DbOptions.CfAllowIngestBehind"/>.
    /// </summary>
    public bool IngestBehind
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_ingest_behind(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_ingest_behind(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, ingestion fails unless the files can be placed in the
    /// bottommost level.
    /// </summary>
    public bool FailIfNotBottommostLevel
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_fail_if_not_bottommost_level(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_fail_if_not_bottommost_level(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, checksums in the incoming files are verified before the
    /// files are ingested. Catches a corrupt file before it enters the database.
    /// </summary>
    public bool VerifyChecksumsBeforeIngest
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_verify_checksums_before_ingest(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_verify_checksums_before_ingest(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Readahead size in bytes used while verifying checksums before ingest.
    /// 0 lets RocksDb choose.
    /// </summary>
    public ulong VerifyChecksumsReadaheadSize
    {
        get => (ulong)NativeMethods.rocksdb_ingestexternalfileoptions_get_verify_checksums_readahead_size(Handle);
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_verify_checksums_readahead_size(Handle, (nuint)value);
    }

    /// <summary>
    /// When <c>true</c>, each file's whole-file checksum is verified against the
    /// checksum recorded for it.
    /// </summary>
    public bool VerifyFileChecksum
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_verify_file_checksum(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_verify_file_checksum(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, blocks read during ingestion are added to the block
    /// cache. Leave this off for a bulk load so it does not evict live data.
    /// </summary>
    public bool FillCache
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_fill_cache(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_fill_cache(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, index and filter blocks of files landing in the last
    /// level are prefetched.
    /// </summary>
    public bool PrefetchLmaxIndexAndFilterBlocks
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_prefetch_lmax_index_and_filter_blocks(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_prefetch_lmax_index_and_filter_blocks(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, files produced by a RocksDb database, rather than by
    /// <see cref="SstFileWriter"/>, may be ingested.
    /// </summary>
    public bool AllowDbGeneratedFiles
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_allow_db_generated_files(Handle) != 0;
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_allow_db_generated_files(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Number of threads used to open the incoming files. Higher values speed up
    /// ingesting many files at once.
    /// </summary>
    public int FileOpeningThreads
    {
        get => NativeMethods.rocksdb_ingestexternalfileoptions_get_file_opening_threads(Handle);
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_file_opening_threads(Handle, value);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_ingestexternalfileoptions_destroy(Handle);
    }
}
