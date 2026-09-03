using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// Narrows what <see cref="RocksDb.GetColumnFamilyMetadata(ColumnFamilyMetadataOptions)"/>
/// reports, by level and by key range.
/// Maps to <c>rocksdb_column_family_metadata_options_t</c>.
/// </summary>
/// <remarks>
/// Useful on a large database, where collecting metadata for every level and
/// every file is far more work than the caller needs.
/// </remarks>
public sealed class ColumnFamilyMetadataOptions : RocksDbHandle
{
    // RocksDb copies the key bytes into a std::string on the native side, per
    // db/c.cc, so unlike the iteration bounds on ReadOptions there is nothing to
    // keep alive here.
    public ColumnFamilyMetadataOptions()
        : base(NativeMethods.rocksdb_column_family_metadata_options_create())
    {
    }

    /// <summary>
    /// Restricts the result to this LSM level. A negative value, the default,
    /// covers every level.
    /// </summary>
    public int Level
    {
        get => NativeMethods.rocksdb_column_family_metadata_options_get_level(Handle);
        set => NativeMethods.rocksdb_column_family_metadata_options_set_level(Handle, value);
    }

    /// <summary>
    /// Inclusive lower bound on the keys of interest, or <c>null</c> when
    /// unbounded. Assigning an empty span clears the bound.
    /// </summary>
    public unsafe byte[]? StartKey
    {
        get
        {
            byte* ptr = NativeMethods.rocksdb_column_family_metadata_options_get_start_key(Handle, out nuint length);
            return ptr is null || length == 0 ? null : new ReadOnlySpan<byte>(ptr, checked((int)length)).ToArray();
        }
        set => SetStartKey(value);
    }

    /// <summary>
    /// Exclusive upper bound on the keys of interest, or <c>null</c> when
    /// unbounded. Assigning an empty span clears the bound.
    /// </summary>
    public unsafe byte[]? EndKey
    {
        get
        {
            byte* ptr = NativeMethods.rocksdb_column_family_metadata_options_get_end_key(Handle, out nuint length);
            return ptr is null || length == 0 ? null : new ReadOnlySpan<byte>(ptr, checked((int)length)).ToArray();
        }
        set => SetEndKey(value);
    }

    /// <summary>Sets the inclusive lower bound on the keys of interest.</summary>
    public unsafe ColumnFamilyMetadataOptions SetStartKey(ReadOnlySpan<byte> key)
    {
        fixed (byte* p = key)
            NativeMethods.rocksdb_column_family_metadata_options_set_start_key(Handle, p, (nuint)key.Length);
        return this;
    }

    /// <summary>Sets the exclusive upper bound on the keys of interest.</summary>
    public unsafe ColumnFamilyMetadataOptions SetEndKey(ReadOnlySpan<byte> key)
    {
        fixed (byte* p = key)
            NativeMethods.rocksdb_column_family_metadata_options_set_end_key(Handle, p, (nuint)key.Length);
        return this;
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_column_family_metadata_options_destroy(Handle);
    }
}
