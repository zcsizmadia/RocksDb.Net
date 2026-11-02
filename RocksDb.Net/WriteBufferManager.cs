namespace RocksDbNet;

/// <summary>
/// A memory budget for memtables, shared across column families and across
/// databases. Maps to <c>rocksdb_write_buffer_manager_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DbOptions.WriteBufferSize"/> bounds one memtable. It says nothing
/// about the total, so a process with many column families or several databases
/// has no way to cap how much memory their memtables take together. This does.
/// </para>
/// <para>
/// Attach it with <see cref="DbOptions.WriteBufferManager"/>. RocksDb takes a
/// shared reference, so this object may be disposed once assigned, and sharing
/// one instance between databases is the point: they then draw on a common
/// budget rather than each having their own.
/// </para>
/// </remarks>
public sealed class WriteBufferManager : RocksDbHandle
{
    private WriteBufferManager(nint handle)
        : base(handle)
    {
    }

    /// <summary>Creates a manager with a budget of <paramref name="bufferSize"/> bytes.</summary>
    /// <param name="bufferSize">
    /// The total memtable budget. Zero disables the manager, which then tracks
    /// usage without enforcing anything.
    /// </param>
    /// <param name="allowStall">
    /// Whether writers are stalled when the budget is exhausted. When false, the
    /// budget is enforced by flushing sooner rather than by blocking.
    /// </param>
    public static WriteBufferManager Create(ulong bufferSize, bool allowStall = false)
        => new(NativeMethods.rocksdb_write_buffer_manager_create(
            checked((nuint)bufferSize), allowStall ? (byte)1 : (byte)0));

    /// <summary>
    /// Creates a manager that charges its memory against
    /// <paramref name="cache"/>, so that memtables and cached blocks compete for
    /// one budget.
    /// </summary>
    /// <remarks>
    /// Useful when the real constraint is total process memory rather than
    /// memtable memory specifically. The manager inserts placeholder entries
    /// into the cache to account for what the memtables hold; see
    /// <see cref="DummyEntriesInCacheUsage"/>.
    /// </remarks>
    public static WriteBufferManager Create(ulong bufferSize, Cache cache, bool allowStall = false)
    {
        ArgumentNullException.ThrowIfNull(cache);

        nint handle = NativeMethods.rocksdb_write_buffer_manager_create_with_cache(
            checked((nuint)bufferSize), cache.Handle, allowStall ? (byte)1 : (byte)0);
        GC.KeepAlive(cache);

        return new WriteBufferManager(handle);
    }

    /// <summary>
    /// Creates a manager with a budget of <paramref name="bufferSize"/> bytes
    /// that picks what to flush by <paramref name="flushPolicy"/>.
    /// </summary>
    /// <param name="bufferSize">
    /// The total memtable budget. Zero disables the manager, which then tracks
    /// usage without enforcing anything.
    /// </param>
    /// <param name="flushPolicy">Which memtable to flush when the budget is exceeded.</param>
    /// <param name="allowStall">
    /// Whether writers are stalled when the budget is exhausted. When false, the
    /// budget is enforced by flushing sooner rather than by blocking.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="flushPolicy"/> is not a defined value.</exception>
    public static WriteBufferManager Create(ulong bufferSize, WriteBufferFlushPolicy flushPolicy, bool allowStall = false)
    {
        ThrowIfUndefined(flushPolicy);

        return new(NativeMethods.rocksdb_write_buffer_manager_create_with_policy(
            checked((nuint)bufferSize), allowStall ? (byte)1 : (byte)0, (int)flushPolicy));
    }

    /// <summary>
    /// Creates a manager that charges its memory against
    /// <paramref name="cache"/> and picks what to flush by
    /// <paramref name="flushPolicy"/>.
    /// </summary>
    /// <remarks>
    /// See <see cref="Create(ulong, Cache, bool)"/> for what charging the cache
    /// does.
    /// </remarks>
    /// <param name="bufferSize">The total memtable budget, in bytes.</param>
    /// <param name="cache">The cache the memtable memory is charged against.</param>
    /// <param name="flushPolicy">Which memtable to flush when the budget is exceeded.</param>
    /// <param name="allowStall">Whether writers are stalled when the budget is exhausted.</param>
    /// <exception cref="ArgumentNullException"><paramref name="cache"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="flushPolicy"/> is not a defined value.</exception>
    public static WriteBufferManager Create(
        ulong bufferSize, Cache cache, WriteBufferFlushPolicy flushPolicy, bool allowStall = false)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ThrowIfUndefined(flushPolicy);

