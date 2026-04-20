namespace RocksDbNet;

/// <summary>
/// A point-in-time consistent snapshot of the database state.
/// The snapshot is owned by the <see cref="RocksDb"/> instance that created it and
/// must be disposed before the database is closed.
/// Maps to <c>rocksdb_snapshot_t</c>.
/// </summary>
public sealed class Snapshot : RocksDbHandle
{
    private readonly RocksDb _db;

    internal Snapshot(nint handle, RocksDb db)
        : base(handle)
    {
        _db = db;
    }

    /// <summary>Returns the sequence number at which this snapshot was taken.</summary>
    public ulong SequenceNumber => NativeMethods.rocksdb_snapshot_get_sequence_number(Handle);

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_release_snapshot(_db.Handle, Handle);
    }
}
