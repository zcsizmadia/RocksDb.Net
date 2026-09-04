namespace RocksDbNet;

/// <summary>
/// The databases and caches a memory snapshot should cover.
/// </summary>
/// <remarks>
/// Build one of these, add what you care about, then take an
/// <see cref="ApproximateMemoryUsage"/> from it.
/// </remarks>
public sealed class MemoryConsumers : RocksDbHandle
{
    // What was added, held so that nothing here is collected while RocksDb is
    // still holding a raw pointer to it. Closing one of them explicitly is
    // still the caller's mistake to avoid, and the remarks on Add say so.
    private readonly List<RocksDbHandle> _added = [];

    private MemoryConsumers(nint handle)
        : base(handle)
    {
    }

    /// <summary>Creates an empty collection.</summary>
    public static MemoryConsumers Create()
        => new(NativeMethods.rocksdb_memory_consumers_create());

    /// <summary>Includes a database's memory in the snapshot.</summary>
    /// <remarks>
    /// RocksDb keeps a raw pointer to the database, so it must still be open
    /// when the snapshot is taken.
    /// </remarks>
    public MemoryConsumers Add(RocksDb db)
    {
        ArgumentNullException.ThrowIfNull(db);
        ThrowIfDisposed();

        NativeMethods.rocksdb_memory_consumers_add_db(Handle, db.Handle);
        _added.Add(db);
        return this;
    }

    /// <summary>Includes a cache's memory in the snapshot.</summary>
    /// <inheritdoc cref="Add(RocksDb)" path="/remarks"/>
    public MemoryConsumers Add(Cache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ThrowIfDisposed();

        NativeMethods.rocksdb_memory_consumers_add_cache(Handle, cache.Handle);
        _added.Add(cache);
        return this;
    }

    protected override void DisposeHandle()
        => NativeMethods.rocksdb_memory_consumers_destroy(Handle);
}

/// <summary>
/// How much memory a set of databases and caches is using, at one moment.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot, not a live view: each figure is read once when this is created
/// and does not move afterwards. Take another to see a change.
/// </para>
/// <para>
/// <b>What this adds, and what it does not.</b> Every individual figure below
/// is already reachable without it —
/// <see cref="Cache.Usage"/> for a cache,
/// <c>rocksdb.cur-size-all-mem-tables</c> and
/// <c>rocksdb.estimate-table-readers-mem</c> through
/// <see cref="RocksDb.GetAggregatedPropertyInt"/> for a database. What is new
/// is the aggregation: one snapshot spanning several databases and several
/// caches, which otherwise means writing the loop and knowing which properties
/// to sum. If you are asking about one database, the properties are the simpler
/// answer and this is not worth reaching for.
/// </para>
/// </remarks>
public sealed class ApproximateMemoryUsage : RocksDbHandle
{
    private ApproximateMemoryUsage(nint handle)
        : base(handle)
    {
    }

    /// <summary>Takes a snapshot over <paramref name="consumers"/>.</summary>
    /// <remarks>
    /// The consumers may be disposed once this returns: the figures are read
    /// here, not later.
    /// </remarks>
    public static ApproximateMemoryUsage Take(MemoryConsumers consumers)
    {
        ArgumentNullException.ThrowIfNull(consumers);

        nint err = default;
        nint handle = NativeMethods.rocksdb_approximate_memory_usage_create(consumers.Handle, ref err);
        NativeMethods.ThrowOnError(err);

        return new ApproximateMemoryUsage(handle);
    }

    /// <summary>Bytes held by memtables, flushed and unflushed.</summary>
    public ulong MemTableTotal
        => NativeMethods.rocksdb_approximate_memory_usage_get_mem_table_total(Handle);

    /// <summary>
    /// Bytes held by memtables whose contents are not yet in an SST file.
    /// </summary>
    /// <remarks>
    /// The part of <see cref="MemTableTotal"/> that a flush would release.
    /// </remarks>
    public ulong MemTableUnflushed
        => NativeMethods.rocksdb_approximate_memory_usage_get_mem_table_unflushed(Handle);

    /// <summary>Bytes held by table readers: index and filter blocks, mainly.</summary>
    public ulong MemTableReadersTotal
        => NativeMethods.rocksdb_approximate_memory_usage_get_mem_table_readers_total(Handle);

    /// <summary>Bytes held by the caches that were added.</summary>
    public ulong CacheTotal
        => NativeMethods.rocksdb_approximate_memory_usage_get_cache_total(Handle);

    protected override void DisposeHandle()
        => NativeMethods.rocksdb_approximate_memory_usage_destroy(Handle);
}
