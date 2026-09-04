using System.Text;

namespace RocksDbNet;

/// <summary>
/// An atomic write batch that also indexes its own contents, so it can be
/// read back before it is applied. Maps to <c>rocksdb_writebatch_wi_t</c>.
/// </summary>
/// <remarks>
/// Apply it with <see cref="RocksDb.Write(WriteBatchWithIndex, WriteOptions)"/>.
/// To read pending writes before applying them, use
/// <see cref="GetFromBatch(DbOptions, ReadOnlySpan{byte})"/> for the batch
/// alone, or <see cref="GetFromBatchAndDb(RocksDb, ReadOnlySpan{byte}, ReadOptions)"/>
/// to see the batch layered over the committed database.
/// </remarks>
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

    // DeleteRange is deliberately absent: WriteBatchWithIndex::DeleteRange
    // returns NotSupported for every argument and the C API discards that
    // status, so it queued nothing and reported success. Use WriteBatch, whose
    // DeleteRange RocksDb does support.

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

    // ── Reading back ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a key from this batch alone, ignoring the database, or returns
    /// <see langword="null"/> if the batch does not mention it.
    /// </summary>
    /// <param name="options">
    /// The database options, not read options. RocksDb needs them for the merge
    /// operator, so that a merge queued in this batch can be resolved.
    /// </param>
    /// <param name="key">The key to read.</param>
    /// <remarks>
    /// Throws when the batch holds a merge for this key that cannot be resolved
    /// on its own, for instance because there is no base value here and no merge
    /// operator configured. Use
    /// <see cref="GetFromBatchAndDb(RocksDb, ReadOnlySpan{byte}, ReadOptions?)"/>
    /// when the base value is in the database.
    /// </remarks>
    public unsafe byte[]? GetFromBatch(DbOptions options, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(options);

        nint err = default;
        nint value;
        nuint length;
        fixed (byte* k = key)
            value = NativeMethods.rocksdb_writebatch_wi_get_from_batch(
                Handle, options.Handle, k, (nuint)key.Length, out length, ref err);

        NativeMethods.ThrowOnError(err);
        return CopyAndFree(value, length);
    }

    /// <inheritdoc cref="GetFromBatch(DbOptions, ReadOnlySpan{byte})"/>
    public unsafe byte[]? GetFromBatch(DbOptions options, ReadOnlySpan<byte> key, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        nint value;
        nuint length;
        fixed (byte* k = key)
            value = NativeMethods.rocksdb_writebatch_wi_get_from_batch_cf(
                Handle, options.Handle, cf.Handle, k, (nuint)key.Length, out length, ref err);

        NativeMethods.ThrowOnError(err);
        return CopyAndFree(value, length);
    }

    /// <summary>
    /// Reads a key, seeing this batch's queued writes on top of what is already
    /// in <paramref name="db"/>, or returns <see langword="null"/> if neither
    /// has it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is read-your-own-writes, and the reason to choose an indexed batch
    /// over a plain <see cref="WriteBatch"/>. A queued put shadows the stored
    /// value, a queued delete hides it, and a queued merge is resolved against
    /// it using the database's merge operator.
    /// </para>
    /// <para>
    /// A snapshot in <paramref name="options"/> applies to the database side
    /// only. This batch's own writes are always visible, since they are not in
    /// the database yet.
    /// </para>
    /// </remarks>
    public unsafe byte[]? GetFromBatchAndDb(RocksDb db, ReadOnlySpan<byte> key, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        nint err = default;
        nint value;
        nuint length;
        fixed (byte* k = key)
            value = NativeMethods.rocksdb_writebatch_wi_get_from_batch_and_db(
                Handle, db.Handle, (options ?? _defaultReadOptions).Handle,
                k, (nuint)key.Length, out length, ref err);

        NativeMethods.ThrowOnError(err);
        return CopyAndFree(value, length);
    }

    /// <inheritdoc cref="GetFromBatchAndDb(RocksDb, ReadOnlySpan{byte}, ReadOptions?)"/>
    public unsafe byte[]? GetFromBatchAndDb(
        RocksDb db, ReadOnlySpan<byte> key, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        nint value;
        nuint length;
        fixed (byte* k = key)
            value = NativeMethods.rocksdb_writebatch_wi_get_from_batch_and_db_cf(
                Handle, db.Handle, (options ?? _defaultReadOptions).Handle, cf.Handle,
                k, (nuint)key.Length, out length, ref err);

        NativeMethods.ThrowOnError(err);
        return CopyAndFree(value, length);
    }

    /// <summary>Reads a UTF-8 key from this batch and the database.</summary>
    public string? GetStringFromBatchAndDb(RocksDb db, string key, ReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        byte[]? value = GetFromBatchAndDb(db, Encoding.UTF8.GetBytes(key), options);
        return value is null ? null : Encoding.UTF8.GetString(value);
    }

    // ── Iteration ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an iterator over <paramref name="db"/> with this batch's queued
    /// writes overlaid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The iterator merges the two sources, so it shows what the data would look
    /// like if the batch were applied. Queued puts shadow stored values, and
    /// queued deletes hide them.
    /// </para>
    /// <para>
    /// Do not modify the batch while the iterator is positioned; RocksDb
    /// invalidates the current key and value. Dispose the iterator before both
    /// the batch and the database.
    /// </para>
    /// </remarks>
    public Iterator NewIteratorWithBase(RocksDb db, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        // The base iterator is created here and never wrapped, because
        // rocksdb_writebatch_wi_create_iterator_with_base deletes the
        // rocksdb_iterator_t it is given. Handing it a caller's Iterator would
        // leave that object holding a freed pointer, and its disposal would
        // destroy the same memory twice.
        nint baseIterator = NativeMethods.rocksdb_create_iterator(
            db.Handle, (options ?? _defaultReadOptions).Handle);

        nint overlay = NativeMethods.rocksdb_writebatch_wi_create_iterator_with_base_readopts(
            Handle, baseIterator, (options ?? _defaultReadOptions).Handle);

        return Iterator.FromHandle(overlay, db, options, secondary: this);
    }

    /// <inheritdoc cref="NewIteratorWithBase(RocksDb, ReadOptions?)"/>
    public Iterator NewIteratorWithBase(RocksDb db, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(cf);

        nint baseIterator = NativeMethods.rocksdb_create_iterator_cf(
            db.Handle, (options ?? _defaultReadOptions).Handle, cf.Handle);

        nint overlay = NativeMethods.rocksdb_writebatch_wi_create_iterator_with_base_cf_readopts(
            Handle, baseIterator, cf.Handle, (options ?? _defaultReadOptions).Handle);

        return Iterator.FromHandle(overlay, db, options, secondary: this);
    }

    private static readonly ReadOptions _defaultReadOptions = new();

    private static unsafe byte[]? CopyAndFree(nint value, nuint length)
    {
        if (value == nint.Zero)
        {
            return null;
        }

        byte[] result = new ReadOnlySpan<byte>((byte*)value, checked((int)length)).ToArray();
        NativeMethods.rocksdb_free(value);
        return result;
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_writebatch_wi_destroy(Handle);
    }
}
