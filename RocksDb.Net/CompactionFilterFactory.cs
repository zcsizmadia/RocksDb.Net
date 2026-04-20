using System.Runtime.InteropServices;

namespace RocksDbNet;


/// <summary>
/// Creates a fresh <see cref="CompactionFilter"/> for each compaction / flush
/// job. Preferred over sharing a single filter instance when the filter
/// contains per-compaction state or when thread-safe access to a shared
/// instance is not practical.
/// </summary>
/// <remarks>
/// <b>Lifetime:</b> After passing the factory to
/// <see cref="DbOptions.SetCompactionFilterFactory"/>, the C++ options object
/// takes ownership (via <c>shared_ptr</c>). Do <em>not</em> dispose the factory
/// before the database and its options have been closed and disposed.
/// </remarks>
public abstract class CompactionFilterFactory : RocksDbHandle
{
    // ── Unmanaged delegate types ─────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestructorCallback(nint state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateFilterCallback(nint state, nint context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NameCallback(nint state);

    private readonly DestructorCallback _destructorCallback;
    private readonly CreateFilterCallback _createFilterCallback;
    private readonly NameCallback _nameCallback;

    // ── Construction ─────────────────────────────────────────────────────────
    protected CompactionFilterFactory(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        PinGarbageCollector(name);

        _destructorCallback = FCB_Destructor;
        _createFilterCallback = FCB_CreateFilter;
        _nameCallback = GetNameFromPinnedIntPtr;

        Handle = NativeMethods.rocksdb_compactionfilterfactory_create(
            GetPinnedIntPtr(),
            Marshal.GetFunctionPointerForDelegate(_destructorCallback),
            Marshal.GetFunctionPointerForDelegate(_createFilterCallback),
            Marshal.GetFunctionPointerForDelegate(_nameCallback));
    }

    // ── Static callbacks ─────────────────────────────────────────────────────

    private static void FCB_Destructor(nint state)
    {
        var self = GetSelfFromPinnedIntPtr<CompactionFilterFactory>(state);
        self.TransferOwnership();
        self.UnpinGarbageCollector();
    }

    // Called by C++ for each compaction job. The returned filter handle is
    // wrapped in std::unique_ptr<CompactionFilter>; C++ deletes it when the
    // job finishes, which triggers the filter's own destructor callback.
    private static nint FCB_CreateFilter(nint state, nint contextPtr)
    {
        var self = GetSelfFromPinnedIntPtr<CompactionFilterFactory>(state);

        var ctx = new CompactionFilterContext
        {
            IsFullCompaction = NativeMethods.rocksdb_compactionfiltercontext_is_full_compaction(contextPtr) != 0,
            IsManualCompaction = NativeMethods.rocksdb_compactionfiltercontext_is_manual_compaction(contextPtr) != 0,
        };

        CompactionFilter filter = self.CreateFilter(ctx);

        // Return the native handle. C++ now owns this handle; its destructor
        // callback will free the filter's GCHandle when compaction finishes.
        // The caller MUST NOT dispose `filter` — C++ manages its lifetime.
        return filter.Handle;
    }

    // ── Abstract factory method ──────────────────────────────────────────────
    /// <summary>
    /// Creates a new <see cref="CompactionFilter"/> for the given compaction job.
    /// Return a <em>freshly constructed</em> instance on every call; do not
    /// share instances between jobs. RocksDb owns the returned filter's
    /// lifetime — <b>do not dispose the returned filter</b>.
    /// </summary>
    protected abstract CompactionFilter CreateFilter(CompactionFilterContext context);

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_compactionfilterfactory_destroy(Handle);
    }
}