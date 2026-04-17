using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>
/// Options controlling how external SST files are ingested into the database.
/// </summary>
public sealed class IngestExternalFileOptions : RocksDbHandle
{
    /// <summary>Creates a new <see cref="IngestExternalFileOptions"/> with default settings.</summary>
    public IngestExternalFileOptions()
    {
        Handle = NativeMethods.rocksdb_ingestexternalfileoptions_create();
    }

    /// <summary>
    /// When <c>true</c>, the files are moved into the database directory instead of copied.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool MoveFiles
    {
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_move_files(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, ingestion verifies snapshot consistency.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool SnapshotConsistency
    {
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_snapshot_consistency(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, the global sequence number written in the file is allowed to be modified.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool AllowGlobalSeqno
    {
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_allow_global_seqno(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// When <c>true</c>, the ingest operation may wait and block ongoing flushes.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool AllowBlockingFlush
    {
        set => NativeMethods.rocksdb_ingestexternalfileoptions_set_allow_blocking_flush(Handle, value ? (byte)1 : (byte)0);
    }

    public override void DisposeUnmanagedResources()
    {
        NativeMethods.rocksdb_ingestexternalfileoptions_destroy(Handle);
    }
}