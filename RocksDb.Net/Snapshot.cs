namespace RocksDbNet;

/// <summary>
/// A point-in-time consistent view of the database. Maps to
/// <c>rocksdb_snapshot_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Dispose it as soon as the view is no longer needed. While a snapshot lives,
/// compaction cannot reclaim any version of a key that the snapshot can still
/// see, so a long-lived snapshot grows the database.
/// </para>
/// <para>
/// It must be released before the database that created it. That ordering is
/// enforced: a snapshot keeps its database reachable, and skips its native
/// release if the database has already been closed.
/// </para>
/// </remarks>
public sealed class Snapshot : RocksDbHandle
{
    // Which native function releases this snapshot. A plain database and a
    // transaction database take different ones, and calling the wrong one
    // passes a handle of the wrong type into native code.
    private enum Owner
    {
        Database,
        TransactionDatabase,
    }

    private readonly RocksDbHandle _owner;
    private readonly Owner _ownerKind;

    internal Snapshot(nint handle, RocksDb db)
        : base(handle)
    {
        _owner = db;
        _ownerKind = Owner.Database;
        SetParent(db);
    }

    internal Snapshot(nint handle, TransactionDb db)
        : base(handle)
    {
        _owner = db;
        _ownerKind = Owner.TransactionDatabase;
        SetParent(db);
    }

    /// <summary>The sequence number at which this snapshot was taken.</summary>
    public ulong SequenceNumber => NativeMethods.rocksdb_snapshot_get_sequence_number(Handle);

    protected override void DisposeHandle()
    {
        switch (_ownerKind)
        {
            case Owner.TransactionDatabase:
                NativeMethods.rocksdb_transactiondb_release_snapshot(_owner.Handle, Handle);
                break;

            default:
                NativeMethods.rocksdb_release_snapshot(_owner.Handle, Handle);
                break;
        }
    }
}
