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

        return new WriteBufferManager(NativeMethods.rocksdb_write_buffer_manager_create_with_cache(
            checked((nuint)bufferSize), cache.Handle, allowStall ? (byte)1 : (byte)0));
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
