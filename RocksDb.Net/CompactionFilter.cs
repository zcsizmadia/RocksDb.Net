using System.Buffers;
using System.Runtime.CompilerServices;
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
    /// <c>Filter</c>.
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
/// User-defined compaction filter. Override <c>Filter</c> to inspect
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
/// multi-threaded compaction is active, <c>Filter</c> may be called
/// from multiple threads concurrently. Either make your override thread-safe
/// or use <see cref="CompactionFilterFactory"/> to create a separate instance
/// per compaction job.
/// </para>
/// </remarks>
public abstract class CompactionFilter : RocksDbHandle
{
    // ── Unmanaged delegate types ─────────────────────────────────────────────
    // Native entry points, not delegates: RocksDb receives the address of the
    // method rather than of a runtime-generated marshalling thunk. The fields
    // that used to hold the delegates alive are gone with them; what keeps this
    // object reachable is the GCHandle from PinGarbageCollector.

    // A replacement value is written into this thread's CallbackScratch
    // buffer. rocksdb_compactionfilter_t::Filter() copies *new_value with
    // std::string::assign straight after the callback returns, and has no
    // matching free(), so the buffer only has to last until the next callback
    // on the thread. It used to be a fresh AllocHGlobal per changed value,
    // tracked per thread in two ConcurrentDictionary instances that every key
    // touched, changed or not.

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CB_Destructor(nint state)
    {
        try
        {
            var self = GetSelfFromPinnedIntPtr<CompactionFilter>(state);
            self.TransferOwnership();
            self.UnpinGarbageCollector();
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report("CompactionFilter destructor", ex, state);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
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

            CallbackScratch replacement = CallbackScratch.Acquire();
            try
            {
                FilterDecision decision = self.Filter(level, keySpan, valSpan, replacement);

                if (decision == FilterDecision.ChangeValue)
                {
                    // Never a null pointer, even for an empty replacement:
                    // RocksDb does a std::string::assign of the reported
                    // length, and while a zero count would not dereference the
                    // pointer, handing over a real allocation avoids depending
                    // on that.
                    *newValue = replacement.Pointer;
                    *newValueLen = (nuint)replacement.WrittenCount;
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
            finally
            {
                CallbackScratch.Release();
            }
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

        Handle = NativeMethods.rocksdb_compactionfilter_create(
            GetPinnedIntPtr(),
            (nint)(delegate* unmanaged[Cdecl]<nint, void>)&CB_Destructor,
            (nint)(delegate* unmanaged[Cdecl]<
                nint, int, byte*, nuint, byte*, nuint, byte**, nuint*, byte*, byte>)&CB_Filter,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint>)&GetNameFromPinnedIntPtrSafe);
    }

    // ── Filter methods: override one form ────────────────────────────────────
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
    /// <exception cref="NotSupportedException">
    /// Neither form of <c>Filter</c> is overridden. Reported through
    /// <see cref="RocksDbCallbacks.UnhandledException"/>, and the entry is kept.
    /// </exception>
    protected virtual FilterDecision Filter(
        int level,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> existingValue,
        out byte[]? newValue)
        => throw new NotSupportedException(
            $"{GetType().Name} overrides neither form of Filter. Override one of them.");

    /// <summary>
    /// Called for each key-value pair during table-file creation, writing any
    /// replacement value into a buffer rather than returning an array.
    /// </summary>
    /// <param name="level">The SST level of the file being created.</param>
    /// <param name="key">The key. Valid only for the duration of this call.</param>
    /// <param name="existingValue">The current value. Valid only for the duration of this call.</param>
    /// <param name="newValue">
    /// Where to write the replacement when returning
    /// <see cref="FilterDecision.ChangeValue"/>; nothing written means an
    /// empty value. Ignored for other decisions. Valid only for the duration
    /// of this call: do not keep it.
    /// </param>
    /// <returns>The decision for this entry.</returns>
    /// <remarks>
    /// <para>
    /// The default calls the array form and copies its replacement in, so a
    /// filter written against that form behaves as it always has. Override
    /// this one instead to replace values without allocating; override one
    /// form, not both.
    /// </para>
    /// <para>
    /// For the array form, <see cref="FilterDecision.ChangeValue"/> with a
    /// null replacement keeps the entry unchanged, as it always did.
    /// </para>
    /// </remarks>
    protected virtual FilterDecision Filter(
        int level,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> existingValue,
        IBufferWriter<byte> newValue)
    {
        ArgumentNullException.ThrowIfNull(newValue);

        FilterDecision decision = Filter(level, key, existingValue, out byte[]? replacement);

        if (decision != FilterDecision.ChangeValue)
        {
            return decision;
        }

        // `is not null`, not `is { Length: > 0 }`. Requiring a positive length
        // once meant that replacing a value with an empty one was silently
        // ignored and the old value kept, even though RocksDb accepts an empty
        // replacement.
        if (replacement is null)
        {
            return FilterDecision.Keep;
        }

        newValue.Write(replacement);
        return FilterDecision.ChangeValue;
    }

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

}
