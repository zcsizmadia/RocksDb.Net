using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>
/// A handle to a column family within a <see cref="RocksDb"/> database.
/// Destroying this handle does not drop the column family from the database.
/// </summary>
public class ColumnFamilyHandle : RocksDbHandle
{
    //private readonly bool _owned;

    /// <param name="handle">Native CF handle pointer.</param>
    /// <param name="owned">
    /// When <c>true</c> (default), Dispose calls
    /// <c>rocksdb_column_familyHandle_destroy</c>. Set to <c>false</c>
    /// for handles returned by the DB itself (e.g. the default CF) whose
    /// lifetime is managed by the database.
    /// </param>
    internal ColumnFamilyHandle(nint handle, bool owned = true)
    {
        Handle = handle;
        Owned = owned;
    }

    /// <summary>Numeric identifier for this column family.</summary>
    public uint Id => NativeMethods.rocksdb_column_family_handle_get_id(Handle);

    /// <summary>Name of this column family.</summary>
    public unsafe string Name
    {
        get
        {
            nint ptr = NativeMethods.rocksdb_column_family_handle_get_name(Handle, out nuint len);
            return NativeMethods.PtrToStringUTF8((byte*)ptr, len) ?? string.Empty;
        }
    }

    public override string ToString() => Name;

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_column_family_handle_destroy(Handle);
    }
}
