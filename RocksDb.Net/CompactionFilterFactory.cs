using System.Runtime.CompilerServices;
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
/// <see cref="DbOptions.CompactionFilterFactory"/>, the C++ options object
/// shares ownership of it through a <c>shared_ptr</c>. Disposing the factory
/// is safe at any point: attaching registers a hold, so a <c>using</c> block
/// that ends first defers the release rather than performing it.
/// </remarks>
public abstract class CompactionFilterFactory : RocksDbHandle
{
    // ── Unmanaged delegate types ─────────────────────────────────────────────
        // Native entry points, not delegates. See Comparator for why.

    // ── Construction ─────────────────────────────────────────────────────────
    protected unsafe CompactionFilterFactory(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        PinGarbageCollector(name);

        Handle = NativeMethods.rocksdb_compactionfilterfactory_create(
            GetPinnedIntPtr(),
            (nint)(delegate* unmanaged[Cdecl]<nint, void>)&FCB_Destructor,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint>)&FCB_CreateFilter,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint>)&GetNameFromPinnedIntPtrSafe);
    }

    // ── Static callbacks ─────────────────────────────────────────────────────

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FCB_Destructor(nint state)
    {
        try
        {
            var self = GetSelfFromPinnedIntPtr<CompactionFilterFactory>(state);
            self.TransferOwnership();
            self.UnpinGarbageCollector();
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report("CompactionFilterFactory destructor", ex, state);
        }
    }

    // Called by C++ for each compaction job. The returned filter handle is
    // wrapped in std::unique_ptr<CompactionFilter>; C++ deletes it when the
    // job finishes, which triggers the filter's own destructor callback.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint FCB_CreateFilter(nint state, nint contextPtr)
    {
        try
        {
            var self = GetSelfFromPinnedIntPtr<CompactionFilterFactory>(state);

            var ctx = new CompactionFilterContext
            {
                IsFullCompaction = NativeMethods.rocksdb_compactionfiltercontext_is_full_compaction(contextPtr) != 0,
                IsManualCompaction = NativeMethods.rocksdb_compactionfiltercontext_is_manual_compaction(contextPtr) != 0,
            };

            CompactionFilter filter = self.CreateFilter(ctx);

            // Exclusive, because c.cc wraps what this returns in a fresh
            // std::unique_ptr<CompactionFilter> on every call. Returning the same
            // instance twice therefore gave two unique_ptrs over one pointer and
            // deleted it twice — heap corruption at job teardown, or a vtable read
            // through freed memory on a compaction thread, a long way from the
            // factory that caused it. Attaching exclusively turns the natural
            // mistake of caching one filter into a named InvalidOperationException,
            // which is what every other exclusive attachment in the library does.
            //
            // This also transfers ownership, so the wrapper will not free a filter
            // RocksDb is going to free itself. The caller must not dispose it.
            filter.AttachExclusively(nameof(CreateFilter));

            return filter.Handle;
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report(nameof(CreateFilter), ex, state);

            // c.cc wraps the returned pointer in std::unique_ptr<CompactionFilter>,
            // and RocksDb treats a null filter as "no filtering for this compaction
            // job", which every call site already guards for. So a null return is a
            // supported outcome that leaves the data untouched.
            return nint.Zero;
        }
    }

    // ── Abstract factory method ──────────────────────────────────────────────
    /// <summary>
    /// Creates a new <see cref="CompactionFilter"/> for the given compaction job.
    /// Return a <em>freshly constructed</em> instance on every call; do not
    /// share instances between jobs. RocksDb owns the returned filter's
    /// lifetime — <b>do not dispose the returned filter</b>.
    /// </summary>
    protected abstract CompactionFilter CreateFilter(CompactionFilterContext context);

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_compactionfilterfactory_destroy(Handle);
    }
}