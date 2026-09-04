using System.Runtime.InteropServices;
using System.Text;

namespace RocksDbNet;

/// <summary>
/// Options that control read operations.
/// Maps to <c>rocksdb_readoptions_t</c>.
/// </summary>
public sealed class ReadOptions : RocksDbHandle
{
    // Iteration bounds are stored by RocksDb as a Slice pointing at the caller's
    // buffer, and are dereferenced on every Seek/Next for as long as these options
    // are in use. Managed memory cannot satisfy that: a `fixed` pin ends when the
    // setter returns and the GC is then free to move or collect the buffer. So keep
    // an unmanaged copy per bound, owned by this instance.
    private NativeBound _upperBound;
    private NativeBound _lowerBound;

    public ReadOptions()
        : base(NativeMethods.rocksdb_readoptions_create())
    {
    }

    /// <summary>An unmanaged copy of an iteration bound, owned by the enclosing options.</summary>
    private readonly struct NativeBound(nint pointer, nuint length)
    {
        public nint Pointer { get; } = pointer;

        public nuint Length { get; } = length;

        public void Free()
        {
            if (Pointer != nint.Zero)
            {
                Marshal.FreeHGlobal(Pointer);
            }
        }
    }

    /// <summary>
    /// Copies <paramref name="key"/> into freshly allocated unmanaged memory and
    /// releases <paramref name="previous"/>. An empty key allocates nothing, which
    /// makes the native setter clear the bound.
    /// </summary>
    private static unsafe NativeBound CopyBound(ReadOnlySpan<byte> key, NativeBound previous)
    {
        NativeBound bound = default;

        if (!key.IsEmpty)
        {
            nint pointer = Marshal.AllocHGlobal(key.Length);
            try
            {
                key.CopyTo(new Span<byte>((void*)pointer, key.Length));
            }
            catch
            {
                Marshal.FreeHGlobal(pointer);
                throw;
            }

            bound = new NativeBound(pointer, (nuint)key.Length);
        }

        // Only release the old copy once the new one is in place, so a failed
        // allocation leaves the existing bound intact.
        previous.Free();
        return bound;
    }

