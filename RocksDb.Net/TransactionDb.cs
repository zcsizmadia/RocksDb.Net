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

    private TransactionDb(nint handle, DbOptions options)
        : base(handle)
    {
        _ownedOptions = options;
    }

    private TransactionDb(nint handle, nint[] cfHandles, IReadOnlyList<ColumnFamilyDescriptor> descriptors, DbOptions options)
        : base(handle)
    {
        _ownedOptions = options;

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

        columnFamily = null;
        return false;
    }

    /// <summary>Names of the column families this database knows about.</summary>
    public IReadOnlyCollection<string> ColumnFamilyNames
        => _columnFamilyHandles.Count > 0 ? [.. _columnFamilyHandles.Keys] : [DefaultColumnFamilyName];

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
        // database that calls them.
        _ownedOptions.Dispose();
    }
}
