using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Xml.Linq;

using RocksDbNet.Native;

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
    /// Remove the entry. For plain key-values and wide-column entities this
    /// inserts a tombstone (Delete), which hides earlier versions of the key.
    /// For merge operands the operand is simply dropped.
    /// </summary>
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
/// <see cref="CompactionFilterFactory.CreateFilter"/> when RocksDB starts
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
/// <b>Lifetime:</b> A filter instance registered via
/// <see cref="DbOptions.SetCompactionFilter"/> must remain alive (not disposed)
/// for the entire lifetime of the database. Dispose it only after the
/// <see cref="RocksDb"/> instance has been closed.
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
        byte* key,   nuint keyLen,
        byte* value, nuint valLen,
        byte** newValue, nuint* newValueLen,
        byte* valueChanged);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NameCb(nint state);

    // ── Instance state ───────────────────────────────────────────────────────
    private readonly nint _namePtr;   // CoTaskMem UTF-8 name string
    private GCHandle   _gcHandle;     // strong root → object stays alive while native holds it
    private int _nativeDestroyed;     // 1 when the native destructor has already fired
    
    // Delegate instances kept as fields to prevent GC from collecting the
    // objects while the native side still holds function pointers into them.
    private readonly DestructorCb _destructorCb;
    private readonly FilterCb     _filterCb;
    private readonly NameCb       _nameCb;

    // Per-thread scratch space for the new-value buffer.
    // The C++ rocksdb_compactionfilter_t::Filter() method immediately copies
    // *new_value via std::string::assign after the callback returns — there is
    // no matching free() in the C layer. We therefore allocate with
    // Marshal.AllocHGlobal and free it at the start of the NEXT call on the
    // same thread, accepting a maximum of one outstanding buffer per thread.
    [ThreadStatic]
    private static nint t_lastNewValueBuf;

    private readonly ConcurrentDictionary<nint, byte> _newValueBufs = new();

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    private static void CB_Destructor(nint state)
    {
        var handle = GCHandle.FromIntPtr(state);
        var self = (CompactionFilter)handle.Target!;
        Interlocked.Exchange(ref self._nativeDestroyed, 1);
        handle.Free();
    }

    private static unsafe byte CB_Filter(
        nint state, int level,
        byte* key, nuint keyLen,
        byte* val, nuint valLen,
        byte** newValue, nuint* newValueLen,
        byte* valueChanged)
    {
        var self = SelfFromState(state);
        var keySpan = new ReadOnlySpan<byte>(key, checked((int)keyLen));
        var valSpan = new ReadOnlySpan<byte>(val, checked((int)valLen));

        // Release the buffer returned to C++ on the previous call.
        // C++ has already called std::string::assign on it by now.
        nint lastNewValueBuf = Interlocked.Exchange(ref t_lastNewValueBuf, IntPtr.Zero);
        if (lastNewValueBuf != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(lastNewValueBuf);
            self._newValueBufs.TryRemove(lastNewValueBuf, out _);
        }

        FilterDecision decision = self.Filter(level, keySpan, valSpan, out byte[]? newVal);

        if (decision == FilterDecision.ChangeValue && newVal is { Length: > 0 })
        {
            nint buf = Marshal.AllocHGlobal(newVal.Length);
            t_lastNewValueBuf = buf;
            self._newValueBufs.TryAdd(t_lastNewValueBuf, 0);
            Marshal.Copy(newVal, 0, buf, newVal.Length);
            *newValue    = (byte*)buf;
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

    private static nint CB_Name(nint state) => SelfFromState(state)._namePtr;

    private static CompactionFilter SelfFromState(nint state) => (CompactionFilter)GCHandle.FromIntPtr(state).Target!;

    // ── Construction ─────────────────────────────────────────────────────────


    protected unsafe CompactionFilter(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        // Allocate unmanaged memory for the name string
        _namePtr = Marshal.StringToCoTaskMemUTF8(name);

        // Pin this instance so that the C++ callbacks can access it via the state pointer
        _gcHandle = GCHandle.Alloc(this);

        _destructorCb = CB_Destructor;
        _filterCb = CB_Filter;
        _nameCb = CB_Name;

        Handle = NativeMethods.rocksdb_compactionfilter_create(
            GCHandle.ToIntPtr(_gcHandle),
            Marshal.GetFunctionPointerForDelegate(_destructorCb),
            Marshal.GetFunctionPointerForDelegate(_filterCb),
            Marshal.GetFunctionPointerForDelegate(_nameCb));
    }

    // ── Properties ───────────────────────────────────────────────────────────
    /// <summary>
    /// When <c>true</c>, the filter is not invoked for entries that are still
    /// visible under a live snapshot. Defaults to <c>false</c>.
    /// </summary>
    public bool IgnoreSnapshots
    {
        set
        {
            NativeMethods.rocksdb_compactionfilter_set_ignore_snapshots(Handle, value ? (byte)1 : (byte)0);
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

    public override void DisposeUnmanagedResources()
    {
        if (Interlocked.CompareExchange(ref _nativeDestroyed, 1, 0) == 0)
        {
            NativeMethods.rocksdb_compactionfilter_destroy(Handle);
        }

        // Free name
        Marshal.FreeCoTaskMem(_namePtr);

        // Free new var buffers
        foreach (var buf in _newValueBufs.Keys)
        {
            Marshal.FreeHGlobal(buf);
        }
        _newValueBufs.Clear();
    }
}