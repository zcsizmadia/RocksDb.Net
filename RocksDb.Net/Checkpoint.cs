using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>
/// Creates on-disk checkpoints (hard-link-based snapshots) of a RocksDB database.
/// Maps to <c>rocksdb_checkpoint_t</c>.
/// </summary>
public sealed class Checkpoint : RocksDbHandle
{
    private Checkpoint(nint handle)
    {
        Handle = handle;
    }

    /// <summary>Creates a <see cref="Checkpoint"/> object for the given database.</summary>
    public static Checkpoint Create(RocksDb db)
    {
        nint err = default;
        nint handle = NativeMethods.rocksdb_checkpoint_object_create(db.Handle, ref err);
        NativeMethods.ThrowOnError(err);
        return new Checkpoint(handle);
    }

    /// <summary>
    /// Creates a new database checkpoint at <paramref name="checkpointDir"/>.
    /// If <paramref name="logSizeForFlush"/> is 0, all memtables are flushed.
    /// </summary>
    public void CreateCheckpoint(string checkpointDir, ulong logSizeForFlush = 0)
    {
        nint err = default;
        NativeMethods.rocksdb_checkpoint_create(Handle, checkpointDir, logSizeForFlush, ref err);
        NativeMethods.ThrowOnError(err);
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_checkpoint_object_destroy(Handle);
    }
}
