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
    // ── Unmanaged delegate types ─────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestructorDelegate(nint state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate int CompareDelegate(
        nint state,
        byte* keyA, nuint keyALen,
        byte* keyB, nuint keyBLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NameDelegate(nint state);

    // Delegate instances kept as fields to prevent GC from collecting the
    // objects while the native side still holds function pointers into them.
    private readonly DestructorDelegate _destructorDelegate;
    private readonly CompareDelegate _compareDelegate;
    private readonly NameDelegate _nameDelegate;

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    private static void DestructorCallback(nint state)
    {
        var self = GetSelfFromPinnedIntPtr<Comparator>(state);
        self.UnpinGarbageCollector();
    }

    private static unsafe int CompareCallback(
        nint state,
        byte* keyA, nuint keyALen,
        byte* keyB, nuint keyBLen)
    {
        var self = GetSelfFromPinnedIntPtr<Comparator>(state);
        var keyASpan = new ReadOnlySpan<byte>(keyA, checked((int)keyALen));
        var keyBSpan = new ReadOnlySpan<byte>(keyB, checked((int)keyBLen));
        return self.Compare(keyASpan, keyBSpan);
    }

    //private static nint NameCallback(nint state) => GetNameFromPinnedIntPtr(state);

    // ── Construction ─────────────────────────────────────────────────────────

    protected unsafe Comparator(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        PinGarbageCollector(name);

        _destructorDelegate = DestructorCallback;
        _compareDelegate = CompareCallback;
        _nameDelegate = GetNameFromPinnedIntPtr; // NameCallback;

        Handle = NativeMethods.rocksdb_comparator_create(
            GetPinnedIntPtr(),
            Marshal.GetFunctionPointerForDelegate(_destructorDelegate),
            Marshal.GetFunctionPointerForDelegate(_compareDelegate),
            Marshal.GetFunctionPointerForDelegate(_nameDelegate));
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

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_comparator_destroy(Handle);
    }
}