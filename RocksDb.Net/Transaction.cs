using System.Runtime.InteropServices;
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
/// This is not repeatable read. A plain <see cref="Get(ReadOnlySpan{byte}, ReadOptions?)"/>
/// takes no lock and is not tracked, so a key can change underneath a
/// transaction between reading it and committing, and nothing reports that.
/// Conflict detection happens when a key is locked, by
/// <see cref="GetForUpdate(ReadOnlySpan{byte}, bool, ReadOptions?)"/> or by a
/// write, and not at <see cref="Commit"/>. A transaction whose correctness
/// depends on what it read has to read for update.
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

    // ── Batched reads ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads several keys in one call, seeing this transaction's pending
    /// writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One native call instead of one per key. A missing key yields
    /// <see langword="null"/> in the corresponding position.
    /// </para>
    /// <para>
    /// This takes no locks, exactly like
    /// <see cref="Get(ReadOnlySpan{byte}, ReadOptions?)"/>. Use
    /// <see cref="MultiGetForUpdate(IReadOnlyList{byte[]}, ReadOptions?)"/>
    /// for reads a later write depends on.
    /// </para>
    /// </remarks>
    public byte[]?[] MultiGet(IReadOnlyList<byte[]> keys, ReadOptions? options = null)
        => MultiGetCore(keys, columnFamilies: null, options, forUpdate: false);

    /// <inheritdoc cref="MultiGet(IReadOnlyList{byte[]}, ReadOptions?)"/>
    public byte[]?[] MultiGet(IReadOnlyList<byte[]> keys, ColumnFamilyHandle cf, ReadOptions? options = null)
        => MultiGetCore(keys, Repeat(cf, keys), options, forUpdate: false);

    /// <summary>
    /// Reads several keys in one call, each from the column family at the same
    /// position in <paramref name="columnFamilies"/>.
    /// </summary>
    /// <inheritdoc cref="MultiGet(IReadOnlyList{byte[]}, ReadOptions?)" path="/remarks"/>
    /// <exception cref="ArgumentException">The two lists are of different lengths.</exception>
    public byte[]?[] MultiGet(
        IReadOnlyList<byte[]> keys, IReadOnlyList<ColumnFamilyHandle> columnFamilies, ReadOptions? options = null)
        => MultiGetCore(keys, Handles(keys, columnFamilies), options, forUpdate: false);

    /// <summary>
    /// Reads several keys in one call and locks every one of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The batched form of
    /// <see cref="GetForUpdate(ReadOnlySpan{byte}, bool, ReadOptions?)"/>, and
    /// the more useful half of this pair: a transaction that reads a set of
    /// keys it intends to write is exactly the case conflict detection is for,
    /// and locking them one at a time is the slowest way to do it.
    /// </para>
    /// <para>
    /// Every key is locked, including ones that turn out to be absent. If any
    /// key cannot be locked the call throws, and the locks it did take stay
    /// held until the transaction ends — locking is not rolled back.
    /// </para>
    /// <para>
    /// The locks are always exclusive. Unlike
    /// <see cref="GetForUpdate(ReadOnlySpan{byte}, bool, ReadOptions?)"/> there
    /// is no shared-lock form, because the C API's batched variant takes no
    /// such flag; a shared lock on a set of keys means reading them one at a
    /// time.
    /// </para>
    /// </remarks>
    /// <param name="keys">Keys to read and lock.</param>
    /// <param name="options">Read options, or null for the defaults.</param>
    public byte[]?[] MultiGetForUpdate(IReadOnlyList<byte[]> keys, ReadOptions? options = null)
        => MultiGetCore(keys, columnFamilies: null, options, forUpdate: true);

    /// <inheritdoc cref="MultiGetForUpdate(IReadOnlyList{byte[]}, ReadOptions?)"/>
    public byte[]?[] MultiGetForUpdate(
        IReadOnlyList<byte[]> keys, ColumnFamilyHandle cf, ReadOptions? options = null)
        => MultiGetCore(keys, Repeat(cf, keys), options, forUpdate: true);

    /// <inheritdoc cref="MultiGetForUpdate(IReadOnlyList{byte[]}, ReadOptions?)"/>
    /// <exception cref="ArgumentException">The two lists are of different lengths.</exception>
    public byte[]?[] MultiGetForUpdate(
        IReadOnlyList<byte[]> keys,
        IReadOnlyList<ColumnFamilyHandle> columnFamilies,
        ReadOptions? options = null)
        => MultiGetCore(keys, Handles(keys, columnFamilies), options, forUpdate: true);

    // ── Reads that avoid a copy ──────────────────────────────────────────────

    /// <summary>
    /// Reads a key without copying the value into managed memory, or returns
    /// <see langword="null"/> if it is absent.
    /// </summary>
    /// <remarks>
    /// Dispose the result promptly: it pins the block the value came from,
    /// which cannot be evicted from the block cache while it lives. See
    /// <see cref="PinnableSlice"/>.
    /// </remarks>
    public unsafe PinnableSlice? GetPinned(ReadOnlySpan<byte> key, ReadOptions? options = null)
    {
        nint err = default;
        nint slice;
        fixed (byte* k = key)
            slice = NativeMethods.rocksdb_transaction_get_pinned(
                Handle, (options ?? _defaultReadOptions).Handle, k, (nuint)key.Length, ref err);

        // A null return means either "not found" or "failed", so the error has
        // to be checked before deciding which.
        NativeMethods.ThrowOnError(err);

        return slice == nint.Zero ? null : new PinnableSlice(slice, this);
    }

    /// <inheritdoc cref="GetPinned(ReadOnlySpan{byte}, ReadOptions?)"/>
    public unsafe PinnableSlice? GetPinned(ReadOnlySpan<byte> key, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        nint slice;
        fixed (byte* k = key)
            slice = NativeMethods.rocksdb_transaction_get_pinned_cf(
                Handle, (options ?? _defaultReadOptions).Handle, cf.Handle, k, (nuint)key.Length, ref err);

        NativeMethods.ThrowOnError(err);

        return slice == nint.Zero ? null : new PinnableSlice(slice, this);
    }

    /// <summary>
    /// Reads a key without copying the value, and locks it.
    /// </summary>
    /// <inheritdoc cref="GetPinned(ReadOnlySpan{byte}, ReadOptions?)" path="/remarks"/>
    public unsafe PinnableSlice? GetPinnedForUpdate(
        ReadOnlySpan<byte> key, bool exclusive = true, ReadOptions? options = null)
    {
        nint err = default;
        nint slice;
        fixed (byte* k = key)
            slice = NativeMethods.rocksdb_transaction_get_pinned_for_update(
                Handle, (options ?? _defaultReadOptions).Handle,
                k, (nuint)key.Length, exclusive ? (byte)1 : (byte)0, ref err);

        NativeMethods.ThrowOnError(err);

        return slice == nint.Zero ? null : new PinnableSlice(slice, this);
    }

    /// <inheritdoc cref="GetPinnedForUpdate(ReadOnlySpan{byte}, bool, ReadOptions?)"/>
    public unsafe PinnableSlice? GetPinnedForUpdate(
        ReadOnlySpan<byte> key, ColumnFamilyHandle cf, bool exclusive = true, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        nint slice;
        fixed (byte* k = key)
            slice = NativeMethods.rocksdb_transaction_get_pinned_for_update_cf(
                Handle, (options ?? _defaultReadOptions).Handle, cf.Handle,
                k, (nuint)key.Length, exclusive ? (byte)1 : (byte)0, ref err);

        NativeMethods.ThrowOnError(err);

        return slice == nint.Zero ? null : new PinnableSlice(slice, this);
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

    // ── Two-phase commit ─────────────────────────────────────────────────────

    /// <summary>
    /// The name this transaction is known by, which is what makes it findable
    /// again after a crash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty until set. A name has to be given before <see cref="Prepare"/>,
    /// must be unique among live transactions, and cannot be changed once set.
    /// </para>
    /// <para>
    /// This is the identifier
    /// <see cref="TransactionDb.GetPreparedTransactions"/> hands back, so it
    /// wants to mean something to the recovering process — a message id or a
    /// coordinator's transaction id, rather than a counter that restarts with
    /// the program.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
    /// <exception cref="RocksDbException">
    /// The name is already taken, or this transaction already has one.
    /// </exception>
    public string Name
    {
        get
        {
            nint ptr = NativeMethods.rocksdb_transaction_get_name(Handle, out nuint length);
            return CopyAndFreeUtf8(ptr, length);
        }

        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);

            nint err = default;

            // The length is in bytes, not characters: the marshaller writes
            // UTF-8, so a non-ASCII name would otherwise be truncated mid-way
            // and the transaction registered under a name nothing can look up.
            NativeMethods.rocksdb_transaction_set_name(
                Handle, value, (nuint)Encoding.UTF8.GetByteCount(value), ref err);

            NativeMethods.ThrowOnError(err);
        }
    }

    /// <summary>
    /// Makes this transaction's writes durable without committing them, so that
    /// it survives a crash and can be resolved afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first half of two-phase commit. After this returns, the transaction
    /// is on disk in a prepared state: a later <see cref="Commit"/> applies it
    /// and <see cref="Rollback"/> discards it, and if the process dies in
    /// between, <see cref="TransactionDb.GetPreparedTransactions"/> finds it
    /// again in the reopened database. Without preparing, an interrupted
    /// transaction is simply gone.
    /// </para>
    /// <para>
    /// A prepared transaction holds its locks until it is resolved, including
    /// across the restart. Recovery that never commits or rolls one back leaves
    /// those keys locked against every other writer.
    /// </para>
    /// </remarks>
    /// <exception cref="RocksDbException">
    /// The transaction has no <see cref="Name"/>, or is not in a state that can
    /// be prepared.
    /// </exception>
    public void Prepare()
    {
        // Iterators read the write batch this is about to make durable, and
        // Commit and Rollback already dispose them for the same reason.
        DisposeIterators();

        nint err = default;
        NativeMethods.rocksdb_transaction_prepare(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ── Finishing ────────────────────────────────────────────────────────────

    /// <summary>Applies the transaction's writes to the database.</summary>
    /// <remarks>
    /// <para>
    /// Conflicts are not detected here. A pessimistic transaction, which is
    /// what <see cref="TransactionDb"/> gives you, takes a lock and checks for
    /// a conflicting change when the key is written or read for update, so a
    /// conflict has already thrown from <see cref="Put(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    /// or <see cref="GetForUpdate(ReadOnlySpan{byte}, bool, ReadOptions?)"/>
    /// long before this is called. That is the ordinary outcome to retry.
    /// </para>
    /// <para>
    /// A key read with a plain <see cref="Get(ReadOnlySpan{byte}, ReadOptions?)"/>
    /// is not tracked and never causes a conflict, whether or not
    /// <see cref="TransactionOptions.SetSnapshot"/> was used. Measured: a
    /// transaction that read a key, watched another transaction change and
    /// commit it, and then committed its own unrelated write, committed
    /// successfully. Use <c>GetForUpdate</c> for reads a decision depends on.
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

    /// <summary>The same column family for every key.</summary>
    private static nint[] Repeat(ColumnFamilyHandle cf, IReadOnlyList<byte[]> keys)
    {
        ArgumentNullException.ThrowIfNull(cf);
        ArgumentNullException.ThrowIfNull(keys);

        nint[] handles = new nint[keys.Count];
        Array.Fill(handles, cf.Handle);
        return handles;
    }

    /// <summary>One column family per key, checked for length agreement.</summary>
    private static nint[] Handles(IReadOnlyList<byte[]> keys, IReadOnlyList<ColumnFamilyHandle> columnFamilies)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(columnFamilies);

        if (keys.Count != columnFamilies.Count)
        {
            throw new ArgumentException(
                $"Expected one column family per key, but got {keys.Count} keys and " +
                $"{columnFamilies.Count} column families.",
                nameof(columnFamilies));
        }

        nint[] handles = new nint[columnFamilies.Count];
        for (int i = 0; i < columnFamilies.Count; i++)
        {
            ColumnFamilyHandle cf = columnFamilies[i];
            ArgumentNullException.ThrowIfNull(cf);
            handles[i] = cf.Handle;
        }

        return handles;
    }

    /// <summary>
    /// Marshals the key list, makes the one native call, then copies and frees
    /// every value before considering the errors.
    /// </summary>
    /// <remarks>
    /// The ordering is the point: throwing from inside the copy loop would leak
    /// the values and error strings for every key after the first failure, and
    /// that is exactly the defect the database's own MultiGet was fixed for.
    /// </remarks>
    private unsafe byte[]?[] MultiGetCore(
        IReadOnlyList<byte[]> keys, nint[]? columnFamilies, ReadOptions? options, bool forUpdate)
    {
        ArgumentNullException.ThrowIfNull(keys);

        int n = keys.Count;
        if (n == 0)
        {
            return [];
        }

        byte*[] keyPtrs = new byte*[n];
        nuint[] keySizes = new nuint[n];
        byte*[] valPtrs = new byte*[n];
        nuint[] valSizes = new nuint[n];
        nint[] errs = new nint[n];

        var pins = new GCHandle[n];
        try
        {
            for (int i = 0; i < n; i++)
            {
                ArgumentNullException.ThrowIfNull(keys[i]);
                pins[i] = GCHandle.Alloc(keys[i], GCHandleType.Pinned);
                keyPtrs[i] = (byte*)pins[i].AddrOfPinnedObject();
                keySizes[i] = (nuint)keys[i].Length;
            }

            nint opts = (options ?? _defaultReadOptions).Handle;

            fixed (byte** kp = keyPtrs)
            fixed (nuint* ks = keySizes)
            fixed (byte** vp = valPtrs)
            fixed (nuint* vs = valSizes)
            fixed (nint* ep = errs)
            fixed (nint* cfp = columnFamilies)
            {
                if (columnFamilies is null)
                {
                    if (forUpdate)
                    {
                        NativeMethods.rocksdb_transaction_multi_get_for_update(
                            Handle, opts, (nuint)n, kp, ks, vp, vs, (byte**)ep);
                    }
                    else
                    {
                        NativeMethods.rocksdb_transaction_multi_get(
                            Handle, opts, (nuint)n, kp, ks, vp, vs, (byte**)ep);
                    }
                }
                else if (forUpdate)
                {
                    NativeMethods.rocksdb_transaction_multi_get_for_update_cf(
                        Handle, opts, cfp, (nuint)n, kp, ks, vp, vs, (byte**)ep);
                }
                else
                {
                    NativeMethods.rocksdb_transaction_multi_get_cf(
                        Handle, opts, cfp, (nuint)n, kp, ks, vp, vs, (byte**)ep);
                }
            }
        }
        finally
        {
            for (int i = 0; i < n; i++)
            {
                if (pins[i].IsAllocated)
                {
                    pins[i].Free();
                }
            }
        }

        var results = new byte[]?[n];
        for (int i = 0; i < n; i++)
        {
            if (valPtrs[i] is not null)
            {
                results[i] = new ReadOnlySpan<byte>(valPtrs[i], checked((int)valSizes[i])).ToArray();
                NativeMethods.rocksdb_free((nint)valPtrs[i]);
            }
        }

        NativeMethods.ThrowFirstError(errs);
        return results;
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

    /// <summary>
    /// Copies a native UTF-8 string the caller owns, and frees it.
    /// </summary>
    /// <remarks>
    /// An unnamed transaction returns a zero-length name rather than a null
    /// pointer, so both are treated as "no name" and neither is an error.
    /// </remarks>
    private static unsafe string CopyAndFreeUtf8(nint value, nuint length)
    {
        if (value == nint.Zero)
        {
            return string.Empty;
        }

        string result = Encoding.UTF8.GetString((byte*)value, checked((int)length));
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