        nint handle = NativeMethods.rocksdb_write_buffer_manager_create_with_cache_and_policy(
            checked((nuint)bufferSize), cache.Handle, allowStall ? (byte)1 : (byte)0, (int)flushPolicy);
        GC.KeepAlive(cache);

        return new WriteBufferManager(handle);
    }

    /// <summary>
    /// Which memtable is flushed when the budget is exceeded. Defaults to
    /// <see cref="WriteBufferFlushPolicy.Oldest"/>, RocksDb's historical
    /// behaviour.
    /// </summary>
    /// <remarks>
    /// Can be changed while databases are using the manager.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined value.</exception>
    public WriteBufferFlushPolicy FlushPolicy
    {
        get => (WriteBufferFlushPolicy)NativeMethods.rocksdb_write_buffer_manager_flush_policy(Handle);
        set
        {
            // RocksDb ignores a value it does not recognise, so the assignment
            // would appear to succeed and change nothing.
            ThrowIfUndefined(value);
            NativeMethods.rocksdb_write_buffer_manager_set_flush_policy(Handle, (int)value);
        }
    }

    private static void ThrowIfUndefined(WriteBufferFlushPolicy policy)
    {
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Not a defined WriteBufferFlushPolicy.");
        }
    }

    /// <summary>
    /// Whether the manager is enforcing a budget, which it is only when the
    /// buffer size is non-zero.
    /// </summary>
    public bool IsEnabled => NativeMethods.rocksdb_write_buffer_manager_enabled(Handle) != 0;

    /// <summary>Whether this manager charges its memory against a cache.</summary>
    public bool CostsToCache => NativeMethods.rocksdb_write_buffer_manager_cost_to_cache(Handle) != 0;

    /// <summary>The budget, in bytes. Zero disables enforcement.</summary>
    public ulong BufferSize
    {
        get => (ulong)NativeMethods.rocksdb_write_buffer_manager_buffer_size(Handle);
        set => NativeMethods.rocksdb_write_buffer_manager_set_buffer_size(Handle, checked((nuint)value));
    }

    /// <summary>
    /// Whether writers are stalled when the budget is exhausted.
    /// </summary>
    /// <remarks>
    /// Write-only: RocksDb exposes no getter for it.
    /// </remarks>
    public bool AllowStall
    {
        set => NativeMethods.rocksdb_write_buffer_manager_set_allow_stall(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Memory currently attributed to memtables, in bytes, including those
    /// already sealed and waiting to be flushed.
    /// </summary>
    public ulong MemoryUsage => (ulong)NativeMethods.rocksdb_write_buffer_manager_memory_usage(Handle);

    /// <summary>
    /// Memory attributed to memtables still accepting writes, in bytes.
    /// </summary>
    /// <remarks>
    /// The difference between this and <see cref="MemoryUsage"/> is memory held
    /// by memtables that are sealed and waiting on a flush. A large gap means
    /// flushing is not keeping up.
    /// </remarks>
    public ulong MutableMemtableMemoryUsage
        => (ulong)NativeMethods.rocksdb_write_buffer_manager_mutable_memtable_memory_usage(Handle);

    /// <summary>
    /// Memory the manager has reserved in its cache through placeholder
    /// entries, in bytes.
    /// </summary>
    /// <remarks>
    /// Zero unless the manager was created with a cache. This is how memtable
    /// memory is made visible to the cache's accounting.
    /// </remarks>
    public ulong DummyEntriesInCacheUsage
        => (ulong)NativeMethods.rocksdb_write_buffer_manager_dummy_entries_in_cache_usage(Handle);

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_write_buffer_manager_destroy(Handle);
    }
}

/// <summary>
/// Which memtable a <see cref="WriteBufferManager"/> flushes when its budget is
/// exceeded.
/// </summary>
/// <remarks>
/// The values are the <c>rocksdb_write_buffer_manager_flush_policy_*</c>
/// constants in <c>c.h</c>, which the C API translates to
/// <c>WriteBufferFlushPolicy</c> in <c>include/rocksdb/write_buffer_manager.h</c>.
/// </remarks>
public enum WriteBufferFlushPolicy
{
    /// <summary>Flush the oldest mutable memtable. RocksDb's historical behaviour, and the default.</summary>
    Oldest = 0,

    /// <summary>Flush the largest mutable memtable in the database that hit the limit.</summary>
    Largest = 1,

    /// <summary>
    /// Flush the database that would free the most memory, among every database
    /// sharing this manager.
    /// </summary>
    LargestAcrossDbs = 2,
}
