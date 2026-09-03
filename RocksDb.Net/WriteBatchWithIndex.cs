using System.Text;

namespace RocksDbNet;

/// <summary>
/// An atomic write batch. Apply to the database with <see cref="RocksDb.Write"/>.
/// Maps to <c>rocksdb_writebatch_wi_t</c>.
/// </summary>
public sealed class WriteBatchWithIndex : RocksDbHandle
{
    /// <summary>Creates an empty write batch.</summary>
    public WriteBatchWithIndex(int reservedBytes = 0, bool overwriteKeys = true)
    {
        if (reservedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservedBytes), "Reserved bytes must be non-negative.");
        }
        Handle = NativeMethods.rocksdb_writebatch_wi_create((nuint)reservedBytes, overwriteKeys ? (byte)1 : (byte)0);
    }

    /// <summary>Returns the number of operations in the batch.</summary>
    public int Count => NativeMethods.rocksdb_writebatch_wi_count(Handle);

    /// <summary>Clears all operations from the batch.</summary>
    public WriteBatchWithIndex Clear()
    {
        NativeMethods.rocksdb_writebatch_wi_clear(Handle);
        return this;
    }

    // ── Put ──────────────────────────────────────────────────────────────────

    /// <summary>Queues a Put into the default column family.</summary>
    public unsafe WriteBatchWithIndex Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_writebatch_wi_put(Handle, k, (nuint)key.Length, v, (nuint)value.Length);
        return this;
    }

    /// <summary>Queues a Put into the specified column family.</summary>
    public unsafe WriteBatchWithIndex Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_writebatch_wi_put_cf(Handle, cf.Handle, k, (nuint)key.Length, v, (nuint)value.Length);
        return this;
    }

    /// <summary>Convenience overload using UTF-8 string key and value.</summary>
    public WriteBatchWithIndex Put(string key, string value)
        => Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value));

    /// <summary>Convenience overload using UTF-8 string key and value in a specific column family.</summary>
    public WriteBatchWithIndex Put(string key, string value, ColumnFamilyHandle cf)
        => Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), cf);

    // ── Merge ────────────────────────────────────────────────────────────────

    /// <summary>Queues a Merge into the default column family.</summary>
    public unsafe WriteBatchWithIndex Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_writebatch_wi_merge(Handle, k, (nuint)key.Length, v, (nuint)value.Length);
        return this;
    }

    /// <summary>Queues a Merge into the specified column family.</summary>
    public unsafe WriteBatchWithIndex Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_writebatch_wi_merge_cf(Handle, cf.Handle, k, (nuint)key.Length, v, (nuint)value.Length);
        return this;
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    /// <summary>Queues a Delete from the default column family.</summary>
    public unsafe WriteBatchWithIndex Delete(ReadOnlySpan<byte> key)
    {
        fixed (byte* k = key)
            NativeMethods.rocksdb_writebatch_wi_delete(Handle, k, (nuint)key.Length);
        return this;
    }

    /// <summary>Queues a Delete from the specified column family.</summary>
    public unsafe WriteBatchWithIndex Delete(ReadOnlySpan<byte> key, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        fixed (byte* k = key)
            NativeMethods.rocksdb_writebatch_wi_delete_cf(Handle, cf.Handle, k, (nuint)key.Length);
        return this;
    }

    /// <summary>Convenience overload using a UTF-8 string key.</summary>
    public WriteBatchWithIndex Delete(string key) => Delete(Encoding.UTF8.GetBytes(key));

    /// <summary>Convenience overload using a UTF-8 string key in a column family.</summary>
    public WriteBatchWithIndex Delete(string key, ColumnFamilyHandle cf) => Delete(Encoding.UTF8.GetBytes(key), cf);

    // ── SingleDelete ─────────────────────────────────────────────────────────

    /// <summary>
    /// Queues a SingleDelete. Only valid when exactly one Put exists for the key
    /// (no prior Puts remain in the database for that key).
    /// </summary>
    public unsafe WriteBatchWithIndex SingleDelete(ReadOnlySpan<byte> key)
    {
        fixed (byte* k = key)
            NativeMethods.rocksdb_writebatch_wi_singledelete(Handle, k, (nuint)key.Length);
        return this;
    }

    /// <summary>Queues a SingleDelete in the specified column family.</summary>
    public unsafe WriteBatchWithIndex SingleDelete(ReadOnlySpan<byte> key, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        fixed (byte* k = key)
            NativeMethods.rocksdb_writebatch_wi_singledelete_cf(Handle, cf.Handle, k, (nuint)key.Length);
        return this;
    }

    // ── DeleteRange ──────────────────────────────────────────────────────────

    /// <summary>
    /// Not supported by RocksDb on an indexed batch. Always throws.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    /// <remarks>
    /// <para>
    /// <c>WriteBatchWithIndex::DeleteRange</c> returns <c>NotSupported</c> for
    /// every argument, and the C API discards that status, so this used to queue
    /// nothing and report success. RocksDb's own header marks the three entry
    /// points "DO NOT USE - not yet supported".
    /// </para>
    /// <para>
    /// It throws rather than being removed so that the failure is visible.
    /// Silently dropping a range delete loses data the caller believes is gone.
    /// Use <see cref="WriteBatch.DeleteRange(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    /// on a plain batch, which RocksDb does support.
    /// </para>
    /// </remarks>
    [Obsolete("RocksDb does not support DeleteRange on an indexed batch. Use WriteBatch instead.", error: false)]
    public WriteBatchWithIndex DeleteRange(ReadOnlySpan<byte> startKey, ReadOnlySpan<byte> endKey)
        => throw new NotSupportedException(NoDeleteRange);

    /// <inheritdoc cref="DeleteRange(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    [Obsolete("RocksDb does not support DeleteRange on an indexed batch. Use WriteBatch instead.", error: false)]
    public WriteBatchWithIndex DeleteRange(ReadOnlySpan<byte> startKey, ReadOnlySpan<byte> endKey, ColumnFamilyHandle cf)
        => throw new NotSupportedException(NoDeleteRange);

    private const string NoDeleteRange =
        "RocksDb does not support DeleteRange on a WriteBatchWithIndex: the native call " +
        "returns NotSupported and queues nothing. Use WriteBatch.DeleteRange instead.";

    // ── Log data ─────────────────────────────────────────────────────────────

    /// <summary>Inserts a blob of user-defined log data into the WAL.</summary>
    public unsafe WriteBatchWithIndex PutLogData(ReadOnlySpan<byte> blob)
    {
        fixed (byte* b = blob)
            NativeMethods.rocksdb_writebatch_wi_put_log_data(Handle, b, (nuint)blob.Length);
        return this;
    }

    // ── Save points ──────────────────────────────────────────────────────────

    /// <summary>Marks a save point inside the batch.</summary>
    public WriteBatchWithIndex SetSavePoint()
    {
        NativeMethods.rocksdb_writebatch_wi_set_save_point(Handle);
        return this;
    }

    /// <summary>Rolls back to the most recent save point.</summary>
    public WriteBatchWithIndex RollbackToSavePoint()
    {
        nint err = default;
        NativeMethods.rocksdb_writebatch_wi_rollback_to_save_point(Handle, ref err);
        NativeMethods.ThrowOnError(err);
        return this;
    }

    // ── Raw data ─────────────────────────────────────────────────────────────

    /// <summary>Returns the serialized batch data as a byte array.</summary>
    public unsafe byte[] GetData()
    {
        byte* ptr = NativeMethods.rocksdb_writebatch_wi_data(Handle, out nuint size);
        return new ReadOnlySpan<byte>(ptr, checked((int)size)).ToArray();
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_writebatch_wi_destroy(Handle);
    }
}