    /// <summary>If true, all data read from underlying storage will be verified against checksums.</summary>
    public bool VerifyChecksums
    {
        get => NativeMethods.rocksdb_readoptions_get_verify_checksums(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_verify_checksums(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, the returned data block is added to the block cache.</summary>
    public bool FillCache
    {
        get => NativeMethods.rocksdb_readoptions_get_fill_cache(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_fill_cache(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Attaches a snapshot so reads reflect a consistent point-in-time view.</summary>
    public ReadOptions SetSnapshot(Snapshot? snapshot)
    {
        NativeMethods.rocksdb_readoptions_set_snapshot(Handle, snapshot?.Handle ?? nint.Zero);
        return this;
    }

    /// <summary>
    /// Sets the upper bound for iteration; the iterator will not return keys &gt;= this key.
    /// </summary>
    /// <remarks>
    /// The key is copied into unmanaged memory owned by this <see cref="ReadOptions"/>
    /// instance, so the caller does not need to keep <paramref name="key"/> alive or
    /// pinned. The copy is released when the bound is replaced and when this instance
    /// is disposed. Passing an empty span clears the bound.
    /// </remarks>
    public unsafe ReadOptions SetIterateUpperBound(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        _upperBound = CopyBound(key, _upperBound);
        NativeMethods.rocksdb_readoptions_set_iterate_upper_bound(Handle, (byte*)_upperBound.Pointer, _upperBound.Length);
        return this;
    }

    /// <summary>
    /// Sets the lower bound for iteration; the iterator will not return keys &lt; this key.
    /// </summary>
    /// <remarks>
    /// The key is copied into unmanaged memory owned by this <see cref="ReadOptions"/>
    /// instance, so the caller does not need to keep <paramref name="key"/> alive or
    /// pinned. The copy is released when the bound is replaced and when this instance
    /// is disposed. Passing an empty span clears the bound.
    /// </remarks>
    public unsafe ReadOptions SetIterateLowerBound(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        _lowerBound = CopyBound(key, _lowerBound);
        NativeMethods.rocksdb_readoptions_set_iterate_lower_bound(Handle, (byte*)_lowerBound.Pointer, _lowerBound.Length);
        return this;
    }

    /// <summary>
    /// Which tiers of storage the read is allowed to reach into. A read that
    /// cannot be answered from the permitted tiers returns no value rather than
    /// falling through to a slower one.
    /// </summary>
    public ReadTier ReadTier
    {
        get => (ReadTier)NativeMethods.rocksdb_readoptions_get_read_tier(Handle);
        set => NativeMethods.rocksdb_readoptions_set_read_tier(Handle, (int)value);
    }

    /// <summary>Specify to create a non-snapshot-based tailing iterator.</summary>
    public bool Tailing
    {
        get => NativeMethods.rocksdb_readoptions_get_tailing(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_tailing(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Readahead size in bytes for iteration and scans. Zero leaves RocksDb's
    /// automatic readahead in charge.
    /// </summary>
    /// <remarks>
    /// This applies to iterators, not to compaction. Compaction readahead is
    /// <see cref="DbOptions.CompactionReadaheadSize"/>. By default RocksDb
    /// already ramps readahead up on its own once it notices more than two
    /// reads of a table file, starting at 8 KB and doubling to 256 KB, so
    /// setting this only helps when scans are consistently larger than that.
    /// Values above 2 MB mainly pay off for forward iteration on spinning
    /// disks.
    /// </remarks>
    public ulong ReadaheadSize
    {
        get => (ulong)NativeMethods.rocksdb_readoptions_get_readahead_size(Handle);
        set => NativeMethods.rocksdb_readoptions_set_readahead_size(Handle, (nuint)value);
    }

    /// <summary>If true, all returned keys must share the same prefix as the seek key.</summary>
    public bool PrefixSameAsStart
    {
        get => NativeMethods.rocksdb_readoptions_get_prefix_same_as_start(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_prefix_same_as_start(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, the key and value memory an iterator hands out stays valid
    /// until the iterator moves or is disposed, rather than only until the next
    /// call.
    /// </summary>
    /// <remarks>
    /// Nothing to do with <c>PinnableSlice</c>, which pins on its own without
    /// this. This governs iterators, and the cost of it is that the blocks
    /// behind those pointers cannot be evicted while they are held.
    /// </remarks>
    public bool PinData
    {
        get => NativeMethods.rocksdb_readoptions_get_pin_data(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_pin_data(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, bypass prefix-based iteration and use total order (sorted) iteration.</summary>
    public bool TotalOrderSeek
    {
        get => NativeMethods.rocksdb_readoptions_get_total_order_seek(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_total_order_seek(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, enable asynchronous I/O during iteration.</summary>
    public bool AsyncIo
    {
        get => NativeMethods.rocksdb_readoptions_get_async_io(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_async_io(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, range deletion tombstones are ignored during reads.</summary>
    public bool IgnoreRangeDeletions
    {
        get => NativeMethods.rocksdb_readoptions_get_ignore_range_deletions(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_ignore_range_deletions(Handle, value ? (byte)1 : (byte)0);
    }

    // ── Readahead, prefix and I/O accounting ─────────────────────────────────

    /// <summary>
    /// If true, readahead size grows automatically as sequential reading
    /// continues, instead of staying at <see cref="ReadaheadSize"/>.
    /// </summary>
    public bool AdaptiveReadahead
    {
        get => NativeMethods.rocksdb_readoptions_get_adaptive_readahead(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_adaptive_readahead(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, RocksDb sizes iterator readahead itself based on the bounds of
    /// the scan.
    /// </summary>
    public bool AutoReadaheadSize
    {
        get => NativeMethods.rocksdb_readoptions_get_auto_readahead_size(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_auto_readahead_size(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, RocksDb infers from the iteration bounds whether a prefix seek is
    /// safe, allowing prefix optimisations without <see cref="PrefixSameAsStart"/>.
    /// </summary>
    public bool AutoPrefixMode
    {
        get => NativeMethods.rocksdb_readoptions_get_auto_prefix_mode(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_auto_prefix_mode(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, a long-running iterator periodically releases obsolete memory
    /// and file resources while still showing the same point-in-time view.
    /// Experimental, and does nothing unless a snapshot is set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It does not let the iterator see later writes. The opposite: it preserves
    /// the snapshot view and refreshes only the underlying resources, so a
    /// long-lived iterator stops pinning files and memory it no longer needs.
    /// It requires <see cref="SetSnapshot"/> to have been given a snapshot, and
    /// only takes effect while the iterator keeps making progress.
    /// </para>
    /// <para>
    /// Marked experimental by RocksDb, which expects to default it to true
    /// eventually. It has no effect on a transaction database using the
    /// write-prepared or write-unprepared policies, which are currently
    /// incompatible.
    /// </para>
    /// </remarks>
    public bool AutoRefreshIteratorWithSnapshot
    {
        get => NativeMethods.rocksdb_readoptions_get_auto_refresh_iterator_with_snapshot(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_auto_refresh_iterator_with_snapshot(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, an iterator may return an entry whose value has not been loaded
    /// yet, which avoids reading values the caller ends up skipping.
    /// </summary>
    /// <remarks>
    /// Applies to exactly two cases: large values held in blob files, and
    /// iterators spanning several column families. Everywhere else it has no
    /// effect at all, so setting it on an ordinary single-family iterator over
    /// non-blob data changes nothing.
    /// </remarks>
    public bool AllowUnpreparedValue
    {
        get => NativeMethods.rocksdb_readoptions_get_allow_unprepared_value(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_allow_unprepared_value(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, a multi-get reorders and batches its I/O for throughput rather
    /// than issuing reads in key order.
    /// </summary>
    public bool OptimizeMultiGetForIo
    {
        get => NativeMethods.rocksdb_readoptions_get_optimize_multiget_for_io(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_optimize_multiget_for_io(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Soft limit in bytes on the cumulative value size a single multi-get
    /// buffers. The default is <see cref="ulong.MaxValue"/>, which is the
    /// effective "no limit".
    /// </summary>
    /// <remarks>
    /// Zero is not "no limit"; it is the smallest possible limit. The read
    /// always makes progress, so at least one key is returned even when its
    /// value alone exceeds the limit, and every key after the limit is crossed
    /// comes back with an aborted status for the caller to retry. Setting zero
    /// therefore reduces a multi-get to roughly one key per call.
    /// </remarks>
    public ulong ValueSizeSoftLimit
    {
        get => NativeMethods.rocksdb_readoptions_get_value_size_soft_limit(Handle);
        set => NativeMethods.rocksdb_readoptions_set_value_size_soft_limit(Handle, value);
    }

    /// <summary>Priority this read is given by the rate limiter, if one is configured.</summary>
    public RateLimiterPriority RateLimiterPriority
    {
        get => (RateLimiterPriority)NativeMethods.rocksdb_readoptions_get_rate_limiter_priority(Handle);
        set => NativeMethods.rocksdb_readoptions_set_rate_limiter_priority(Handle, (int)value);
    }

    /// <summary>
    /// Labels the I/O this read performs. Leave this alone unless you have a
    /// reason to override how RocksDb accounts for the operation.
    /// </summary>
    public IoActivity IoActivity
    {
        get => (IoActivity)NativeMethods.rocksdb_readoptions_get_io_activity(Handle);
        set => NativeMethods.rocksdb_readoptions_set_io_activity(Handle, (int)value);
    }

    // ── Merge operand count threshold ────────────────────────────────────────
    // A tri-state on the native side: set, or not set at all, which is why it
    // comes with its own Has and Clear rather than a sentinel value.

    /// <summary>
    /// Whether a merge operand count threshold has been set.
    /// </summary>
    public bool HasMergeOperandCountThreshold
        => NativeMethods.rocksdb_readoptions_has_merge_operand_count_threshold(Handle) != 0;

    /// <summary>
    /// Number of merge operands above which RocksDb reports the read as needing
    /// compaction. Reading this when
    /// <see cref="HasMergeOperandCountThreshold"/> is <c>false</c> returns the
    /// native default rather than throwing.
    /// </summary>
    public ulong MergeOperandCountThreshold
    {
        get => (ulong)NativeMethods.rocksdb_readoptions_get_merge_operand_count_threshold(Handle);
        set => NativeMethods.rocksdb_readoptions_set_merge_operand_count_threshold(Handle, (nuint)value);
    }

    /// <summary>Unsets the merge operand count threshold.</summary>
    public ReadOptions ClearMergeOperandCountThreshold()
    {
        NativeMethods.rocksdb_readoptions_clear_merge_operand_count_threshold(Handle);
        return this;
    }

    // ── Request id ───────────────────────────────────────────────────────────

    /// <summary>
    /// An opaque identifier RocksDb attaches to this read's tracing and logging,
    /// for correlating a read with the rest of your system. <c>null</c> when none
    /// is set.
    /// </summary>
    /// <remarks>
    /// RocksDb copies the string, so nothing needs to stay alive on this side.
    /// Assigning <c>null</c> is the same as calling <see cref="ClearRequestId"/>.
    /// </remarks>
    public unsafe string? RequestId
    {
        get
        {
            byte* ptr = NativeMethods.rocksdb_readoptions_get_request_id(Handle, out nuint length);
            return ptr is null ? null : NativeMethods.PtrToStringUTF8(ptr, length);
        }
        set
        {
            if (value is null)
            {
                NativeMethods.rocksdb_readoptions_clear_request_id(Handle);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            fixed (byte* p = bytes)
                NativeMethods.rocksdb_readoptions_set_request_id(Handle, p, (nuint)bytes.Length);
        }
    }

    /// <summary>Removes any request identifier set on these options.</summary>
    public ReadOptions ClearRequestId()
    {
        NativeMethods.rocksdb_readoptions_clear_request_id(Handle);
        return this;
    }

    // ── User-defined index factory ───────────────────────────────────────────

    /// <summary>
    /// Name of the user-defined index factory in use, or <c>null</c> when none is
    /// configured.
    /// </summary>
    public unsafe string? TableIndexFactoryName
    {
        get
        {
            byte* ptr = NativeMethods.rocksdb_readoptions_get_table_index_factory_name(Handle, out nuint length);
            return ptr is null ? null : NativeMethods.PtrToStringUTF8(ptr, length);
        }
    }

    /// <summary>
    /// Selects a user-defined index factory by its RocksDb configuration string.
    /// </summary>
    /// <exception cref="RocksDbException">The string does not name a known factory.</exception>
    public unsafe ReadOptions SetTableIndexFactoryFromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        nint err = default;
        fixed (byte* p = bytes)
            NativeMethods.rocksdb_readoptions_set_table_index_factory_from_string(Handle, p, (nuint)bytes.Length, ref err);
        NativeMethods.ThrowOnError(err);
        return this;
    }

    /// <summary>Removes any user-defined index factory from these options.</summary>
    public ReadOptions ClearTableIndexFactory()
    {
        NativeMethods.rocksdb_readoptions_clear_table_index_factory(Handle);
        return this;
    }

    // ── Table filter ─────────────────────────────────────────────────────────

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte TableFilterCb(nint state, nint tableProperties);

    // Kept alive for as long as the native side holds a pointer into it.
    private TableFilterCb? _tableFilterCb;

    // Handed to RocksDb as the callback state. RocksDb calls the destructor on
    // re-set and when the options are destroyed, per ClearTableFilter in
    // db/c.cc, so freeing the GCHandle from there is safe.
    private GCHandle _tableFilterState;

    private static readonly TableFilterDestructorCb TableFilterDestructorDelegate = FreeTableFilterState;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TableFilterDestructorCb(nint state);

    /// <summary>
    /// Installs a predicate that decides which SST files a read may look at.
    /// Returning <c>false</c> skips the file entirely.
    /// </summary>
    /// <param name="filter">
    /// Called once per candidate SST file, on the thread performing the read, and
    /// concurrently when several reads are in flight, so it must be thread-safe
    /// and must not throw. An exception is caught and reported through
    /// <see cref="RocksDbCallbacks.UnhandledException"/>, and the file is then
    /// included, since excluding it would silently hide data.
    /// </param>
    /// <remarks>
    /// The <see cref="TablePropertiesView"/> passed in is only valid for the
    /// duration of the call. Use <see cref="TablePropertiesView.ToSnapshot"/> to
    /// keep any of it.
    /// </remarks>
    public unsafe ReadOptions SetTableFilter(Func<TablePropertiesView, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ThrowIfDisposed();

        // A GCHandle to the predicate is the state pointer. Setting a new filter
        // makes RocksDb run the previous destructor, which frees the old handle,
        // so no leak and no double free.
        GCHandle state = GCHandle.Alloc(filter);
        _tableFilterCb = InvokeTableFilter;
        _tableFilterState = state;

        NativeMethods.rocksdb_readoptions_set_table_filter(
            Handle,
            GCHandle.ToIntPtr(state),
            Marshal.GetFunctionPointerForDelegate(TableFilterDestructorDelegate),
            Marshal.GetFunctionPointerForDelegate(_tableFilterCb));

        return this;
    }

    /// <summary>Whether a table filter is installed on these options.</summary>
    public bool HasTableFilter
        => NativeMethods.rocksdb_readoptions_has_table_filter(Handle) != 0;

    /// <summary>Removes any table filter from these options.</summary>
    public ReadOptions ClearTableFilter()
    {
        // RocksDb runs the destructor for us, which releases the GCHandle.
        NativeMethods.rocksdb_readoptions_clear_table_filter(Handle);
        _tableFilterCb = null;
        _tableFilterState = default;
        return this;
    }

    private static byte InvokeTableFilter(nint state, nint tableProperties)
    {
        TablePropertiesView? view = null;
        try
        {
            if (GCHandle.FromIntPtr(state).Target is not Func<TablePropertiesView, bool> filter)
            {
                return 1; // Include the file: we cannot ask, so we must not exclude.
            }

            view = new TablePropertiesView(tableProperties);
            return filter(view) ? (byte)1 : (byte)0;
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report(nameof(SetTableFilter), ex, state);

            // Including the file is the only safe fallback. Excluding it would
            // quietly drop data from the read's results.
            return 1;
        }
        finally
        {
            // The native pointer dies with this call, so make later use throw
            // instead of reading freed memory.
            view?.Invalidate();
        }
    }

    private static void FreeTableFilterState(nint state)
    {
        try
        {
            if (state != nint.Zero)
            {
                GCHandle handle = GCHandle.FromIntPtr(state);
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report("table filter destructor", ex, state);
        }
    }

    // ── Deadlines and limits ─────────────────────────────────────────────────

    /// <summary>
    /// The point in time by which the read should give up, or
    /// <see langword="null"/> for no deadline. Default is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an absolute time, not a duration. RocksDb takes microseconds
    /// since the Unix epoch, so the natural mistake is to set it to how long the
    /// read may take and get a deadline in 1970, which has already passed. Use
    /// <see cref="SetDeadlineAfter"/> to express it as a duration from now.
    /// </para>
    /// <para>
    /// Best effort. A read can overrun the deadline when the file system does
    /// not support deadlines, and a batch read checks periodically rather than
    /// per key.
    /// </para>
    /// </remarks>
    public DateTimeOffset? Deadline
    {
        get
        {
            ulong micros = NativeMethods.rocksdb_readoptions_get_deadline(Handle);
            return micros == 0 ? null : DateTimeOffset.UnixEpoch.AddTicks(checked((long)micros) * TicksPerMicrosecond);
        }

        set => NativeMethods.rocksdb_readoptions_set_deadline(Handle, value is null ? 0 : ToUnixMicroseconds(value.Value));
    }

    /// <summary>
    /// Sets <see cref="Deadline"/> to <paramref name="timeout"/> from now.
    /// </summary>
    /// <param name="timeout">
    /// How long the read may take. <see cref="TimeSpan.Zero"/> or less clears
    /// the deadline rather than setting one in the past, because a deadline that
    /// has already passed is almost never what a caller means.
    /// </param>
    /// <remarks>
    /// The form callers actually want. It exists because <see cref="Deadline"/>
    /// is absolute and converting a timeout into an epoch offset by hand is easy
    /// to get wrong.
    /// </remarks>
    public ReadOptions SetDeadlineAfter(TimeSpan timeout)
    {
        Deadline = timeout > TimeSpan.Zero ? DateTimeOffset.UtcNow + timeout : null;
        return this;
    }

    /// <summary>
    /// How long a single file read may take, or <see cref="TimeSpan.Zero"/> for
    /// no limit. Default is <see cref="TimeSpan.Zero"/>.
    /// </summary>
    /// <remarks>
    /// A duration, unlike <see cref="Deadline"/>, and it applies per file read
    /// rather than to the operation as a whole. One get or seek can issue
    /// several reads, and each may take this long.
    /// </remarks>
    public TimeSpan IoTimeout
    {
        get => TimeSpan.FromTicks(checked((long)NativeMethods.rocksdb_readoptions_get_io_timeout(Handle)) * TicksPerMicrosecond);
        set => NativeMethods.rocksdb_readoptions_set_io_timeout(Handle, ToMicroseconds(value));
    }

    /// <summary>
    /// How many internal keys an iterator seek may skip before failing as
    /// incomplete. Zero, the default, means never fail.
    /// </summary>
    /// <remarks>
    /// The defence against a pathological seek. A large deleted range leaves
    /// tombstones behind until it is compacted away, and a seek into it walks
    /// every one, so a single seek can turn into an unbounded scan. Setting a
    /// limit makes that fail fast instead.
    /// </remarks>
    public ulong MaxSkippableInternalKeys
    {
        get => NativeMethods.rocksdb_readoptions_get_max_skippable_internal_keys(Handle);
        set => NativeMethods.rocksdb_readoptions_set_max_skippable_internal_keys(Handle, value);
    }

    /// <summary>
    /// Whether obsolete files are deleted on a background thread when an
    /// iterator is cleaned up, rather than on the thread disposing it. Default
    /// is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Worth setting when iterators are disposed on threads that should not
    /// block on file deletion. The database-level
    /// <see cref="DbOptions.AvoidUnnecessaryBlockingIo"/> overrides this one
    /// when enabled, so setting that makes this redundant.
    /// </remarks>
    public bool BackgroundPurgeOnIteratorCleanup
    {
        get => NativeMethods.rocksdb_readoptions_get_background_purge_on_iterator_cleanup(Handle) != 0;
        set => NativeMethods.rocksdb_readoptions_set_background_purge_on_iterator_cleanup(Handle, value ? (byte)1 : (byte)0);
    }

    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    private static ulong ToUnixMicroseconds(DateTimeOffset value)
    {
        long micros = (value.ToUniversalTime() - DateTimeOffset.UnixEpoch).Ticks / TicksPerMicrosecond;

        // A deadline before the epoch cannot be expressed, and zero is how
        // RocksDb spells "no deadline", so clamp rather than wrap into a
        // deadline the caller did not ask for.
        return micros <= 0 ? 0 : (ulong)micros;
    }

    private static ulong ToMicroseconds(TimeSpan value)
        => value <= TimeSpan.Zero ? 0 : (ulong)(value.Ticks / TicksPerMicrosecond);

    protected override void DisposeHandle()
    {
        // Destroying the options runs the table filter destructor, which frees
        // the GCHandle allocated in SetTableFilter.
        NativeMethods.rocksdb_readoptions_destroy(Handle);
    }

    protected override void DisposeUnmanagedResources()
    {
        // Destroy the native options first: they hold Slices into the bound
        // buffers, so the buffers must outlive them.
        base.DisposeUnmanagedResources();

        _upperBound.Free();
        _upperBound = default;

        _lowerBound.Free();
        _lowerBound = default;
    }
}
