using System.Buffers;
using System.Text;

namespace RocksDbNet;

/// <summary>
/// One or two strings encoded as UTF-8 into a single buffer rented from the
/// shared pool, for the length of one call.
/// </summary>
/// <remarks>
/// <para>
/// The string overloads used to call <see cref="Encoding.GetBytes(string)"/>
/// for the key and again for the value, which is two arrays per call that are
/// garbage the moment it returns. Every native call they make copies what it
/// is given or reads it only for the duration of the call, so the bytes can go
/// back to the pool as soon as the call returns.
/// </para>
/// <para>
/// Only for that shape. Anything RocksDb keeps a pointer to after the call,
/// such as an iterate bound, must not be encoded through this.
/// </para>
/// </remarks>
internal ref struct PooledUtf8
{
    private byte[]? _rented;
    private readonly int _firstLength;
    private readonly int _secondLength;

    private PooledUtf8(byte[]? rented, int firstLength, int secondLength)
    {
        _rented = rented;
        _firstLength = firstLength;
        _secondLength = secondLength;
    }

    /// <summary>The first string's bytes, valid until disposal.</summary>
    public readonly ReadOnlySpan<byte> First
        => _rented is null ? default : new ReadOnlySpan<byte>(_rented, 0, _firstLength);

    /// <summary>The second string's bytes, valid until disposal.</summary>
    public readonly ReadOnlySpan<byte> Second
        => _rented is null ? default : new ReadOnlySpan<byte>(_rented, _firstLength, _secondLength);

    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static PooledUtf8 Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int length = Encoding.UTF8.GetByteCount(value);
        if (length == 0)
        {
            return default;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        Encoding.UTF8.GetBytes(value, rented);
        return new PooledUtf8(rented, length, 0);
    }

    /// <exception cref="ArgumentNullException">Either string is null.</exception>
    public static PooledUtf8 Encode(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        int firstLength = Encoding.UTF8.GetByteCount(first);
        int secondLength = Encoding.UTF8.GetByteCount(second);
        int total = checked(firstLength + secondLength);

        if (total == 0)
        {
            return default;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(total);
        Encoding.UTF8.GetBytes(first, rented);
        Encoding.UTF8.GetBytes(second, rented.AsSpan(firstLength));
        return new PooledUtf8(rented, firstLength, secondLength);
    }

    public void Dispose()
    {
        if (_rented is not null)
        {
            ArrayPool<byte>.Shared.Return(_rented);
            _rented = null;
        }
    }
}
