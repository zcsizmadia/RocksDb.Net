using System.Text;

namespace RocksDbNet;

/// <summary>
/// A transaction on a <see cref="TransactionDb"/>. Maps to
/// <c>rocksdb_transaction_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reads see the transaction's own pending writes as well as the committed
/// database. Writes are buffered until <see cref="Commit"/>, and discarded by
/// <see cref="Rollback"/>.
/// </para>
/// <para>
/// Always dispose it. Neither <see cref="Commit"/> nor <see cref="Rollback"/>
/// releases the transaction; they only decide what happens to its writes. A
/// transaction that is never disposed keeps its locks.
/// </para>
/// <para>
/// It must be released before the database. That ordering is enforced rather
/// than merely documented: a transaction keeps its database reachable, and
/// skips its native release once the database has closed. RocksDb's own
/// destructor unlocks keys and unregisters the transaction through the database
/// pointer, so releasing after the close would use freed memory.
/// </para>
/// </remarks>
public sealed class Transaction : RocksDbHandle
{
    private static readonly ReadOptions _defaultReadOptions = new();

    // Iterators over a transaction are invalidated by Commit, Rollback and
    // RollbackToSavePoint, and nothing in the C API stops a caller using one
    // afterwards. Tracking them lets those operations dispose them first, which
    // turns a use-after-free into an ObjectDisposedException.
    private readonly List<Iterator> _iterators = [];
    private readonly object _gate = new();

    internal Transaction(nint handle, TransactionDb db)
        : base(handle)
    {
        SetParent(db);
    }

    // ── Writes ───────────────────────────────────────────────────────────────

