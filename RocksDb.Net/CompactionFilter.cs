using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// Decision returned by a <see cref="CompactionFilter"/> for each key-value pair
/// encountered during table-file creation (compaction or flush).
/// </summary>
public enum FilterDecision
{
    /// <summary>Preserve the entry unchanged.</summary>
    Keep,

    /// <summary>
    /// Remove the entry, which inserts a tombstone and hides earlier versions of
    /// the key.
    /// </summary>
    /// <remarks>
    /// Only plain key-values and wide-column entities reach a filter at all. The
    /// C API installs a filter through the plain callback alone, so merge
    /// operands are never offered to one and this decision cannot drop them. A
    /// filter written to expire data will not expire a key whose value is built
    /// from merge operands.
    /// </remarks>
    Remove,

    /// <summary>
    /// Preserve the entry but replace its value with the byte array written
    /// to the <c>newValue</c> out parameter of
    /// <see cref="CompactionFilter.Filter"/>.
    /// </summary>
    ChangeValue,
}

/// <summary>
/// Context information passed to
/// <see cref="CompactionFilterFactory.CreateFilter"/> when RocksDb starts
/// a new compaction or flush job.
/// </summary>
public readonly struct CompactionFilterContext
{
    /// <summary>
    /// <c>true</c> when the job compacts all SST files (full compaction).
    /// </summary>
    public bool IsFullCompaction { get; init; }

    /// <summary>
    /// <c>true</c> when the compaction was triggered manually by the user.
    /// </summary>
    public bool IsManualCompaction { get; init; }
}

