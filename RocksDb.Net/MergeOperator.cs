using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace RocksDbNet;

/// <summary>
/// User-defined merge operator that enables read-modify-write semantics
/// on values stored in RocksDb. Override <c>FullMerge</c> (and
/// optionally <c>PartialMerge</c>) to implement custom merge logic, in either
/// the array form or the span form, which reads the operands in place through
/// <see cref="MergeOperands"/> and merges without allocating.
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
            bool hasExistingValue = existingVal != null;
            var existingValueSpan = hasExistingValue ? new ReadOnlySpan<byte>(existingVal, checked((int)existingValLen)) : default;

            // The result goes into this thread's scratch buffer, which RocksDb
            // copies before anything else runs here; see CallbackScratch.
            CallbackScratch result = CallbackScratch.Acquire();
            try
            {
                if (!self.FullMerge(keySpan, hasExistingValue, existingValueSpan,
                        new MergeOperands(operands, operandsLen, numOperands), result))
                {
                    // Failure: a null pointer, zero length and success = 0.
                    // RocksDb still assigns the (empty) result and calls
                    // delete_value on it unconditionally, failure or not.
                    *newValLen = 0;
                    *success = 0;
                    return nint.Zero;
                }

                *newValLen = (nuint)result.WrittenCount;
                *success = 1;
                return (nint)result.Pointer;
            }
            finally
            {
                CallbackScratch.Release();
            }
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

            CallbackScratch result = CallbackScratch.Acquire();
            try
            {
                if (!self.PartialMerge(keySpan, new MergeOperands(operands, operandsLen, numOperands), result))
                {
                    // Failure, as in FullMerge.
                    *newValLen = 0;
                    *success = (byte)0;
                    return nint.Zero;
                }

                *newValLen = (nuint)result.WrittenCount;
                *success = (byte)1;
                return (nint)result.Pointer;
            }
            finally
            {
                CallbackScratch.Release();
            }
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
        // Nothing to free. The value is this thread's scratch buffer, which is
        // reused by the next merge rather than released after each one. The
        // slot is still installed, because RocksDb calls free() on the value
        // instead when it is null, and free() on scratch memory would be a
        // double free when the buffer is next reused.
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

    // ── Merge methods ───────────────────────────────────────────────────────
    //
    // Two forms of each. The span forms read the operands in place and write
    // the result into a buffer RocksDb copies from, so a merge allocates
    // nothing; override those where merges are hot. The array forms are the
    // original API: each operand is copied into a managed array, which may be
    // kept beyond the call, and the result is returned as one. By default each
    // span form calls the array form, so an operator written against the
    // array forms behaves exactly as it always has. Override one form of
    // FullMerge, not both.

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
    /// <exception cref="NotSupportedException">
    /// Neither form of <c>FullMerge</c> is overridden. Reported through
    /// <see cref="RocksDbCallbacks.UnhandledException"/>, and the merge fails.
    /// </exception>
    public virtual bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue, IReadOnlyList<byte[]> operands, out byte[]? newValue)
        => throw new NotSupportedException(
            $"{GetType().Name} overrides neither form of FullMerge. Override one of them.");

    /// <summary>
    /// Merges all accumulated operands with the existing value for a key,
    /// without copying either.
    /// </summary>
    /// <param name="key">The key being merged. Valid for the duration of the call.</param>
    /// <param name="hasExistingValue"><c>true</c> if the key has a pre-existing value.</param>
    /// <param name="existingValue">The current value, valid only when <paramref name="hasExistingValue"/> is <c>true</c>.</param>
    /// <param name="operands">The operands, oldest first, read in place. See <see cref="MergeOperands"/>.</param>
    /// <param name="newValue">
    /// Where to write the result. Valid only for the duration of the call: do
    /// not keep it.
    /// </param>
    /// <returns><c>true</c> if the merge succeeded; <c>false</c> to signal failure.</returns>
    /// <remarks>
    /// The default copies the operands into arrays and calls the array form,
    /// then writes its result. Override this instead to merge without
    /// allocating.
    /// </remarks>
    public virtual bool FullMerge(
        ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue,
        MergeOperands operands, IBufferWriter<byte> newValue)
    {
        ArgumentNullException.ThrowIfNull(newValue);

        if (!FullMerge(key, hasExistingValue, existingValue, operands.ToArrays(), out byte[]? result) || result is null)
        {
            return false;
        }

        newValue.Write(result);
        return true;
    }

    /// <summary>
    /// Optional partial merge: combines a subset of operands before a full
    /// merge. Return <c>false</c> to fall back to <c>FullMerge</c>.
    /// </summary>
    /// <param name="key">The key being merged.</param>
    /// <param name="operands">
    /// The operands to combine, in chronological order. Managed copies, so they
    /// may be kept beyond the call.
    /// </param>
    /// <param name="newValue">Output: the combined operand.</param>
    /// <returns>
    /// <see langword="true"/> if the operands were combined;
    /// <see langword="false"/> to leave it to <c>FullMerge</c>.
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

    /// <summary>
    /// Optional partial merge, without copying the operands or the result.
    /// </summary>
    /// <param name="key">The key being merged. Valid for the duration of the call.</param>
    /// <param name="operands">The operands to combine, oldest first, read in place.</param>
    /// <param name="newValue">Where to write the combined operand. Valid only for the duration of the call.</param>
    /// <returns>
    /// <see langword="true"/> if the operands were combined;
    /// <see langword="false"/> to leave it to <c>FullMerge</c>.
    /// </returns>
    /// <remarks>The default calls the array form, as for <c>FullMerge</c>.</remarks>
    public virtual bool PartialMerge(ReadOnlySpan<byte> key, MergeOperands operands, IBufferWriter<byte> newValue)
    {
        ArgumentNullException.ThrowIfNull(newValue);

        if (!PartialMerge(key, operands.ToArrays(), out byte[]? result) || result is null)
        {
            return false;
        }

        newValue.Write(result);
        return true;
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_mergeoperator_destroy(Handle);
    }
}