    /// <summary>Queues a write of <paramref name="key"/>, taking a lock on it.</summary>
    public unsafe void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_transaction_put(Handle, k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <inheritdoc cref="Put(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    public unsafe void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_transaction_put_cf(Handle, cf.Handle, k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Queues a write of a UTF-8 key and value.</summary>
    public void Put(string key, string value)
        => Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value));

    /// <inheritdoc cref="Put(string, string)"/>
    public void Put(string key, string value, ColumnFamilyHandle cf)
        => Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), cf);

    /// <summary>Queues a delete of <paramref name="key"/>, taking a lock on it.</summary>
    public unsafe void Delete(ReadOnlySpan<byte> key)
    {
        nint err = default;
        fixed (byte* k = key)
            NativeMethods.rocksdb_transaction_delete(Handle, k, (nuint)key.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <inheritdoc cref="Delete(ReadOnlySpan{byte})"/>
    public unsafe void Delete(ReadOnlySpan<byte> key, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        fixed (byte* k = key)
            NativeMethods.rocksdb_transaction_delete_cf(Handle, cf.Handle, k, (nuint)key.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <inheritdoc cref="Delete(ReadOnlySpan{byte})"/>
    public void Delete(string key) => Delete(Encoding.UTF8.GetBytes(key));

    /// <summary>Queues a merge operation on <paramref name="key"/>.</summary>
    public unsafe void Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_transaction_merge(Handle, k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <inheritdoc cref="Merge(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    public unsafe void Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_transaction_merge_cf(Handle, cf.Handle, k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ── Reads ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a key, seeing this transaction's pending writes, or returns
    /// <see langword="null"/> if it is absent.
    /// </summary>
    /// <remarks>
    /// This takes no lock. Use <see cref="GetForUpdate(ReadOnlySpan{byte}, bool, ReadOptions?)"/>
    /// for a read that a later write in the same transaction depends on.
    /// </remarks>
    public unsafe byte[]? Get(ReadOnlySpan<byte> key, ReadOptions? options = null)
    {
        nint err = default;
        nint value;
        nuint length;
        fixed (byte* k = key)
            value = NativeMethods.rocksdb_transaction_get(Handle, (options ?? _defaultReadOptions).Handle,
                k, (nuint)key.Length, out length, ref err);

        NativeMethods.ThrowOnError(err);
        return CopyAndFree(value, length);
    }

    /// <inheritdoc cref="Get(ReadOnlySpan{byte}, ReadOptions?)"/>
    public unsafe byte[]? Get(ReadOnlySpan<byte> key, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        nint value;
        nuint length;
        fixed (byte* k = key)
            value = NativeMethods.rocksdb_transaction_get_cf(Handle, (options ?? _defaultReadOptions).Handle,
                cf.Handle, k, (nuint)key.Length, out length, ref err);

        NativeMethods.ThrowOnError(err);
        return CopyAndFree(value, length);
    }

    /// <summary>Reads a UTF-8 key as a string, or <see langword="null"/> if absent.</summary>
    public string? GetString(string key, ReadOptions? options = null)
    {
        byte[]? value = Get(Encoding.UTF8.GetBytes(key), options);
        return value is null ? null : Encoding.UTF8.GetString(value);
    }

    /// <inheritdoc cref="GetString(string, ReadOptions?)"/>
    public string? GetString(string key, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        byte[]? value = Get(Encoding.UTF8.GetBytes(key), cf, options);
        return value is null ? null : Encoding.UTF8.GetString(value);
    }

    /// <summary>
    /// Reads a key and locks it, so that no other transaction can change it
    /// before this one finishes.
    /// </summary>
    /// <param name="key">The key to read and lock.</param>
    /// <param name="exclusive">
    /// <see langword="true"/>, the default, takes a write lock. Passing
    /// <see langword="false"/> takes a shared lock, which several transactions
    /// may hold at once, so it guards against writers but not against another
    /// reader also intending to write.
    /// </param>
    /// <param name="options">Read options, or <see langword="null"/> for the defaults.</param>
    /// <remarks>
    /// <para>
    /// This is the read half of a read-modify-write, and the reason to use a
    /// transaction at all. Reading with <see cref="Get(ReadOnlySpan{byte}, ReadOptions?)"/>
    /// and then writing leaves a window in which another transaction can change
    /// the value.
    /// </para>
    /// <para>
    /// Throws if the lock cannot be taken within the timeout, or if
    /// <see cref="TransactionOptions.SetSnapshot"/> was set and the key has
    /// changed since the transaction began. Both are ordinary outcomes to retry,
    /// not bugs.
    /// </para>
    /// </remarks>
    public unsafe byte[]? GetForUpdate(ReadOnlySpan<byte> key, bool exclusive = true, ReadOptions? options = null)
    {
        nint err = default;
        nint value;
        nuint length;
        fixed (byte* k = key)
            value = NativeMethods.rocksdb_transaction_get_for_update(Handle, (options ?? _defaultReadOptions).Handle,
                k, (nuint)key.Length, out length, exclusive ? (byte)1 : (byte)0, ref err);

        NativeMethods.ThrowOnError(err);
        return CopyAndFree(value, length);
    }

    /// <inheritdoc cref="GetForUpdate(ReadOnlySpan{byte}, bool, ReadOptions?)"/>
    public unsafe byte[]? GetForUpdate(
        ReadOnlySpan<byte> key, ColumnFamilyHandle cf, bool exclusive = true, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        nint value;
        nuint length;
        fixed (byte* k = key)
            value = NativeMethods.rocksdb_transaction_get_for_update_cf(Handle, (options ?? _defaultReadOptions).Handle,
                cf.Handle, k, (nuint)key.Length, out length, exclusive ? (byte)1 : (byte)0, ref err);

        NativeMethods.ThrowOnError(err);
        return CopyAndFree(value, length);
    }

    /// <summary>Reads and locks a UTF-8 key.</summary>
    public string? GetStringForUpdate(string key, bool exclusive = true, ReadOptions? options = null)
    {
        byte[]? value = GetForUpdate(Encoding.UTF8.GetBytes(key), exclusive, options);
        return value is null ? null : Encoding.UTF8.GetString(value);
    }

    // ── Iteration ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an iterator over the database plus this transaction's pending
    /// writes.
    /// </summary>
    /// <remarks>
    /// The iterator is invalidated by <see cref="Commit"/>,
    /// <see cref="Rollback"/> and <see cref="RollbackToSavePoint"/>. Those
    /// dispose any iterator still open, so using one afterwards throws
    /// <see cref="ObjectDisposedException"/> rather than reading freed memory.
    /// </remarks>
    public Iterator NewIterator(ReadOptions? options = null)
    {
        nint handle = NativeMethods.rocksdb_transaction_create_iterator(
            Handle, (options ?? _defaultReadOptions).Handle);

        return Track(Iterator.FromHandle(handle, this, options));
    }

    /// <inheritdoc cref="NewIterator(ReadOptions?)"/>
    public Iterator NewIterator(ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint handle = NativeMethods.rocksdb_transaction_create_iterator_cf(
            Handle, (options ?? _defaultReadOptions).Handle, cf.Handle);

        return Track(Iterator.FromHandle(handle, this, options));
    }

    // ── Save points ──────────────────────────────────────────────────────────

    /// <summary>Marks a point that <see cref="RollbackToSavePoint"/> can return to.</summary>
    public void SetSavePoint() => NativeMethods.rocksdb_transaction_set_savepoint(Handle);

    /// <summary>
    /// Discards everything queued since the last <see cref="SetSavePoint"/>.
    /// </summary>
    /// <remarks>
    /// Invalidates any open iterator, so those are disposed first.
    /// </remarks>
    public void RollbackToSavePoint()
    {
        DisposeIterators();

        nint err = default;
        NativeMethods.rocksdb_transaction_rollback_to_savepoint(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ── Finishing ────────────────────────────────────────────────────────────

    /// <summary>Applies the transaction's writes to the database.</summary>
    /// <remarks>
    /// <para>
    /// Throws when the commit conflicts, which for a transaction created with
    /// <see cref="TransactionOptions.SetSnapshot"/> means a key it read has
    /// changed since. That is an ordinary outcome to retry.
    /// </para>
    /// <para>
    /// This does not release the transaction. Dispose it as well.
    /// </para>
    /// </remarks>
    public void Commit()
    {
        DisposeIterators();

        nint err = default;
        NativeMethods.rocksdb_transaction_commit(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Discards the transaction's writes and releases its locks.</summary>
    /// <remarks>This does not release the transaction. Dispose it as well.</remarks>
    public void Rollback()
    {
        DisposeIterators();

        nint err = default;
        NativeMethods.rocksdb_transaction_rollback(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Iterator Track(Iterator iterator)
    {
        lock (_gate)
        {
            _iterators.Add(iterator);
        }

        return iterator;
    }

    private void DisposeIterators()
    {
        Iterator[] open;
        lock (_gate)
        {
            if (_iterators.Count == 0)
            {
                return;
            }

            open = [.. _iterators];
            _iterators.Clear();
        }

        foreach (Iterator iterator in open)
        {
            iterator.Dispose();
        }
    }

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
        NativeMethods.rocksdb_transaction_destroy(Handle);
    }

    protected override void DisposeUnmanagedResources()
    {
        // Iterators read this transaction's write batch, so they go first.
        DisposeIterators();

        base.DisposeUnmanagedResources();
    }
}
