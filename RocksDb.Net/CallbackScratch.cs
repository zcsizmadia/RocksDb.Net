using System.Buffers;
using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// A growable native buffer, one per thread and nesting depth, that a callback
/// writes its result into and hands to RocksDb.
/// </summary>
/// <remarks>
/// <para>
/// The compaction filter and merge operator callbacks return a value RocksDb
/// copies with <c>std::string::assign</c> straight after the callback returns,
/// on the same thread, before anything else runs there. So the memory only has
/// to survive until the next callback on that thread, and one buffer per
/// thread, reused, does that. It replaces an <c>AllocHGlobal</c> for every
/// changed value or merge, and for the compaction filter two
/// <c>ConcurrentDictionary</c> operations on every key it saw.
/// </para>
/// <para>
/// One per nesting depth rather than one per thread, as a precaution. RocksDb
/// does not support re-entering itself from a merge operator, which is the
/// obvious way two of these callbacks would nest on one thread, but if a
/// callback ever does run inside another, the inner one takes the next buffer
/// up and the outer one's result is left alone rather than overwritten. It
/// costs an index into a list. The buffers stay
/// referenced from the thread for as long as it lives, and each frees its
/// memory from a finalizer once the thread is gone, so none is released while
/// RocksDb may still be about to copy it.
/// </para>
/// </remarks>
internal sealed unsafe class CallbackScratch : IBufferWriter<byte>
{
    [ThreadStatic]
    private static List<CallbackScratch>? t_buffers;

    [ThreadStatic]
    private static int t_depth;

    private byte* _buffer;
    private int _capacity;
    private int _written;
    private readonly ScratchMemory _memory;

    private CallbackScratch() => _memory = new ScratchMemory(this);

    ~CallbackScratch()
    {
        NativeMemory.Free(_buffer);
    }

    /// <summary>Takes this thread's next free buffer, emptied. Pair with <see cref="Release"/>.</summary>
    public static CallbackScratch Acquire()
    {
        List<CallbackScratch> buffers = t_buffers ??= [];

        if (t_depth == buffers.Count)
        {
            buffers.Add(new CallbackScratch());
        }

        CallbackScratch scratch = buffers[t_depth++];
        scratch._written = 0;
        return scratch;
    }

    /// <summary>
    /// Gives the buffer back. Its contents stay intact until this thread's
    /// next <see cref="Acquire"/>, which is what lets RocksDb copy them after
    /// the callback has returned.
    /// </summary>
    public static void Release() => t_depth--;

    /// <summary>How many bytes have been written.</summary>
    public int WrittenCount => _written;

    /// <summary>
    /// The written bytes, for RocksDb to copy. Never null, even for an empty
    /// result, so the pointer handed over is always a real allocation.
    /// </summary>
    public byte* Pointer
    {
        get
        {
            EnsureCapacity(1);
            return _buffer;
        }
    }

    /// <summary>Appends <paramref name="value"/>.</summary>
    public void Write(ReadOnlySpan<byte> value)
    {
        value.CopyTo(GetSpan(value.Length));
        Advance(value.Length);
    }

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count > _capacity - _written)
        {
            throw new InvalidOperationException("Advanced past the end of the buffer.");
        }

        _written += count;
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(checked(_written + Math.Max(sizeHint, 1)));
        return new Span<byte>(_buffer + _written, _capacity - _written);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(checked(_written + Math.Max(sizeHint, 1)));
        return _memory.Memory[_written.._capacity];
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _capacity)
        {
            return;
        }

        int capacity = Math.Max(required, Math.Max(256, _capacity * 2));
        _buffer = (byte*)NativeMemory.Realloc(_buffer, (nuint)capacity);
        _capacity = capacity;
    }

    /// <summary>
    /// Exposes the native buffer as <see cref="Memory{T}"/>, which
    /// <see cref="IBufferWriter{T}"/> requires alongside spans.
    /// </summary>
    private sealed class ScratchMemory(CallbackScratch owner) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => new(owner._buffer, owner._capacity);

        public override MemoryHandle Pin(int elementIndex = 0) => new(owner._buffer + elementIndex);

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
