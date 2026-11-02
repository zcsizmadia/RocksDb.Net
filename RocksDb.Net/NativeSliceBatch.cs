using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// The keys of a batched read as an array of <c>rocksdb_slice_t</c>, in one
/// native allocation.
/// </summary>
/// <remarks>
/// The sibling of <see cref="NativeKeyBatch"/> for the C functions that take
/// keys as slices rather than as parallel pointer and length arrays, which is
/// <c>rocksdb_batched_multi_get_pinned_cf</c>. Those functions return their
/// results in an object of their own, so there are no per-key result slots
/// here, only the keys. The same reasoning applies: one allocation and one copy
/// of the keys, and nothing on the managed heap to pin.
/// </remarks>
internal readonly unsafe struct NativeSliceBatch : IDisposable
{
    private readonly void* _block;

    /// <summary>The number of keys.</summary>
    public readonly int Count;

    /// <summary>One slice per key, pointing into the same block.</summary>
    public readonly rocksdb_slice_t* Slices;

    /// <exception cref="ArgumentNullException">A key is null.</exception>
    public NativeSliceBatch(IReadOnlyList<byte[]> keys)
    {
        int n = keys.Count;
        long keyBytes = 0;

        for (int i = 0; i < n; i++)
        {
            ArgumentNullException.ThrowIfNull(keys[i]);
            keyBytes += keys[i].Length;
        }

        nuint sliceBytes = checked((nuint)n * (nuint)sizeof(rocksdb_slice_t));
        nuint total = checked(sliceBytes + (nuint)keyBytes);

        // Never zero bytes, which NativeMemory may answer with null.
        _block = NativeMemory.Alloc(total == 0 ? 1 : total);
        Count = n;
        Slices = (rocksdb_slice_t*)_block;

        byte* data = (byte*)_block + sliceBytes;

        for (int i = 0; i < n; i++)
        {
            byte[] key = keys[i];

            key.CopyTo(new Span<byte>(data, key.Length));
            Slices[i].data = data;
            Slices[i].size = (nuint)key.Length;

            data += key.Length;
        }
    }

    public void Dispose() => NativeMemory.Free(_block);
}
