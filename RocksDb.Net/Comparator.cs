using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// User-defined comparator for controlling the sort order of keys in
/// a RocksDb database. Override <see cref="Compare"/> to define custom
/// key ordering.
/// </summary>
/// <remarks>
/// <para>
/// Every database uses a comparator to determine the ordering of keys.
/// The default comparator uses bytewise (lexicographic) ordering. To use
/// a custom comparator, create a subclass and pass it to
/// <see cref="DbOptions.Comparator"/>.
/// </para>
/// <para>
/// <b>Important:</b> Once a database has been created with a given
/// comparator, every subsequent open must use a comparator with the
/// same name and semantics.
/// </para>
/// </remarks>
public abstract class Comparator : RocksDbHandle
{
    // ── Native entry points ──────────────────────────────────────────────────
    //
    // [UnmanagedCallersOnly] rather than delegates, so what RocksDb receives is
    // the address of the method. A function pointer taken from a delegate is
    // the address of a runtime-generated marshalling thunk that dispatches
    // through a delegate object, which for a blittable signature does little
    // but is paid on every call — and Compare is on the innermost read and
    // compaction loop.
    //
    // These methods cannot be called from managed code, and nothing does. The
    // delegate fields that used to exist only to keep the delegates from being
    // collected are gone with them; what keeps this object alive is the
    // GCHandle from PinGarbageCollector, which is unchanged and is also what
    // makes an UnmanagedCallersOnly entry point workable here, since such a
    // method cannot close over an instance.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DestructorCallback(nint state)
    {
        try
        {
            var self = GetSelfFromPinnedIntPtr<Comparator>(state);
            self.UnpinGarbageCollector();
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report("Comparator destructor", ex, state);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int CompareCallback(
        nint state,
        byte* keyA, nuint keyALen,
        byte* keyB, nuint keyBLen)
    {
        try
        {
            var self = GetSelfFromPinnedIntPtr<Comparator>(state);
            var keyASpan = new ReadOnlySpan<byte>(keyA, checked((int)keyALen));
            var keyBSpan = new ReadOnlySpan<byte>(keyB, checked((int)keyBLen));
            return self.Compare(keyASpan, keyBSpan);
        }
        catch (Exception ex)
        {
            // Compare has no failure channel: it must return an ordering. Any
            // value we invent is a lie about key order, and RocksDb would write
            // and later read data against it, so there is no safe fallback.
            // Terminate with a diagnosable message instead.
            RocksDbCallbacks.ReportFatal(nameof(Compare), ex, state);
            throw; // Unreachable: ReportFatal does not return.
        }
    }

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>Creates a comparator with the given name.</summary>
    /// <param name="name">
    /// Identifies the ordering this comparator implements. RocksDb records it
    /// in the database and refuses to reopen with a comparator whose name
    /// differs, which is what stops data being read back in an order it was
    /// not written in. Change the name only when the ordering itself changes,
    /// and never reuse a name for different semantics.
    /// </param>
    protected unsafe Comparator(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        PinGarbageCollector(name);

        Handle = NativeMethods.rocksdb_comparator_create(
            GetPinnedIntPtr(),
            (nint)(delegate* unmanaged[Cdecl]<nint, void>)&DestructorCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, byte*, nuint, byte*, nuint, int>)&CompareCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint>)&GetNameFromPinnedIntPtrSafe);
    }

    // ── Abstract methods ───────────────────────────────────────────────

    /// <summary>
    /// Compares two keys and returns their relative ordering.
    /// </summary>
    /// <param name="keyA">The first key.</param>
    /// <param name="keyB">The second key.</param>
    /// <returns>
    /// A negative value if <paramref name="keyA"/> is less than <paramref name="keyB"/>,
    /// zero if they are equal, or a positive value if <paramref name="keyA"/> is
    /// greater than <paramref name="keyB"/>.
    /// </returns>
    public abstract int Compare(ReadOnlySpan<byte> keyA, ReadOnlySpan<byte> keyB);

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_comparator_destroy(Handle);
    }
}