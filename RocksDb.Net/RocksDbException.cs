namespace RocksDbNet;

/// <summary>Represents an error returned by the RocksDB native library.</summary>
public sealed class RocksDbException : Exception
{
    public RocksDbException(string message) : base(message) { }

    public RocksDbException(string message, Exception inner) : base(message, inner) { }
}
