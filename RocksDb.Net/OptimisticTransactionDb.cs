using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace RocksDbNet;

/// <summary>
/// A database whose transactions detect conflicts at commit rather than by
/// locking. Maps to <c>rocksdb_optimistictransactiondb_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// The difference from <see cref="TransactionDb"/> is where the cost falls. A
/// transaction database locks every key as it is written and holds the lock
/// until the transaction ends, so a second writer waits for the lock timeout
/// and then fails; it also keeps a lock manager and can deadlock. This takes no
/// locks at all while a transaction runs. Instead <see cref="Transaction.Commit"/>
/// checks that nothing it read or wrote has changed since, and fails if it has.
/// </para>
/// <para>
/// That trade is worth making when conflicts are rare and expensive to prevent
/// — per-user or per-session keys that two writers almost never touch at once.
/// It is the wrong trade under real contention, where every conflicting
/// transaction does its work and then throws it away. A caller has to be
/// prepared to retry: a failed commit here means "someone else got there
/// first", not "the database is broken".
/// </para>
/// <para>
/// It also cannot deadlock, having no locks to wait on, so there is no lock
/// timeout to tune and no deadlock detection to configure.
/// </para>
/// <para>
/// A separate type from <see cref="RocksDb"/> and <see cref="TransactionDb"/>
/// rather than a subclass of either, for the same reason those are separate:
/// RocksDb gives this a distinct native type with its own close, and it has no
/// compaction, ingestion or range deletion of its own.
/// </para>
/// <para>
/// Opening takes ownership of the <see cref="DbOptions"/> and disposes them
/// when the database closes. The <see cref="OptimisticTransactionDbOptions"/>
/// are copied, so those may be disposed immediately.
/// </para>
/// <para>
/// The underlying non-transactional database is deliberately not exposed.
/// <c>rocksdb_optimistictransactiondb_get_base_db</c> hands back a
/// <c>rocksdb_t*</c> that must be released with
/// <c>rocksdb_optimistictransactiondb_close_base_db</c> rather than
/// <c>rocksdb_close</c> — wrapping it as a <see cref="RocksDb"/> would give
/// callers an object whose disposal closes the real database out from under
/// this one. <see cref="TransactionDb"/> withholds it for the same reason, and
/// the members worth reaching for through it are surfaced here directly.
/// </para>
/// </remarks>
public sealed class OptimisticTransactionDb : RocksDbHandle
{
    private const string DefaultColumnFamilyName = "default";

    private static readonly ReadOptions _defaultReadOptions = new();
    private static readonly WriteOptions _defaultWriteOptions = new();

    private readonly Dictionary<string, ColumnFamilyHandle> _columnFamilyHandles = [];
    private readonly DbOptions _ownedOptions;

    private ColumnFamilyHandle? _defaultColumnFamily;

    private OptimisticTransactionDb(nint handle, DbOptions options)
        : base(handle)
    {
        _ownedOptions = options;

        // A hold rather than a plain reference, for the reason TransactionDb
        // documents at length: a reference stops collection but not
        // finalization, .NET orders finalizers arbitrarily, and the options own
        // the comparator and env that closing this database reaches through.
        options.AddHolder();
    }

    private OptimisticTransactionDb(
        nint handle, nint[] cfHandles, IReadOnlyList<ColumnFamilyDescriptor> descriptors, DbOptions options)
        : base(handle)
    {
        _ownedOptions = options;
        options.AddHolder();

        for (int i = 0; i < cfHandles.Length; i++)
        {
            var cfh = new ColumnFamilyHandle(cfHandles[i]);
            cfh.SetParent(this);
            _columnFamilyHandles[descriptors[i].Name] = cfh;
        }
    }

    // ── Open ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens, or creates, an optimistic transaction database at
    /// <paramref name="path"/>.
    /// </summary>
    public static OptimisticTransactionDb Open(DbOptions options, string path)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        nint handle = NativeMethods.rocksdb_optimistictransactiondb_open(options.Handle, path, ref err);
        NativeMethods.ThrowOnError(err);

