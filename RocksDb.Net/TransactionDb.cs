using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace RocksDbNet;

/// <summary>
/// A database that supports transactions with per-key locking and conflict
/// detection. Maps to <c>rocksdb_transactiondb_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every write takes locks, whether it goes through a
/// <see cref="Transaction"/> or straight through this object. A second writer
/// touching a locked key waits for the lock timeout and then fails, or fails at
/// once if deadlock detection spots a cycle.
/// </para>
/// <para>
/// This is a separate type from <see cref="RocksDb"/> rather than a subclass,
/// because RocksDb gives a transaction database a different native type with a
/// different close, and genuinely no compaction, ingestion, range deletion or
/// column family dropping. A subclass would inherit a dozen members that do not
/// exist here.
/// </para>
/// <para>
/// Like <see cref="RocksDb"/>, opening takes ownership of the
/// <see cref="DbOptions"/> and disposes it when the database closes. The
/// <see cref="TransactionDbOptions"/> are copied, so those may be disposed
/// immediately.
/// </para>
/// </remarks>
public sealed class TransactionDb : RocksDbHandle
{
    private const string DefaultColumnFamilyName = "default";

    private static readonly ReadOptions _defaultReadOptions = new();
    private static readonly WriteOptions _defaultWriteOptions = new();
    private static readonly FlushOptions _defaultFlushOptions = new();

    private readonly Dictionary<string, ColumnFamilyHandle> _columnFamilyHandles = [];
    private readonly DbOptions _ownedOptions;

    private ColumnFamilyHandle? _defaultColumnFamily;

    private TransactionDb(nint handle, DbOptions options)
        : base(handle)
    {
        _ownedOptions = options;

        // A hold, not just a reference. The options are disposed after the
        // close below, because their sub-objects have to outlive the database
        // that calls them — but a plain reference only stops collection, not
        // finalization, and .NET orders finalizers arbitrarily. A DbOptions is
        // necessarily allocated before the database it opens, so on an
        // abandoned database the options could be finalized first: that
        // destroyed the options and released their comparator, env and
        // compaction filter, and then rocksdb_close dereferenced them while
        // flushing the memtable and deleting files. The hold makes the release
        // wait for whoever lets go last, which is what it is for.
        options.AddHolder();
    }

    private TransactionDb(nint handle, nint[] cfHandles, IReadOnlyList<ColumnFamilyDescriptor> descriptors, DbOptions options)
        : base(handle)
    {
        _ownedOptions = options;

        // A hold, not just a reference. The options are disposed after the
        // close below, because their sub-objects have to outlive the database
        // that calls them — but a plain reference only stops collection, not
        // finalization, and .NET orders finalizers arbitrarily. A DbOptions is
        // necessarily allocated before the database it opens, so on an
        // abandoned database the options could be finalized first: that
        // destroyed the options and released their comparator, env and
        // compaction filter, and then rocksdb_close dereferenced them while
        // flushing the memtable and deleting files. The hold makes the release
        // wait for whoever lets go last, which is what it is for.
        options.AddHolder();

        for (int i = 0; i < cfHandles.Length; i++)
        {
            var cfh = new ColumnFamilyHandle(cfHandles[i]);
            cfh.SetParent(this);
            _columnFamilyHandles[descriptors[i].Name] = cfh;
        }
    }

    // ── Open ─────────────────────────────────────────────────────────────────

