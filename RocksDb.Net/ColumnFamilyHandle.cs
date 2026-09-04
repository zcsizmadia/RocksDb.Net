namespace RocksDbNet;

/// <summary>
/// A handle to a column family within a <see cref="RocksDb"/> database.
/// Destroying this handle does not drop the column family from the database.
/// </summary>
public class ColumnFamilyHandle : RocksDbHandle
{
    /// <param name="handle">Native CF handle pointer.</param>
    internal ColumnFamilyHandle(nint handle)
        : base(handle)
    {
    }

    /// <summary>Numeric identifier for this column family.</summary>
    public uint Id => NativeMethods.rocksdb_column_family_handle_get_id(Handle);

    /// <summary>Name of this column family.</summary>
    /// <remarks>
    /// The native accessor returns a fresh copy rather than a pointer into
    /// the handle, so the caller owns it. Reading this without freeing leaked
    /// the name on every access, and the database constructor reads it once
    /// per column family.
    /// </remarks>
    public unsafe string Name
    {
        get
        {
            nint ptr = NativeMethods.rocksdb_column_family_handle_get_name(Handle, out nuint len);

            if (ptr == nint.Zero)
            {
                return string.Empty;
            }

            try
            {
                return NativeMethods.PtrToStringUTF8((byte*)ptr, len) ?? string.Empty;
            }
            finally
            {
                NativeMethods.rocksdb_free(ptr);
            }
        }
    }

    public override string ToString() => Name;

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_column_family_handle_destroy(Handle);
    }
}
