using RocksDbNet.Native;
using System.Runtime.InteropServices;

namespace RocksDbNet;

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
    
    // Delegate instances kept as fields to prevent GC from collecting the
    // objects while the native side still holds function pointers into them.
    private readonly DestructorDelegate _destructorDelegate;
    private readonly CompareDelegate _compareDelegate;
    private readonly NameDelegate _nameDelegate;

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    private static void DestructorCallback(nint state)
    {
        // Unpin self
        GCHandle.FromIntPtr(state).Free();
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

    public abstract int Compare(ReadOnlySpan<byte> keyA, ReadOnlySpan<byte> keyB);

    public override void DisposeUnmanagedResources()
    {
        NativeMethods.rocksdb_mergeoperator_destroy(Handle);

        // Free name
        Marshal.FreeCoTaskMem(_namePtr);
    }
}