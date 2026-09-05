namespace RocksDbNet;

/// <summary>
/// Creates on-disk checkpoints (hard-link-based snapshots) of a RocksDb database.
/// Maps to <c>rocksdb_checkpoint_t</c>.
/// </summary>
public sealed class Checkpoint : RocksDbHandle
{
    private Checkpoint(nint handle)
        : base(handle)
    {
    }

    /// <summary>Creates a <see cref="Checkpoint"/> object for the given database.</summary>
    public static Checkpoint Create(RocksDb db)
    {
        ArgumentNullException.ThrowIfNull(db);

        nint err = default;
        nint handle = NativeMethods.rocksdb_checkpoint_object_create(db.Handle, ref err);
        NativeMethods.ThrowOnError(err);

        return FromHandle(handle, db);
    }

    /// <summary>
    /// Wraps a checkpoint handle the caller already created, parenting it to
    /// the database it came from.
    /// </summary>
    /// <remarks>
    /// Parented to the database, like every other handle derived from one.
    /// <c>rocksdb_checkpoint_object_create</c> passes the database's <c>DB*</c>
    /// to <c>Checkpoint::Create</c>, which keeps it for the checkpoint's whole
    /// life, so a checkpoint outliving its database was a use-after-free on the
    /// next call rather than the <see cref="ObjectDisposedException"/> every
    /// sibling type gives. The parent link also keeps the database reachable,
    /// which matters when the only reference to it is the one the checkpoint
    /// was created from.
    /// </remarks>
    internal static Checkpoint FromHandle(nint handle, RocksDbHandle owner)
    {
        var checkpoint = new Checkpoint(handle);
        checkpoint.SetParent(owner);
        return checkpoint;
    }

    /// <summary>
    /// Creates a new database checkpoint at <paramref name="checkpointDir"/>.
    /// If <paramref name="logSizeForFlush"/> is 0, all memtables are flushed.
    /// </summary>
    public void CreateCheckpoint(string checkpointDir, ulong logSizeForFlush = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(checkpointDir);

        nint err = default;
        NativeMethods.rocksdb_checkpoint_create(Handle, checkpointDir, logSizeForFlush, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Writes one column family's live files into <paramref name="exportDir"/>
    /// and returns the metadata needed to import them elsewhere.
    /// </summary>
    /// <param name="cf">The column family to export.</param>
    /// <param name="exportDir">
    /// Directory to write into. <b>It must not already exist:</b> RocksDb
    /// creates it and fails with "Specified export_dir exists" otherwise, even
    /// if the directory is empty. Pass a path under a directory you control
    /// rather than one you have already created.
    /// </param>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="CreateCheckpoint"/>, which copies a whole database into
    /// something you open separately, this exports a single column family so it
    /// can be imported into a database that is already running. That is the way
    /// to copy a column family between databases, rename one, or restore just
    /// one part of a database.
    /// </para>
    /// <para>
    /// The export holds no write-ahead log and no history, only the current
    /// contents. Dispose the returned metadata when finished with it.
    /// </para>
    /// </remarks>
    public ExportImportFilesMetadata ExportColumnFamily(ColumnFamilyHandle cf, string exportDir)
    {
        ArgumentNullException.ThrowIfNull(cf);
        ArgumentException.ThrowIfNullOrEmpty(exportDir);

        nint err = default;
        nint metadata = NativeMethods.rocksdb_checkpoint_export_column_family(Handle, cf.Handle, exportDir, ref err);
        NativeMethods.ThrowOnError(err);

        return new ExportImportFilesMetadata(metadata);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_checkpoint_object_destroy(Handle);
    }
}