    /// <summary>Opens, or creates, a transaction database at <paramref name="path"/>.</summary>
    public static TransactionDb Open(DbOptions options, TransactionDbOptions transactionDbOptions, string path)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transactionDbOptions);
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        nint handle = NativeMethods.rocksdb_transactiondb_open(
            options.Handle, transactionDbOptions.Handle, path, ref err);
        NativeMethods.ThrowOnError(err);

        return new TransactionDb(handle, options);
    }

    /// <summary>
    /// Opens, or creates, a transaction database with the given column families.
    /// </summary>
    public static unsafe TransactionDb Open(
        DbOptions options,
        TransactionDbOptions transactionDbOptions,
        string path,
        IReadOnlyList<ColumnFamilyDescriptor> columnFamilies)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transactionDbOptions);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(columnFamilies);

        if (columnFamilies.Count == 0)
        {
            throw new ArgumentException("At least one column family descriptor is required.", nameof(columnFamilies));
        }

        int count = columnFamilies.Count;
        nint[] cfOptions = [.. columnFamilies.Select(cf => cf.Options.Handle)];
        nint[] cfHandles = new nint[count];
        byte[][] nameBytes = [.. columnFamilies.Select(cf => Encoding.UTF8.GetBytes(cf.Name + '\0'))];

        nint handle;
        nint err = default;
        var pins = new GCHandle[count];
        var namePtrs = new byte*[count];

        try
        {
            for (int i = 0; i < count; i++)
            {
                pins[i] = GCHandle.Alloc(nameBytes[i], GCHandleType.Pinned);
                namePtrs[i] = (byte*)pins[i].AddrOfPinnedObject();
            }

            fixed (byte** names = namePtrs)
            fixed (nint* opts = cfOptions)
            fixed (nint* handles = cfHandles)
                handle = NativeMethods.rocksdb_transactiondb_open_column_families(
                    options.Handle, transactionDbOptions.Handle, path, count, names, opts, handles, ref err);
        }
        finally
        {
            for (int i = 0; i < count; i++)
            {
                if (pins[i].IsAllocated)
                {
                    pins[i].Free();
                }
            }
        }

        NativeMethods.ThrowOnError(err);

        return new TransactionDb(handle, cfHandles, columnFamilies, options);
    }

    // ── Transactions ─────────────────────────────────────────────────────────

    /// <summary>Begins a transaction.</summary>
    /// <remarks>
    /// Dispose the returned transaction, whether or not it is committed.
    /// Committing and rolling back do not release it; they only decide what
    /// happens to its writes.
    /// </remarks>
    public Transaction BeginTransaction(
        WriteOptions? writeOptions = null, TransactionOptions? transactionOptions = null)
    {
        using TransactionOptions? owned = transactionOptions is null ? new TransactionOptions() : null;

        nint handle = NativeMethods.rocksdb_transaction_begin(
            Handle,
            (writeOptions ?? _defaultWriteOptions).Handle,
            (transactionOptions ?? owned!).Handle,
            nint.Zero);

        return new Transaction(handle, this);
    }

    /// <summary>
    /// Returns the transactions that were prepared but never committed or
    /// rolled back, so that recovery can resolve them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the other half of <see cref="Transaction.Prepare"/> and the
    /// reason to prepare at all. Reopening a database after a crash leaves any
    /// prepared transaction in place, still holding its locks; this is how a
    /// process finds them. Each carries the <see cref="Transaction.Name"/> it
    /// was prepared under, which is what lets a coordinator decide whether it
    /// should now commit or roll back.
    /// </para>
    /// <para>
    /// The returned transactions are owned by the caller: commit or roll each
    /// one back and then dispose it, exactly as for one from
    /// <see cref="BeginTransaction"/>. Leaving them undisposed keeps their keys
    /// locked against every other writer.
    /// </para>
    /// <para>
    /// An empty list is the normal result. A database that was closed cleanly
    /// has nothing outstanding.
    /// </para>
    /// </remarks>
    public unsafe IReadOnlyList<Transaction> GetPreparedTransactions()
    {
        nuint count;
        nint* handles = NativeMethods.rocksdb_transactiondb_get_prepared_transactions(Handle, &count);

        if (handles is null || count == 0)
        {
            // RocksDb still allocates the array for an empty result, so it has
            // to be freed even when there is nothing in it.
            if (handles is not null)
            {
                NativeMethods.rocksdb_free((nint)handles);
            }

            return [];
        }

        try
        {
            var transactions = new List<Transaction>(checked((int)count));

            // Each handle is wrapped as it is read, so that a failure part-way
            // through still leaves the ones already wrapped owned by something
            // that will destroy them.
            for (nuint i = 0; i < count; i++)
            {
                transactions.Add(new Transaction(handles[i], this));
            }

            return transactions;
        }
        finally
        {
            // The array is the caller's to free; the transactions in it are not
            // freed with it, which is why they are wrapped above.
            NativeMethods.rocksdb_free((nint)handles);
        }
    }

    // ── Reads and writes outside a transaction ───────────────────────────────

    /// <summary>Writes a key and value, taking the same locks a transaction would.</summary>
    public unsafe void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WriteOptions? options = null)
    {
        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_transactiondb_put(Handle, (options ?? _defaultWriteOptions).Handle,
                k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <inheritdoc cref="Put(ReadOnlySpan{byte}, ReadOnlySpan{byte}, WriteOptions?)"/>
    public unsafe void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ColumnFamilyHandle cf, WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_transactiondb_put_cf(Handle, (options ?? _defaultWriteOptions).Handle, cf.Handle,
                k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Writes a UTF-8 key and value.</summary>
    public void Put(string key, string value, WriteOptions? options = null)
        => Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), options);

    /// <inheritdoc cref="Put(string, string, WriteOptions?)"/>
    public void Put(string key, string value, ColumnFamilyHandle cf, WriteOptions? options = null)
        => Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), cf, options);

    /// <summary>Deletes a key.</summary>
    public unsafe void Delete(ReadOnlySpan<byte> key, WriteOptions? options = null)
    {
        nint err = default;
        fixed (byte* k = key)
            NativeMethods.rocksdb_transactiondb_delete(Handle, (options ?? _defaultWriteOptions).Handle,
                k, (nuint)key.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <inheritdoc cref="Delete(ReadOnlySpan{byte}, WriteOptions?)"/>
    public unsafe void Delete(ReadOnlySpan<byte> key, ColumnFamilyHandle cf, WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        fixed (byte* k = key)
            NativeMethods.rocksdb_transactiondb_delete_cf(Handle, (options ?? _defaultWriteOptions).Handle, cf.Handle,
                k, (nuint)key.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <inheritdoc cref="Delete(ReadOnlySpan{byte}, WriteOptions?)"/>
    public void Delete(string key, WriteOptions? options = null)
        => Delete(Encoding.UTF8.GetBytes(key), options);

    /// <summary>Applies a merge operation to a key.</summary>
    public unsafe void Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WriteOptions? options = null)
    {
        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_transactiondb_merge(Handle, (options ?? _defaultWriteOptions).Handle,
                k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <inheritdoc cref="Merge(ReadOnlySpan{byte}, ReadOnlySpan{byte}, WriteOptions?)"/>
    public unsafe void Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ColumnFamilyHandle cf, WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_transactiondb_merge_cf(Handle, (options ?? _defaultWriteOptions).Handle, cf.Handle,
                k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Applies a write batch atomically.</summary>
    public void Write(WriteBatch batch, WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(batch);

        nint err = default;
        NativeMethods.rocksdb_transactiondb_write(Handle, (options ?? _defaultWriteOptions).Handle, batch.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Reads a key, or returns <see langword="null"/> if it is absent.</summary>
    public unsafe byte[]? Get(ReadOnlySpan<byte> key, ReadOptions? options = null)
    {
        nint err = default;
        nint value;
        nuint length;
        fixed (byte* k = key)
            value = NativeMethods.rocksdb_transactiondb_get(Handle, (options ?? _defaultReadOptions).Handle,
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
            value = NativeMethods.rocksdb_transactiondb_get_cf(Handle, (options ?? _defaultReadOptions).Handle, cf.Handle,
                k, (nuint)key.Length, out length, ref err);

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
    /// Reads a key without copying the value into managed memory, or returns
    /// <see langword="null"/> if it is absent.
    /// </summary>
    public unsafe PinnableSlice? GetPinned(ReadOnlySpan<byte> key, ReadOptions? options = null)
    {
        nint err = default;
        nint slice;
        fixed (byte* k = key)
            slice = NativeMethods.rocksdb_transactiondb_get_pinned(Handle, (options ?? _defaultReadOptions).Handle,
                k, (nuint)key.Length, ref err);

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
            slice = NativeMethods.rocksdb_transactiondb_get_pinned_cf(Handle, (options ?? _defaultReadOptions).Handle,
                cf.Handle, k, (nuint)key.Length, ref err);

        NativeMethods.ThrowOnError(err);
        return slice == nint.Zero ? null : new PinnableSlice(slice, this);
    }

    // ── Iterators and snapshots ──────────────────────────────────────────────

    /// <summary>Creates an iterator over the default column family.</summary>
    public Iterator NewIterator(ReadOptions? options = null)
    {
        nint handle = NativeMethods.rocksdb_transactiondb_create_iterator(
            Handle, (options ?? _defaultReadOptions).Handle);

        return Iterator.FromHandle(handle, this, options);
    }

    /// <inheritdoc cref="NewIterator(ReadOptions?)"/>
    public Iterator NewIterator(ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint handle = NativeMethods.rocksdb_transactiondb_create_iterator_cf(
            Handle, (options ?? _defaultReadOptions).Handle, cf.Handle);

        return Iterator.FromHandle(handle, this, options);
    }

    /// <summary>Takes a point-in-time snapshot.</summary>
    public Snapshot NewSnapshot()
        => new(NativeMethods.rocksdb_transactiondb_create_snapshot(Handle), this);

    // ── Column families ──────────────────────────────────────────────────────

    /// <summary>Creates a column family and returns its handle.</summary>
    public ColumnFamilyHandle CreateColumnFamily(DbOptions options, string name)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(name);

        nint err = default;
        nint handle = NativeMethods.rocksdb_transactiondb_create_column_family(Handle, options.Handle, name, ref err);
        NativeMethods.ThrowOnError(err);

        var cf = new ColumnFamilyHandle(handle);
        cf.SetParent(this);
        _columnFamilyHandles.Add(name, cf);
        return cf;
    }

    /// <summary>Returns the handle for the column family called <paramref name="name"/>.</summary>
    /// <exception cref="KeyNotFoundException">No such column family is known.</exception>
    public ColumnFamilyHandle GetColumnFamily(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return TryGetColumnFamily(name, out ColumnFamilyHandle? cf)
            ? cf
            : throw new KeyNotFoundException(
                $"No column family named '{name}' is known to this database. " +
                $"Known families: {string.Join(", ", ColumnFamilyNames)}.");
    }

    /// <summary>
    /// Looks up a column family handle, returning false rather than throwing
    /// when there is none.
    /// </summary>
    public bool TryGetColumnFamily(string name, [NotNullWhen(true)] out ColumnFamilyHandle? columnFamily)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (_columnFamilyHandles.TryGetValue(name, out ColumnFamilyHandle? cf))
        {
            columnFamily = cf;
            return true;
        }

        // Every database has a default family, even one opened without naming
        // any, so resolve it on demand rather than reporting it as unknown.
        // Without this the listing and the lookup disagreed: ColumnFamilyNames
        // reported "default" and asking for it threw, with a message that
        // listed it among the known families.
        if (name == DefaultColumnFamilyName)
        {
            columnFamily = GetDefaultColumnFamily();
            return true;
        }

        columnFamily = null;
        return false;
    }

    /// <summary>
    /// Returns a non-owning wrapper around the default column family handle.
    /// Do <em>not</em> call Dispose on the returned handle — its lifetime is
    /// managed by the database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handle is only reachable through the underlying non-transactional
    /// database, since <c>rocksdb_get_default_column_family_handle</c> takes a
    /// <c>rocksdb_t*</c>. <c>rocksdb_transactiondb_get_base_db</c> allocates a
    /// <c>rocksdb_t</c> wrapper around a <c>DB*</c> this database owns, and
    /// <c>rocksdb_transactiondb_close_base_db</c> deletes that wrapper and
    /// nothing else — so the base database is not closed, and the column family
    /// handle taken from it outlives the wrapper it came through.
    /// </para>
    /// <para>
    /// Cached, because each call allocates a fresh
    /// <c>rocksdb_column_family_handle_t</c> and the wrapper is non-owning, so
    /// every call would otherwise leak one.
    /// </para>
    /// </remarks>
    public ColumnFamilyHandle GetDefaultColumnFamily()
    {
        if (_defaultColumnFamily is not null)
        {
            return _defaultColumnFamily;
        }

        nint baseDb = NativeMethods.rocksdb_transactiondb_get_base_db(Handle);
        nint h;
        try
        {
            h = NativeMethods.rocksdb_get_default_column_family_handle(baseDb);
        }
        finally
        {
            // Releases only the rocksdb_t wrapper allocated above. Using
            // rocksdb_close here instead would shut the real database.
            NativeMethods.rocksdb_transactiondb_close_base_db(baseDb);
        }

        var cf = new ColumnFamilyHandle(h);

        // Destroyed like any other handle rather than transferred away: the
        // native call sets immortal on the struct it allocates, and
        // rocksdb_column_family_handle_destroy honours that by deleting only
        // the wrapper and leaving the column family alone.
        cf.SetParent(this);

        _defaultColumnFamily = cf;
        return cf;
    }

    /// <inheritdoc cref="RocksDb.ColumnFamilyNames"/>
    public IReadOnlyCollection<string> ColumnFamilyNames
        => _columnFamilyHandles.ContainsKey(DefaultColumnFamilyName)
            ? [.. _columnFamilyHandles.Keys]
            : [DefaultColumnFamilyName, .. _columnFamilyHandles.Keys];

    // ── Maintenance ──────────────────────────────────────────────────────────

    /// <summary>Flushes the memtable to disk.</summary>
    public void Flush(FlushOptions? options = null)
    {
        nint err = default;
        NativeMethods.rocksdb_transactiondb_flush(Handle, (options ?? _defaultFlushOptions).Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <inheritdoc cref="Flush(FlushOptions?)"/>
    public void Flush(ColumnFamilyHandle cf, FlushOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        NativeMethods.rocksdb_transactiondb_flush_cf(Handle, (options ?? _defaultFlushOptions).Handle, cf.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Flushes the write-ahead log.</summary>
    /// <param name="sync">
    /// Whether to fsync the file as well. Flushing without syncing hands the
    /// buffer to the operating system and nothing more.
    /// </param>
    /// <remarks>
    /// There is no default, and there used to be one that disagreed with
    /// <see cref="RocksDb.FlushWal(bool)"/>. See that method for why both now
    /// require the argument.
    /// </remarks>
    public void FlushWal(bool sync)
    {
        nint err = default;
        NativeMethods.rocksdb_transactiondb_flush_wal(Handle, sync ? (byte)1 : (byte)0, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Flushes the write-ahead log, with control over the rate limiter priority
    /// as well as syncing.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="RocksDb.FlushWal(FlushWalOptions)"/>, which
    /// this type was missing even though the C API has it.
    /// </remarks>
    public void FlushWal(FlushWalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        nint err = default;
        NativeMethods.rocksdb_transactiondb_flush_wal_with_options(Handle, options.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Reads a string-valued RocksDb property.</summary>
    public string? GetProperty(string propertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        nint ptr = NativeMethods.rocksdb_transactiondb_property_value(Handle, propertyName);
        if (ptr == nint.Zero)
        {
            return null;
        }

        string? value = Marshal.PtrToStringUTF8(ptr);
        NativeMethods.rocksdb_free(ptr);
        return value;
    }

    /// <summary>Reads an integer-valued RocksDb property.</summary>
    public unsafe ulong? GetPropertyInt(string propertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        ulong value;
        int found = NativeMethods.rocksdb_transactiondb_property_int(Handle, propertyName, &value);
        return found == 0 ? value : null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
        NativeMethods.rocksdb_transactiondb_close(Handle);
    }

    protected override void DisposeUnmanagedResources()
    {
        // Column family handles and transactions must go before the database
        // closes, for the same reason as on RocksDb: their destructors reach
        // into database internals. Each registered this as its parent, so the
        // base releases them, newest first, before closing.
        base.DisposeUnmanagedResources();

        // After the close, so that callbacks the options own outlive the
        // database that calls them. Releasing the hold taken at Open rather
        // than disposing outright, so a caller who disposed the options early
        // defers to this instead of destroying a comparator under a live
        // database.
        _ownedOptions.ReleaseHolder();
    }
}
