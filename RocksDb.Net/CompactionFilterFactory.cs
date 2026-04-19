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

    // ── Instance state ───────────────────────────────────────────────────────
    private readonly nint _namePtr;
    private GCHandle _gcHandle;

    private readonly DestructorCallback _destructorCallback;
    private readonly CreateFilterCallback _createFilterCallback;
    private readonly NameCallback _nameCallback;

    // ── Construction ─────────────────────────────────────────────────────────
    protected CompactionFilterFactory(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        
        // Allocate unmanaged memory for the name string
        _namePtr = Marshal.StringToCoTaskMemUTF8(name);

        // Pin this instance so that the C++ callbacks can access it via the state pointer
        _gcHandle = GCHandle.Alloc(this);

        _destructorCallback = FCB_Destructor;
        _createFilterCallback = FCB_CreateFilter;
        _nameCallback = FCB_Name;

        Handle = NativeMethods.rocksdb_compactionfilterfactory_create(
            GCHandle.ToIntPtr(_gcHandle),
            Marshal.GetFunctionPointerForDelegate(_destructorCallback),
            Marshal.GetFunctionPointerForDelegate(_createFilterCallback),
            Marshal.GetFunctionPointerForDelegate(_nameCallback));
    }

    // ── Static callbacks ─────────────────────────────────────────────────────

    private static void FCB_Destructor(nint state)
    {
        var handle = GCHandle.FromIntPtr(state);
        var self = (CompactionFilterFactory)handle.Target!;
        self.TransferOwnership();
        handle.Free();
    }

    // Called by C++ for each compaction job. The returned filter handle is
    // wrapped in std::unique_ptr<CompactionFilter>; C++ deletes it when the
    // job finishes, which triggers the filter's own destructor callback.
    private static nint FCB_CreateFilter(nint state, nint contextPtr)
    {
        var self = SelfFromState(state);

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

    private static nint FCB_Name(nint state)
    {
        return SelfFromState(state)._namePtr;
    }

    private static CompactionFilterFactory SelfFromState(nint state) => (CompactionFilterFactory)GCHandle.FromIntPtr(state).Target!;

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

    public override void DisposeUnmanagedResources()
    {
        Marshal.FreeCoTaskMem(_namePtr);
    }
}