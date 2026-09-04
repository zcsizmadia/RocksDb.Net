using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace RocksDbNet;

/// <summary>
/// User-defined merge operator that enables read-modify-write semantics
/// on values stored in RocksDb. Override <see cref="FullMerge"/> (and
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
        // Native entry points, not delegates. See Comparator for why.

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DestructorCallback(nint state)
    {
        try
        {
            var self = GetSelfFromPinnedIntPtr<MergeOperator>(state);
            self.TransferOwnership();
            self.UnpinGarbageCollector();
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report("MergeOperator destructor", ex, state);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
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
        try
        {
            var self = SelfFromState(state);
            var keySpan = new ReadOnlySpan<byte>(key, checked((int)keyLen));
            var operandsList = CreateOperands(operands, operandsLen, numOperands);
            bool hasExistingValue = existingVal != null;
            var existingValueSpan = hasExistingValue ? new ReadOnlySpan<byte>(existingVal, checked((int)existingValLen)) : default;

            if (!self.FullMerge(keySpan, hasExistingValue, existingValueSpan, operandsList, out byte[]? newVal)
                || newVal is null)
            {
                // If no success, return a null pointer and set newValLen to 0
                // This indicates to RocksDb that the merge operation failed, and in that case RocksDb will not use the returned value,
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
        catch (Exception ex)
        {
            // Unlike a comparator, a merge operator has a real failure channel:
            // success = 0 tells RocksDb the merge failed, which surfaces as a
            // corruption error on the read or compaction that triggered it. That
            // is a truthful outcome, so report and fail the merge rather than
            // inventing a merged value.
            RocksDbCallbacks.Report(nameof(FullMerge), ex, state);

            *newValLen = 0;
            *success = 0;
            return nint.Zero;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe nint PartialMergeCallback(
        nint state,
        byte* key, nuint keyLen,
        nint operands,
        nint operandsLen,
        int numOperands,
        byte* success,
        nuint* newValLen)
    {
        try
        {
            var self = SelfFromState(state);
            var keySpan = new ReadOnlySpan<byte>(key, checked((int)keyLen));
            var operandsList = CreateOperands(operands, operandsLen, numOperands);

            if (!self.PartialMerge(keySpan, operandsList, out byte[]? newVal) || newVal is null)
            {
                // If no success, return a null pointer and set newValLen to 0
                // This indicates to RocksDb that the merge operation failed, and in that case RocksDb will not use the returned value,
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
        catch (Exception ex)
        {
            // A failed partial merge is not an error: RocksDb falls back to
            // keeping the operands and merging them later via FullMerge.
            RocksDbCallbacks.Report(nameof(PartialMerge), ex, state);

            *newValLen = 0;
            *success = (byte)0;
            return nint.Zero;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DeleteValueCallback(
        nint state,
        nint value, nuint valueLen)
    {
        try
        {
            Marshal.FreeHGlobal(value);
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report("DeleteValue", ex, state);
        }
    }

    private static MergeOperator SelfFromState(nint state) => GetSelfFromPinnedIntPtr<MergeOperator>(state);

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>Creates a merge operator with the given name.</summary>
    /// <param name="name">
    /// Identifies this operator in RocksDb's logs and options output. Unlike
    /// a comparator name it is not enforced on reopen, so a mismatch will not
    /// be caught for you: opening a database with a different merge operator
    /// than the one that wrote its operands silently produces wrong merges.
    /// </param>
    protected unsafe MergeOperator(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        PinGarbageCollector(name);

        // The partial-merge slot is always installed, even when the subclass does
        // not override PartialMerge. RocksDb invokes it through
        // `(*partial_merge_)(...)` with no null check, unlike the delete-value
        // slot beside it, and it reaches that call on any flush or non-bottommost
        // compaction that collapses two or more operands for one key. Leaving the
        // slot null therefore terminated the process. The base PartialMerge
        // returns false, which is the correct answer for an operator that cannot
        // combine operands: RocksDb keeps them and calls FullMerge later.
        Handle = NativeMethods.rocksdb_mergeoperator_create(
            GetPinnedIntPtr(),
            (nint)(delegate* unmanaged[Cdecl]<nint, void>)&DestructorCallback,
            (nint)(delegate* unmanaged[Cdecl]<
                nint, byte*, nuint, byte*, nuint, nint, nint, int, byte*, nuint*,
                nint>)&FullMergeCallback,
            (nint)(delegate* unmanaged[Cdecl]<
                nint, byte*, nuint, nint, nint, int, byte*, nuint*, nint>)&PartialMergeCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nuint, void>)&DeleteValueCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint>)&GetNameFromPinnedIntPtrSafe);
    }

    /// <summary>
    /// Copies the operands out of the native arrays.
    /// </summary>
    /// <remarks>
    /// Materialised rather than yielded. RocksDb builds these arrays as
    /// call-scoped locals, so a lazy sequence that an override stored and
    /// enumerated later read freed memory. Making it eager costs one array
    /// allocation and nothing else: each operand was already copied into a
    /// managed array here, so the same bytes move either way, and an operator
    /// that reads all of its operands, which is nearly all of them, pays the
    /// same as before.
    /// </remarks>
    private static IReadOnlyList<byte[]> CreateOperands(nint operands, nint operandsLen, int numOperands)
    {
        var result = new byte[numOperands][];

        for (int i = 0; i < numOperands; i++)
        {
            // Get the pointer to the operand
            nint operandPtr = Marshal.ReadIntPtr(operands, i * nint.Size);

            // The lengths are a `const size_t*`, so the element width is the
            // pointer width, not 8. Reading them as Int64 put every index after
            // the first at the wrong offset on 32-bit, which win-x86 is, fusing
            // pairs of lengths and reading past the end of the array.
            nuint operandLen = (nuint)Marshal.ReadIntPtr(operandsLen, i * nint.Size);

            // Copy the operand data into a managed byte array
            byte[] operandData = new byte[operandLen];
            Marshal.Copy(operandPtr, operandData, 0, checked((int)operandLen));

            result[i] = operandData;
        }

        return result;
    }

    // ── Abstract methods ───────────────────────────────────────────────

    /// <summary>
    /// Called to merge all accumulated operands with the existing value for a key.
    /// </summary>
    /// <param name="key">The key being merged.</param>
    /// <param name="hasExistingValue"><c>true</c> if the key has a pre-existing value.</param>
    /// <param name="existingValue">The current value (valid only when <paramref name="hasExistingValue"/> is <c>true</c>).</param>
    /// <param name="operands">
    /// The operands to merge, in chronological order. Managed copies, so they
    /// may be kept beyond the call.
    /// </param>
    /// <param name="newValue">Output: the result of the merge.</param>
    /// <returns><c>true</c> if the merge succeeded; <c>false</c> to signal failure.</returns>
    public abstract bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue, IReadOnlyList<byte[]> operands, out byte[]? newValue);

    /// <summary>
    /// Optional partial merge: combines a subset of operands before a full
    /// merge. Return <c>false</c> to fall back to <see cref="FullMerge"/>.
    /// </summary>
    /// <param name="key">The key being merged.</param>
    /// <param name="operands">
    /// The operands to combine, in chronological order. Managed copies, so they
    /// may be kept beyond the call.
    /// </param>
    /// <param name="newValue">Output: the combined operand.</param>
    /// <returns>
    /// <see langword="true"/> if the operands were combined;
    /// <see langword="false"/> to leave it to <see cref="FullMerge"/>.
    /// </returns>
    public virtual bool PartialMerge(
        ReadOnlySpan<byte> key, IReadOnlyList<byte[]> operands, out byte[]? newValue)
    {
        // null rather than an empty array. There is no value to give when the
        // answer is "leave it to FullMerge", and the empty array was only ever
        // there to satisfy a non-nullable out parameter.
        newValue = null;
        return false;
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_mergeoperator_destroy(Handle);
    }
}