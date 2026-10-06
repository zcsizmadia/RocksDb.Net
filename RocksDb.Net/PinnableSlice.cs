using System.Text;

namespace RocksDbNet;

/// <summary>
/// A value read from the database without copying it into managed memory.
/// Maps to <c>rocksdb_pinnableslice_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// An ordinary <see cref="RocksDb.Get(ReadOnlySpan{byte}, ReadOptions?)"/> copies
/// twice: RocksDb allocates a copy of the value, and the wrapper copies that into
/// a managed array. This copies neither. When the value is served from the block
/// cache, <see cref="Value"/> points straight at the cached block.
/// </para>
/// <para>
/// The trade is that the value stays pinned until this object is disposed, so
/// dispose it promptly. While it lives it holds a reference to the block it came
/// from, which cannot be evicted and still counts against the cache's capacity.
/// Holding many of these, or holding one for a long time, degrades the cache.
/// </para>
/// <para>
/// <see cref="Value"/> is only valid until disposal. Copy it with
/// <see cref="ToArray"/> if it needs to outlive this object. The instance keeps
/// the database alive, so it cannot be invalidated by the database being
/// collected, but it does not survive the database being disposed explicitly:
/// see the remarks on <see cref="Value"/>.
/// </para>
/// </remarks>
public sealed unsafe class PinnableSlice : RocksDbHandle
{
    // Read once. A pinned value cannot change or move for as long as the slice
    // holds it, so asking the native side again on every access, as Value and
    // Length used to, cost a transition per read and bought nothing; a loop
    // over Value[i] paid one per element.
    private readonly byte* _data;
    private readonly int _length;

    internal PinnableSlice(nint handle, RocksDbHandle owner)
        : base(handle)
    {
        SetParent(owner);

        _data = NativeMethods.rocksdb_pinnableslice_value(handle, out nuint length);
        _length = _data is null ? 0 : checked((int)length);
    }

    /// <summary>
    /// The value, without a copy.
    /// </summary>
    /// <remarks>
    /// Valid until this instance is disposed, and no longer. It also does not
    /// survive the database being disposed first: the memory may belong to a
    /// block cache that the database owns, so reading it afterwards reads freed
    /// memory. Dispose this before the database, which the
    /// <see langword="using" /> pattern gives you for free when both are locals.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
    public ReadOnlySpan<byte> Value
    {
        get
        {
            ThrowIfDisposed();
            return new ReadOnlySpan<byte>(_data, _length);
        }
    }

    /// <summary>Length of the value in bytes.</summary>
    /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
    public int Length
    {
        get
        {
            ThrowIfDisposed();
            return _length;
        }
    }

    /// <summary>Copies the value into a new managed array.</summary>
    /// <remarks>
    /// Use this when the value has to outlive the slice. It costs the copy that
    /// the pinned read exists to avoid, so prefer reading <see cref="Value"/>
    /// directly where the lifetime allows.
    /// </remarks>
    public byte[] ToArray() => Value.ToArray();

    /// <summary>Decodes the value as UTF-8.</summary>
    public string ToUtf8String() => Encoding.UTF8.GetString(Value);

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_pinnableslice_destroy(Handle);
    }
}