        return new OptimisticTransactionDb(handle, options);
    }

    /// <inheritdoc cref="Open(DbOptions, string)"/>
    public static OptimisticTransactionDb Open(
        DbOptions options, OptimisticTransactionDbOptions optimisticOptions, string path)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(optimisticOptions);
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        nint handle = NativeMethods.rocksdb_optimistictransactiondb_open_with_otxn_db_options(
            options.Handle, optimisticOptions.Handle, path, ref err);
        NativeMethods.ThrowOnError(err);

        return new OptimisticTransactionDb(handle, options);
    }

    /// <summary>
    /// Opens, or creates, an optimistic transaction database with the given
    /// column families.
    /// </summary>
    public static OptimisticTransactionDb Open(
        DbOptions options, string path, IReadOnlyList<ColumnFamilyDescriptor> columnFamilies)
        => OpenWithColumnFamilies(options, optimisticOptions: null, path, columnFamilies);

    /// <inheritdoc cref="Open(DbOptions, string, IReadOnlyList{ColumnFamilyDescriptor})"/>
    public static OptimisticTransactionDb Open(
        DbOptions options,
        OptimisticTransactionDbOptions optimisticOptions,
        string path,
        IReadOnlyList<ColumnFamilyDescriptor> columnFamilies)
    {
        ArgumentNullException.ThrowIfNull(optimisticOptions);
        return OpenWithColumnFamilies(options, optimisticOptions, path, columnFamilies);
    }

    private static unsafe OptimisticTransactionDb OpenWithColumnFamilies(
        DbOptions options,
        OptimisticTransactionDbOptions? optimisticOptions,
        string path,
        IReadOnlyList<ColumnFamilyDescriptor> columnFamilies)
    {
        ArgumentNullException.ThrowIfNull(options);
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
            {
                handle = optimisticOptions is null
                    ? NativeMethods.rocksdb_optimistictransactiondb_open_column_families(
                        options.Handle, path, count, names, opts, handles, ref err)
                    : NativeMethods.rocksdb_optimistictransactiondb_open_column_families_with_otxn_db_options(
                        options.Handle, optimisticOptions.Handle, path, count, names, opts, handles, ref err);
            }
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

        return new OptimisticTransactionDb(handle, cfHandles, columnFamilies, options);
    }

    // ── Transactions ─────────────────────────────────────────────────────────

    /// <summary>Begins an optimistic transaction.</summary>
    /// <remarks>
    /// <para>
    /// The returned transaction takes no locks. Reads and writes are buffered,
    /// and <see cref="Transaction.Commit"/> is where a conflict surfaces, as a
    /// <see cref="RocksDbException"/>. Treat that as a signal to retry rather
    /// than a failure.
    /// </para>
    /// <para>
    /// Dispose it whether or not it committed, and before this database.
    /// </para>
    /// </remarks>
    public Transaction BeginTransaction(
        WriteOptions? writeOptions = null, OptimisticTransactionOptions? transactionOptions = null)
    {
        using OptimisticTransactionOptions? owned =
            transactionOptions is null ? new OptimisticTransactionOptions() : null;

        nint handle = NativeMethods.rocksdb_optimistictransaction_begin(
            Handle,
            (writeOptions ?? _defaultWriteOptions).Handle,
            (transactionOptions ?? owned!).Handle,
            nint.Zero);

        return new Transaction(handle, this);
    }

    // ── Writes outside a transaction ─────────────────────────────────────────

    /// <summary>
    /// Applies a write batch straight to the database, without a transaction.
    /// </summary>
    /// <remarks>
    /// Atomic, like any write batch, but not validated against anything: it
    /// bypasses conflict detection entirely rather than taking part in it. Use
    /// it for writes that no transaction is racing.
    /// </remarks>
    public void Write(WriteBatch batch, WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(batch);

        nint err = default;
        NativeMethods.rocksdb_optimistictransactiondb_write(
            Handle, (options ?? _defaultWriteOptions).Handle, batch.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ── Column families ──────────────────────────────────────────────────────

    /// <summary>
    /// Looks up a column family named at open, throwing if there is none.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No column family of that name is known.</exception>
    public ColumnFamilyHandle GetColumnFamily(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return TryGetColumnFamily(name, out ColumnFamilyHandle? cfh)
            ? cfh
            : throw new KeyNotFoundException(
                $"No column family named '{name}' is known to this database. " +
                $"Known families: {string.Join(", ", ColumnFamilyNames)}.");
    }

    /// <summary>
    /// Looks up a column family, returning false rather than throwing when
    /// there is none.
    /// </summary>
    public bool TryGetColumnFamily(string name, [NotNullWhen(true)] out ColumnFamilyHandle? columnFamily)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (_columnFamilyHandles.TryGetValue(name, out ColumnFamilyHandle? cfh))
        {
            columnFamily = cfh;
            return true;
        }

        // Every database has a default family, even one opened without naming
        // any, so resolve it on demand rather than reporting it as unknown.
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
    /// <inheritdoc cref="TransactionDb.GetDefaultColumnFamily" path="/remarks"/>
    public ColumnFamilyHandle GetDefaultColumnFamily()
    {
        if (_defaultColumnFamily is not null)
        {
            return _defaultColumnFamily;
        }

        nint baseDb = NativeMethods.rocksdb_optimistictransactiondb_get_base_db(Handle);
        nint h;
        try
        {
            h = NativeMethods.rocksdb_get_default_column_family_handle(baseDb);
        }
        finally
        {
            // Releases only the rocksdb_t wrapper allocated above. This is the
            // one place the base database is reached for, and it is reached for
            // narrowly: the wrapper never escapes this method, so no caller can
            // dispose it and close the real database.
            NativeMethods.rocksdb_optimistictransactiondb_close_base_db(baseDb);
        }

        var cf = new ColumnFamilyHandle(h);
        cf.SetParent(this);

        _defaultColumnFamily = cf;
        return cf;
    }

    /// <summary>Names of the column families this database knows about.</summary>
    /// <remarks>
    /// The default family is always in here, whether or not it was named when
    /// the database was opened, and <see cref="GetColumnFamily"/> resolves it
    /// either way.
    /// </remarks>
    public IReadOnlyCollection<string> ColumnFamilyNames
        => _columnFamilyHandles.ContainsKey(DefaultColumnFamilyName)
            ? [.. _columnFamilyHandles.Keys]
            : [DefaultColumnFamilyName, .. _columnFamilyHandles.Keys];

    // ── Properties and checkpoints ───────────────────────────────────────────

    /// <summary>Reads a string-valued RocksDb property.</summary>
    public string? GetProperty(string propertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        nint ptr = NativeMethods.rocksdb_optimistictransactiondb_property_value(Handle, propertyName);
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
        int found = NativeMethods.rocksdb_optimistictransactiondb_property_int(Handle, propertyName, &value);
        return found == 0 ? value : null;
    }

    /// <summary>Creates a checkpoint object for this database.</summary>
    /// <remarks>
    /// Dispose the checkpoint before this database. It registers as a child, so
    /// the ordering is enforced rather than merely documented.
    /// </remarks>
    public Checkpoint CreateCheckpoint()
    {
        nint err = default;
        nint handle = NativeMethods.rocksdb_optimistictransactiondb_checkpoint_object_create(Handle, ref err);
        NativeMethods.ThrowOnError(err);

        return Checkpoint.FromHandle(handle, this);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_optimistictransactiondb_close(Handle);
    }

    protected override void DisposeUnmanagedResources()
    {
        // Column family handles, transactions and checkpoints must go before
        // the database closes: their destructors reach into database
        // internals. Each registered this as its parent, so the base releases
        // them, newest first, before the close.
        base.DisposeUnmanagedResources();

        // After the close, so callbacks the options own outlive the database
        // that calls them.
        _ownedOptions.ReleaseHolder();
    }
}
