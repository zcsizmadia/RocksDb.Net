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
    // Kept alive for as long as this iterator is, and never disposed here.
    //
    // RocksDb stores an iterate bound as a pointer *into* the
    // rocksdb_readoptions_t struct, and NewIterator copies the options by
    // value, so a live iterator dereferences that address on every Seek and
    // Next. Letting the options be collected while the iterator was still in
    // use therefore read freed memory. The same applies to a table filter,
    // whose callback state the options own.
    //
    // This does not make an explicit early Dispose of the options safe. Nothing
    // can: the native struct is gone at that point. It removes the far more
    // common failure, where the options were simply not kept in a variable.
    private readonly ReadOptions? _options;

    // A second object this iterator reads through, kept alive for the same
    // reason as the options. RocksDbHandle tracks one parent, which is the
    // thing whose closing invalidates the iterator. An overlay iterator over a
    // WriteBatchWithIndex also reads the batch, and that must not be collected
    // underneath it either.
    private readonly RocksDbHandle? _secondary;

    private Iterator(nint handle, RocksDbHandle owner, ReadOptions? options, RocksDbHandle? secondary)
    {
        Handle = handle;
        _options = options;
        _secondary = secondary;
        SetParent(owner);
    }

    internal static Iterator FromHandle(nint handle, RocksDbHandle owner, ReadOptions? options)
        => new(handle, owner, options, secondary: null);

    /// <summary>
    /// For an iterator that reads through two objects, such as a database
    /// overlaid with an indexed write batch.
    /// </summary>
    internal static Iterator FromHandle(
        nint handle, RocksDbHandle owner, ReadOptions? options, RocksDbHandle secondary)
        => new(handle, owner, options, secondary);

    /// <summary>Whether this iterator reads through a second source.</summary>
    internal bool HasSecondarySource => _secondary is not null;

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

    /// <summary>
    /// Receives one key and value during <see cref="ForEach"/>.
    /// </summary>
    /// <param name="key">
    /// The current key. Valid only for the duration of the call, because it
    /// points into the iterator's buffer; copy it to keep it.
    /// </param>
    /// <param name="value">
    /// The current value, with the same lifetime as <paramref name="key"/>.
    /// </param>
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

    /// <summary>
    /// Walks an iterator without allocating, yielding spans that point
    /// straight at the iterator's buffers.
    /// </summary>
    /// <remarks>
    /// A <see langword="ref struct"/>, so it cannot be boxed, stored in a
    /// field, or used across an <see langword="await"/>. That is deliberate:
    /// the spans it hands out are only valid until the iterator moves, and
    /// those restrictions are what stop one outliving its data.
    /// </remarks>
    public ref struct Enumerator
    {
        private readonly Iterator _iterator;

        private bool _started;

        internal Enumerator(Iterator iterator)
        {
            _iterator = iterator;
            _started = false;
        }

        /// <summary>
        /// Advances to the next entry.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> if there is an entry to read;
        /// <see langword="false"/> once the iterator is exhausted.
        /// </returns>
        public bool MoveNext()
        {
            // A RocksDb iterator already sits on the first entry after a seek,
            // whereas an enumerator has to start before the first element. So
            // the first call reports the position it was handed and every later
            // one advances.
            //
            // Advancing on the first call, which is what this did before, was
            // wrong twice over: it skipped the first entry, and it returned
            // true once more after the last one, leaving the caller reading an
            // invalid iterator. That second part is worse than it sounds,
            // because the native accessors do not fail on an invalid iterator;
            // they return whatever the buffer happens to hold.
            if (_started)
            {
                _iterator.Next();
            }
            else
            {
                _started = true;
            }

            if (_iterator.IsValid())
            {
                return true;
            }

            // An iterator stops being valid either because it ran out or
            // because the read failed. Only the second is an error, and the
            // loop simply ending would otherwise swallow it.
            _iterator.CheckForError();
            return false;
        }

        /// <summary>
        /// The current entry. Invalidated by the next <see cref="MoveNext"/>.
        /// </summary>
        /// <remarks>
        /// Present so <c>foreach</c> works. Without a <c>Current</c> the
        /// pattern is not satisfied and <c>foreach (var e in iterator)</c> did
        /// not compile, despite <see cref="GetEnumerator"/> claiming to return
        /// an enumerator over the collection.
        /// </remarks>
        public Entry Current => new(_iterator.Key(), _iterator.Value());

        /// <summary>
        /// The current key. Invalidated by the next <see cref="MoveNext"/>.
        /// </summary>
        public ReadOnlySpan<byte> CurrentKey => _iterator.Key();

        /// <summary>
        /// The current value. Invalidated by the next <see cref="MoveNext"/>.
        /// </summary>
        public ReadOnlySpan<byte> CurrentValue => _iterator.Value();
    }

    /// <summary>
    /// One key and value from an <see cref="Enumerator"/>.
    /// </summary>
    /// <remarks>
    /// A <see langword="ref struct"/> rather than a tuple, because
    /// <see cref="ValueTuple{T1, T2}"/> cannot hold a
    /// <see cref="ReadOnlySpan{T}"/>. Both spans point into the iterator's own
    /// buffers and are invalidated as soon as it moves, so copy anything you
    /// need to keep.
    /// </remarks>
    public readonly ref struct Entry
    {
        internal Entry(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            Key = key;
            Value = value;
        }

        /// <summary>The key.</summary>
        public ReadOnlySpan<byte> Key { get; }

        /// <summary>The value.</summary>
        public ReadOnlySpan<byte> Value { get; }
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_iter_destroy(Handle);
    }
}
