using RocksDbNet.Native;
using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// User-defined comparator for controlling the sort order of keys in
/// a RocksDB database. Override <see cref="Compare"/> to define custom
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

    // ── Instance state ───────────────────────────────────────────────────────
    private readonly nint _namePtr;   // CoTaskMem UTF-8 name string
    private GCHandle _gcHandle;       // strong root → object stays alive while native holds it
    private int _nativeDestroyed;     // 1 when the native destructor has already fired
    
    // Delegate instances kept as fields to prevent GC from collecting the
    // objects while the native side still holds function pointers into them.
    private readonly DestructorDelegate _destructorDelegate;
    private readonly CompareDelegate _compareDelegate;
    private readonly NameDelegate _nameDelegate;

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    private static void DestructorCallback(nint state)
    {
        var handle = GCHandle.FromIntPtr(state);
        var self = (Comparator)handle.Target!;
        Interlocked.Exchange(ref self._nativeDestroyed, 1);
        handle.Free();
    }

    private static unsafe int CompareCallback(
        nint state,
        byte* keyA, nuint keyALen,
        byte* keyB, nuint keyBLen)
    {
        var self = SelfFromState(state);
        var keyASpan = new ReadOnlySpan<byte>(keyA, checked((int)keyALen));
        var keyBSpan = new ReadOnlySpan<byte>(keyB, checked((int)keyBLen));
        return self.Compare(keyASpan, keyBSpan);
    }

    private static nint NameCallback(nint state) => SelfFromState(state)._namePtr;

    private static Comparator SelfFromState(nint state) => (Comparator)GCHandle.FromIntPtr(state).Target!;

    // ── Construction ─────────────────────────────────────────────────────────

    protected unsafe Comparator(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        // Allocate unmanaged memory for the name string
        _namePtr = Marshal.StringToCoTaskMemUTF8(name);

        // Pin this instance so that the C++ callbacks can access it via the state pointer
        _gcHandle = GCHandle.Alloc(this);

        _destructorDelegate = DestructorCallback;
        _compareDelegate = CompareCallback;
        _nameDelegate = NameCallback;

        Handle = NativeMethods.rocksdb_comparator_create(
            GCHandle.ToIntPtr(_gcHandle),
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

    public override void DisposeUnmanagedResources()
    {
        if (Interlocked.CompareExchange(ref _nativeDestroyed, 1, 0) == 0)
        {
            NativeMethods.rocksdb_comparator_destroy(Handle);
        }

        // Free name
        Marshal.FreeCoTaskMem(_namePtr);
    }
}