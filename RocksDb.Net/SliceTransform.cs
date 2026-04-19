using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>
/// A slice transform (prefix extractor) used with prefix bloom filters.
/// Maps to <c>rocksdb_slicetransform_t</c>.
/// </summary>
public sealed class SliceTransform : RocksDbHandle
{
    private SliceTransform(nint handle)
    {
        Handle = handle;
    }

    /// <summary>Creates a fixed-length prefix extractor.</summary>
    public static SliceTransform CreateFixedPrefix(ulong prefixLength)
        => new(NativeMethods.rocksdb_slicetransform_create_fixed_prefix((nuint)prefixLength));

    /// <summary>Creates a no-op slice transform.</summary>
    public static SliceTransform CreateNoop()
        => new(NativeMethods.rocksdb_slicetransform_create_noop());

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_slicetransform_destroy(Handle);
    }
}