/// <summary>
/// User-defined compaction filter. Override <see cref="Filter"/> to inspect
/// or modify key-value pairs during table-file creation (compaction / flush).
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime:</b> Dispose it whenever you like. Attaching it through
/// <see cref="DbOptions.CompactionFilter"/> registers a hold, so disposing
/// while the database is open defers the release instead of performing it.
/// The usual <c>using</c> shape is safe.
/// </para>
/// <para>
/// <b>Thread safety:</b> When a single instance is registered and
/// multi-threaded compaction is active, <see cref="Filter"/> may be called
/// from multiple threads concurrently. Either make your override thread-safe
/// or use <see cref="CompactionFilterFactory"/> to create a separate instance
/// per compaction job.
/// </para>
/// </remarks>
public abstract class CompactionFilter : RocksDbHandle
{
    // ── Unmanaged delegate types ─────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestructorCb(nint state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate byte FilterCb(
        nint state, int level,
        byte* key, nuint keyLen,
        byte* value, nuint valLen,
        byte** newValue, nuint* newValueLen,
        byte* valueChanged);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NameCb(nint state);

    // Delegate instances kept as fields to prevent GC from collecting the
    // objects while the native side still holds function pointers into them.
    private readonly DestructorCb _destructorCb;
    private readonly FilterCb _filterCb;
    private readonly NameCb _nameCb;

    // Per-thread scratch space for the new-value buffer.
    // The C++ rocksdb_compactionfilter_t::Filter() method immediately copies
    // *new_value via std::string::assign after the callback returns — there is
    // no matching free() in the C layer. We therefore keep at most one
    // outstanding buffer per managed thread and release the previous one on the
    // next callback from that same thread.
    private readonly ConcurrentDictionary<int, nint> _lastNewValueBufsByThread = new();
    private readonly ConcurrentDictionary<nint, byte> _newValueBufs = new();

    /// <summary>Releases every outstanding new-value buffer.</summary>
    /// <remarks>
    /// Called from two places, and both are needed. A filter the caller owns
    /// releases them when it is disposed; a filter RocksDb owns, which is any
    /// filter a factory produced, is never disposed by the wrapper and
    /// releases them from its native destructor callback instead.
    /// </remarks>
    private void FreeNewValueBuffers()
    {
        _lastNewValueBufsByThread.Clear();

        foreach (nint buf in _newValueBufs.Keys)
        {
            Marshal.FreeHGlobal(buf);
        }

        _newValueBufs.Clear();
    }

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    private static void CB_Destructor(nint state)
    {
        try
        {
            var self = GetSelfFromPinnedIntPtr<CompactionFilter>(state);
            self.TransferOwnership();

            // A filter RocksDb owns, which is every filter a factory
            // produced, never has DisposeUnmanagedResources called on it, so
            // this destructor callback is the only place its new-value
            // buffers can be released. Without it each compaction job leaked
            // one buffer per thread that changed a value.
            self.FreeNewValueBuffers();

            self.UnpinGarbageCollector();
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report("CompactionFilter destructor", ex, state);
        }
    }

    private static unsafe byte CB_Filter(
        nint state, int level,
        byte* key, nuint keyLen,
        byte* val, nuint valLen,
        byte** newValue, nuint* newValueLen,
        byte* valueChanged)
    {
        // An exception must not reach native code. Keeping the entry unchanged is
        // the one fallback that cannot lose or alter data: the compaction simply
        // behaves as if this filter had declined to act. Note that a filter which
        // throws for every entry therefore turns into a no-op rather than an
        // error, which is why the exception is also reported.
        try
        {
            //var self = SelfFromState(state);
            var self = GetSelfFromPinnedIntPtr<CompactionFilter>(state);
            var keySpan = new ReadOnlySpan<byte>(key, checked((int)keyLen));
            var valSpan = new ReadOnlySpan<byte>(val, checked((int)valLen));

            // Release the buffer returned to C++ on the previous call from this
            // managed thread. C++ has already copied it via std::string::assign.
            int threadId = Environment.CurrentManagedThreadId;
            if (self._lastNewValueBufsByThread.TryRemove(threadId, out nint lastNewValueBuf) && lastNewValueBuf != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(lastNewValueBuf);
                self._newValueBufs.TryRemove(lastNewValueBuf, out _);
            }

            FilterDecision decision = self.Filter(level, keySpan, valSpan, out byte[]? newVal);

            // `is not null`, not `is { Length: > 0 }`. Requiring a positive
            // length meant that replacing a value with an empty one was
            // silently ignored and the old value kept, even though RocksDb
            // accepts an empty replacement. A filter that blanks a value had no
            // way to say so, and got no error either.
            if (decision == FilterDecision.ChangeValue && newVal is not null)
            {
                // At least one byte, so the pointer handed to RocksDb is always
                // valid and non-null even for an empty replacement. RocksDb
                // does a std::string::assign of the reported length, and while
                // a zero count would not dereference the pointer, passing a
                // real allocation avoids depending on that.
                nint buf = Marshal.AllocHGlobal(Math.Max(newVal.Length, 1));
                self._lastNewValueBufsByThread[threadId] = buf;
                self._newValueBufs.TryAdd(buf, 0);

                if (newVal.Length > 0)
                {
                    Marshal.Copy(newVal, 0, buf, newVal.Length);
                }

                *newValue = (byte*)buf;
                *newValueLen = (nuint)newVal.Length;
                *valueChanged = 1;
            }
            else
            {
                *valueChanged = 0;
            }

            // C API: return non-zero to remove the key, 0 to keep it.
            // ChangeValue keeps the key (return 0) with *valueChanged = 1.
            return decision == FilterDecision.Remove ? (byte)1 : (byte)0;
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report(nameof(Filter), ex, state);

            *valueChanged = 0;
            return 0; // Keep the entry unchanged.
        }
    }

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>Creates a compaction filter with the given name.</summary>
    /// <param name="name">
    /// Identifies the filter in RocksDb's logs and options output. Not
    /// enforced on reopen.
    /// </param>
    protected unsafe CompactionFilter(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        // Pin this instance so that the C++ callbacks can access it via the state pointer
        PinGarbageCollector(name);

        _destructorCb = CB_Destructor;
        _filterCb = CB_Filter;
        _nameCb = GetNameFromPinnedIntPtrSafe;

        Handle = NativeMethods.rocksdb_compactionfilter_create(
            GetPinnedIntPtr(),
            Marshal.GetFunctionPointerForDelegate(_destructorCb),
            Marshal.GetFunctionPointerForDelegate(_filterCb),
            Marshal.GetFunctionPointerForDelegate(_nameCb));
    }

    // ── Properties ───────────────────────────────────────────────────────────
    /// <summary>
    /// Whether the filter runs regardless of live snapshots. Always
    /// <see langword="true"/>, and setting it to <see langword="false"/> is not
    /// usable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three corrections to what this looks like it does. It defaults to
    /// <see langword="true"/>, not <see langword="false"/>. <see langword="true"/>
    /// means the filter <em>is</em> applied to every entry, not that it is
    /// skipped for snapshotted ones. And RocksDb has deprecated the setting:
    /// snapshots are always ignored for compaction filters, because not
    /// ignoring them never gave the guarantee it appeared to.
    /// </para>
    /// <para>
    /// Setting it to <see langword="false"/> does not restore the old
    /// behaviour; RocksDb fails table file creation instead, so compaction
    /// stops working. That is why <see langword="false"/> throws here rather
    /// than being passed through to break compaction later, at a point far
    /// from the call that caused it. Setting <see langword="true"/> is allowed
    /// and does nothing, since it is already true.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The value is <see langword="false"/>.
    /// </exception>
    [Obsolete(
        "RocksDb has deprecated this and always ignores snapshots for compaction filters. " +
        "Setting false fails table file creation.", error: false)]
    public bool IgnoreSnapshots
    {
        set
        {
            if (!value)
            {
                throw new NotSupportedException(
                    "RocksDb always ignores snapshots for compaction filters and has deprecated " +
                    "this setting. Returning false from IgnoreSnapshots makes RocksDb fail table " +
                    "file creation, which stops compaction, so this wrapper refuses the value " +
                    "rather than letting the failure surface later during a compaction.");
            }

            NativeMethods.rocksdb_compactionfilter_set_ignore_snapshots(Handle, 1);
        }
    }

    // ── Abstract filter method ───────────────────────────────────────────────
    /// <summary>
    /// Called for each key-value pair during table-file creation.
    /// </summary>
    /// <param name="level">The SST level of the file being created.</param>
    /// <param name="key">
    /// The key. The span is valid only for the duration of this call; copy the
    /// data if you need it beyond the call.
    /// </param>
    /// <param name="existingValue">
    /// The current value. Valid only for the duration of this call.
    /// </param>
    /// <param name="newValue">
    /// Output: when returning <see cref="FilterDecision.ChangeValue"/>, set
    /// this to the replacement value. Ignored for other decisions.
    /// </param>
    /// <returns>
    /// <see cref="FilterDecision.Keep"/>,
    /// <see cref="FilterDecision.Remove"/>, or
    /// <see cref="FilterDecision.ChangeValue"/>.
    /// </returns>
    protected abstract FilterDecision Filter(
        int level,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> existingValue,
        out byte[]? newValue);

    protected override void DisposeHandle()
    {
        try
        {
            NativeMethods.rocksdb_compactionfilter_destroy(Handle);
        }
        catch(Exception)
        {
            // Ignore exceptions during handle disposal to avoid unhandled exceptions in finalizer.
        }
    }

    protected override void DisposeUnmanagedResources()
    {
        base.DisposeUnmanagedResources();

        // Free up all the newValue allocations which were not freed yet.
        // This is a safety net in case the filter was disposed before all threads finished using it.

        FreeNewValueBuffers();
    }
}