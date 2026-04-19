using RocksDbNet.Native;
using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// User-defined merge operator that enables read-modify-write semantics
/// on values stored in RocksDB. Override <see cref="FullMerge"/> (and
/// optionally <see cref="PartialMerge"/>) to implement custom merge logic.
/// </summary>
/// <remarks>
/// <para>
/// A merge operator is used with <see cref="RocksDb.Merge(string, string, WriteOptions)"/>
/// and similar overloads to combine new values with existing ones without
/// a separate read step. Common use cases include counters, lists, and
/// append-only logs.
/// </para>
/// <para>
/// Register a merge operator via <see cref="DbOptions.MergeOperator"/> or
/// use <see cref="DbOptions.SetUInt64AddMergeOperator"/> for the built-in
/// 64-bit addition operator.
/// </para>
/// </remarks>
public abstract class MergeOperator : RocksDbHandle
{
    // ── Unmanaged delegate types ─────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestructorDelegate(nint state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate nint FullMergeDelegate(
        nint state,                                 // User-defined state
        byte* key, nuint keyLen,                    // The key being operated on
        byte* existingVal, nuint existingValLen,    // The current value (can be IntPtr.Zero)
        nint operands,                              // Pointer to an array of const char*
        nint operandsLen,                           // Pointer to an array of size_t
        int numOperands,                            // Number of operands in the arrays
        byte* success,                              // Set to 1 for success, 0 for failure
        nuint* newValLen);                          // Set to the length of the returned buffer

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate nint PartialMergeDelegate(
        nint state,                                 // User-defined state
        byte* key, nuint keyLen,                    // The key being operated on
        nint operands,                              // Pointer to an array of const char*
        nint operandsLen,                           // Pointer to an array of size_t
        int numOperands,                            // Number of operands in the arrays
        byte* success,                              // Set to 1 for success, 0 for failure
        nuint* newValLen);                          // Set to the length of the returned buffer

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void DeleteValueDelegate(
        nint state,
        nint value, nuint valueLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NameDelegate(nint state);

    // ── Instance state ───────────────────────────────────────────────────────
    private readonly nint _namePtr;   // CoTaskMem UTF-8 name string
    private GCHandle _gcHandle;       // strong root → object stays alive while native holds it
    
    // Delegate instances kept as fields to prevent GC from collecting the
    // objects while the native side still holds function pointers into them.
    private readonly DestructorDelegate _destructorDelegate;
    private readonly FullMergeDelegate _fullMergeDelegate;
    private readonly PartialMergeDelegate _partialMergeDelegate;
    private readonly DeleteValueDelegate _deleteValueDelegate;
    private readonly NameDelegate _nameDelegate;

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    private static void DestructorCallback(nint state)
    {
        var handle = GCHandle.FromIntPtr(state);
        var self = (MergeOperator)handle.Target!;
        self.TransferOwnership();
        handle.Free();
    }

    private static unsafe nint FullMergeCallback(
        nint state,
        byte* key, nuint keyLen,
        byte* existingVal, nuint existingValLen,
        nint operands,
        nint operandsLen,
        int numOperands,
        byte* success,
        nuint* newValLen
        )
    {
        var self = SelfFromState(state);
        var keySpan = new ReadOnlySpan<byte>(key, checked((int)keyLen));
        var operandsList = self.CreateOperands(operands, operandsLen, numOperands);
        bool hasExistingValue = existingVal != null;
        var existingValueSpan = hasExistingValue ? new ReadOnlySpan<byte>(existingVal, checked((int)existingValLen)) : default;

        if (!self.FullMerge(keySpan, hasExistingValue, existingValueSpan, operandsList, out byte[] newVal))
        {
            // If no success, return a null pointer and set newValLen to 0
            // This indicates to RocksDB that the merge operation failed, and in that case RocksDB will not use the returned value,
            // and the delete_value callback will not be called.
            *newValLen = 0;
            *success = 0;
            return nint.Zero;
        }

        nint buf = Marshal.AllocHGlobal(newVal.Length);
        Marshal.Copy(newVal, 0, buf, newVal.Length);

        *newValLen = (nuint)newVal.Length;
        *success = 1;

        return buf;
    }

    private static unsafe nint PartialMergeCallback(
        nint state,
        byte* key, nuint keyLen,
        nint operands,
        nint operandsLen,
        int numOperands,
        byte* success,
        nuint* newValLen)
    {
        var self = SelfFromState(state);
        var keySpan = new ReadOnlySpan<byte>(key, checked((int)keyLen));
        var operandsList = self.CreateOperands(operands, operandsLen, numOperands);

        if (!self.PartialMerge(keySpan, operandsList, out byte[] newVal))
        {
            // If no success, return a null pointer and set newValLen to 0
            // This indicates to RocksDB that the merge operation failed, and in that case RocksDB will not use the returned value,
            // and the delete_value callback will not be called.

            *newValLen = 0;
            *success = (byte)0;
            return nint.Zero;
        }

        nint buf = Marshal.AllocHGlobal(newVal.Length);
        Marshal.Copy(newVal, 0, buf, newVal.Length);

        *newValLen = (nuint)newVal.Length;
        *success = (byte)1;

        return buf;
    }

    private static void DeleteValueCallback(
        nint state,
        nint value, nuint valueLen)
    {
        Marshal.FreeHGlobal(value);
    }

    private static nint NameCallback(nint state) => SelfFromState(state)._namePtr;

    private static MergeOperator SelfFromState(nint state) => (MergeOperator)GCHandle.FromIntPtr(state).Target!;

    // ── Construction ─────────────────────────────────────────────────────────

    protected unsafe MergeOperator(string name, bool enablePartialMerge = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        // Allocate unmanaged memory for the name string
        _namePtr = Marshal.StringToCoTaskMemUTF8(name);

        // Pin this instance so that the C++ callbacks can access it via the state pointer
        _gcHandle = GCHandle.Alloc(this);

        _destructorDelegate = DestructorCallback;
        _fullMergeDelegate = FullMergeCallback;
        _partialMergeDelegate = PartialMergeCallback;
        _deleteValueDelegate = DeleteValueCallback;
        _nameDelegate = NameCallback;

        Handle = NativeMethods.rocksdb_mergeoperator_create(
            GCHandle.ToIntPtr(_gcHandle),
            Marshal.GetFunctionPointerForDelegate(_destructorDelegate),
            Marshal.GetFunctionPointerForDelegate(_fullMergeDelegate),
            enablePartialMerge ? Marshal.GetFunctionPointerForDelegate(_partialMergeDelegate) : IntPtr.Zero,
            Marshal.GetFunctionPointerForDelegate(_deleteValueDelegate),
            Marshal.GetFunctionPointerForDelegate(_nameDelegate));
    }

    private IEnumerable<byte[]> CreateOperands(nint operands, nint operandsLen, int numOperands)
    {
        var result = new List<byte[]>(numOperands);

        for (int i = 0; i < numOperands; i++)
        {
            // Get the pointer to the operand
            nint operandPtr = Marshal.ReadIntPtr(operands, i * nint.Size);

            // Get the length of the operand
            long operandLen = Marshal.ReadInt64(operandsLen, i * sizeof(long));

            // Copy the operand data into a managed byte array
            byte[] operandData = new byte[operandLen];
            Marshal.Copy(operandPtr, operandData, 0, (int)operandLen);

            yield return operandData;
        }
    }

    // ── Abstract methods ───────────────────────────────────────────────

    /// <summary>
    /// Called to merge all accumulated operands with the existing value for a key.
    /// </summary>
    /// <param name="key">The key being merged.</param>
    /// <param name="hasExistingValue"><c>true</c> if the key has a pre-existing value.</param>
    /// <param name="existingValue">The current value (valid only when <paramref name="hasExistingValue"/> is <c>true</c>).</param>
    /// <param name="operands">The operands to merge, in chronological order.</param>
    /// <param name="newValue">Output: the result of the merge.</param>
    /// <returns><c>true</c> if the merge succeeded; <c>false</c> to signal failure.</returns>
    public abstract bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue, IEnumerable<byte[]> operands, out byte[] newValue);

    /// <summary>
    /// Optional partial merge: combines a subset of operands before a full
    /// merge. Return <c>false</c> to fall back to <see cref="FullMerge"/>.
    /// </summary>
    public virtual bool PartialMerge(ReadOnlySpan<byte> key, IEnumerable<byte[]> operands, out byte[] newValue)
    {
        newValue = Array.Empty<byte>();
        return false;
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_mergeoperator_destroy(Handle);
    }

    public override void DisposeUnmanagedResources()
    {
        // Free name
        Marshal.FreeCoTaskMem(_namePtr);
    }
}