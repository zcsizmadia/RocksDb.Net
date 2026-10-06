namespace RocksDbNet;

/// <summary>
/// The operands of a merge, read in place from RocksDb's memory.
/// </summary>
/// <remarks>
/// <para>
/// A <see langword="ref struct"/> because the memory it reads belongs to the
/// merge in progress: RocksDb builds the operand arrays as locals of the call
/// and they are gone when it returns. The compiler therefore stops it being
/// stored in a field, captured, or kept past the callback, which is the
/// mistake the copying overloads exist to make safe. Copy an operand with
/// <see cref="ReadOnlySpan{T}.ToArray"/> if it has to outlive the call.
/// </para>
/// <para>
/// Operands are in chronological order, oldest first.
/// </para>
/// </remarks>
public readonly unsafe ref struct MergeOperands
{
    private readonly nint _pointers;
    private readonly nint _lengths;

    internal MergeOperands(nint pointers, nint lengths, int count)
    {
        _pointers = pointers;
        _lengths = lengths;
        Count = count;
    }

    /// <summary>The number of operands.</summary>
    public int Count { get; }

    /// <summary>The operand at <paramref name="index"/>, valid for the duration of the callback.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public ReadOnlySpan<byte> this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

            // Both arrays are pointer-width: the lengths are a size_t array, so
            // their element width varies with the platform, as on win-x86.
            byte* data = ((byte**)_pointers)[index];
            nuint length = ((nuint*)_lengths)[index];

            return new ReadOnlySpan<byte>(data, checked((int)length));
        }
    }

    /// <summary>Copies every operand into a managed array.</summary>
    public byte[][] ToArrays()
    {
        var result = new byte[Count][];

        for (int i = 0; i < Count; i++)
        {
            result[i] = this[i].ToArray();
        }

        return result;
    }
}
