using RocksDbNet.Native;
using System.Runtime.CompilerServices;
using System.Text;

namespace RocksDbNet;

/// <summary>
/// Iterates over the key-value pairs in a <see cref="RocksDb"/> database
/// or a single column family. Implements forward and reverse iteration.
/// Maps to <c>rocksdb_iterator_t</c>.
/// </summary>
public sealed class Iterator : RocksDbHandle
{
    private Iterator(IntPtr handle)
    {
        Handle = handle;
    }

    public static Iterator FromHandle(IntPtr handle)
    {
        return new Iterator(handle);
    }

    /// <summary>Returns true if the iterator is positioned at a valid entry.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid() => NativeMethods.rocksdb_iter_valid(Handle) != 0;

    /// <summary>Positions the iterator at the first key in the database.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SeekToFirst() => NativeMethods.rocksdb_iter_seek_to_first(Handle);

    /// <summary>Positions the iterator at the last key in the database.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SeekToLast() => NativeMethods.rocksdb_iter_seek_to_last(Handle);

    /// <summary>Positions the iterator at the first key that is &gt;= <paramref name="key"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void Seek(ReadOnlySpan<byte> key)
    {
        fixed (byte* ptr = key)
            NativeMethods.rocksdb_iter_seek(Handle, ptr, (nuint)key.Length);
    }

    /// <summary>Positions the iterator at the last key that is &lt;= <paramref name="key"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void SeekForPrev(ReadOnlySpan<byte> key)
    {
        fixed (byte* ptr = key)
            NativeMethods.rocksdb_iter_seek_for_prev(Handle, ptr, (nuint)key.Length);
    }

    /// <summary>Seeks using a UTF-8 encoded string key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Seek(string key) => Seek(Encoding.UTF8.GetBytes(key));

    /// <summary>Seeks using a UTF-8 encoded string key (SeekForPrev direction).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SeekForPrev(string key) => SeekForPrev(Encoding.UTF8.GetBytes(key));

    /// <summary>Moves to the next entry. Call <see cref="IsValid"/> before reading key/value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Next() => NativeMethods.rocksdb_iter_next(Handle);

    /// <summary>Moves to the previous entry. Call <see cref="IsValid"/> before reading key/value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Prev() => NativeMethods.rocksdb_iter_prev(Handle);

    /// <summary>
    /// Returns the current key as a read-only span.
    /// The span is valid only until the next iterator operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ReadOnlySpan<byte> Key()
    {
        byte* ptr = NativeMethods.rocksdb_iter_key(Handle, out nuint len);
        return new ReadOnlySpan<byte>(ptr, checked((int)len));
    }

    /// <summary>
    /// Returns the current value as a read-only span.
    /// The span is valid only until the next iterator operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ReadOnlySpan<byte> Value()
    {
        byte* ptr = NativeMethods.rocksdb_iter_value(Handle, out nuint len);
        return new ReadOnlySpan<byte>(ptr, checked((int)len));
    }

    /// <summary>Returns a copy of the current key as a byte array.</summary>
    public byte[] KeyToArray() => Key().ToArray();

    /// <summary>Returns a copy of the current value as a byte array.</summary>
    public byte[] ValueToArray() => Value().ToArray();

    /// <summary>Returns the current key decoded as a UTF-8 string.</summary>
    public string KeyAsString() => Encoding.UTF8.GetString(Key());

    /// <summary>Returns the current value decoded as a UTF-8 string.</summary>
    public string ValueAsString() => Encoding.UTF8.GetString(Value());

    /// <summary>Throws a <see cref="RocksDbException"/> if the iterator is in an error state.</summary>
    public void CheckForError()
    {
        nint err = default;
        NativeMethods.rocksdb_iter_get_error(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Refreshes the iterator using the current state of the DB.
    /// Only valid for iterators that support refresh (e.g. non-tailing iterators).
    /// </summary>
    public void Refresh()
    {
        nint err = default;
        NativeMethods.rocksdb_iter_refresh(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Enumerates all key-value pairs forward from the current position,
    /// returning heap-allocated copies. Use <see cref="Key()"/> / <see cref="Value()"/>
    /// directly for zero-copy access within a manual loop.
    /// </summary>
    public IEnumerable<(byte[] Key, byte[] Value)> AsEnumerable()
    {
        while (IsValid())
        {
            yield return (Key().ToArray(), Value().ToArray());
            Next();
        }
        CheckForError();
    }

    public delegate void ForEachDelegate(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);

    /// <summary>
    /// Invokes the specified delegate for each key/value pair in the collection, in enumeration order.
    /// </summary>
    /// <remarks>If the collection is modified during enumeration, the behavior of this method is undefined.
    /// Any exception thrown by the delegate will halt enumeration and propagate to the caller.</remarks>
    /// <param name="action">The delegate to invoke for each key/value pair. The delegate receives the current key and value as arguments.
    /// Cannot be null.</param>
    public void ForEach(ForEachDelegate action)
    {
        while (IsValid())
        {
            action(Key(), Value());
            Next();
        }
        CheckForError();
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    /// <returns>An enumerator that can be used to iterate through the collection.</returns>
    public Enumerator GetEnumerator() => new Enumerator(this);

    public ref struct Enumerator
    {
        private readonly Iterator _iterator;

        internal Enumerator(Iterator iterator) { _iterator = iterator; }

        public bool MoveNext()
        {
            if (!_iterator.IsValid())
            {
                return false;
            }

            _iterator.Next();

            return true;
        }

        public ReadOnlySpan<byte> CurrentKey => _iterator.Key();

        public ReadOnlySpan<byte> CurrentValue => _iterator.Value();
    }

    public override void DisposeUnmanagedResources()
    {
        NativeMethods.rocksdb_iter_destroy(Handle);
    }
}
