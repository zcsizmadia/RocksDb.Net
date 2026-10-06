using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// The keys of a batched read, and the slots RocksDb writes its results into,
/// in one native allocation.
/// </summary>
/// <remarks>
/// <para>
/// The batched reads used to pin every key with its own
/// <see cref="GCHandle"/> and allocate five managed arrays for the pointers,
/// lengths, results and errors. A pinned handle goes through the runtime's
/// handle table on the way in and out, so a batch paid that per key before it
/// reached native code, and left that many pinned objects scattered through
/// the young generation while it ran.
/// </para>
/// <para>
/// Here the keys are copied end to end into a block that also holds the five
/// per-key slots, so a batch costs one allocation and one copy of its keys,
/// and nothing needs pinning because nothing is on the managed heap. The block
/// is zeroed, so a slot RocksDb leaves untouched reads as "no value" and "no
/// error".
/// </para>
/// </remarks>
internal readonly unsafe struct NativeKeyBatch : IDisposable
{
    private readonly void* _block;

    /// <summary>The number of keys.</summary>
    public readonly int Count;

    /// <summary>A pointer to each key.</summary>
    public readonly byte** Keys;

    /// <summary>The length of each key.</summary>
    public readonly nuint* KeySizes;

    /// <summary>Where RocksDb writes each value pointer, or each pinned slice.</summary>
    public readonly nint* Values;

    /// <summary>Where RocksDb writes each value's length.</summary>
    public readonly nuint* ValueSizes;

    /// <summary>Where RocksDb writes each key's error message, if any.</summary>
    public readonly nint* Errors;

    /// <exception cref="ArgumentNullException">A key is null.</exception>
    public NativeKeyBatch(IReadOnlyList<byte[]> keys)
    {
        int n = keys.Count;
        long keyBytes = 0;

        for (int i = 0; i < n; i++)
        {
            ArgumentNullException.ThrowIfNull(keys[i]);
            keyBytes += keys[i].Length;
        }

        const int SlotsPerKey = 5;
        nuint slotBytes = checked((nuint)n * SlotsPerKey * (nuint)sizeof(nint));
        nuint total = checked(slotBytes + (nuint)keyBytes);

        _block = NativeMemory.AllocZeroed(total);
        Count = n;

        nint* slots = (nint*)_block;
        Keys = (byte**)slots;
        KeySizes = (nuint*)(slots + n);
        Values = slots + 2 * n;
        ValueSizes = (nuint*)(slots + 3 * n);
        Errors = slots + 4 * n;

        byte* data = (byte*)_block + slotBytes;

        for (int i = 0; i < n; i++)
        {
            byte[] key = keys[i];

            key.CopyTo(new Span<byte>(data, key.Length));
            Keys[i] = data;
            KeySizes[i] = (nuint)key.Length;

            data += key.Length;
        }
    }

    public void Dispose() => NativeMemory.Free(_block);
}
