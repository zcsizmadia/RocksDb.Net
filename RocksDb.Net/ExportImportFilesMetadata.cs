using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// How an imported column family's files are taken from the export directory.
/// Maps to <c>rocksdb_import_column_family_options_t</c>.
/// </summary>
public sealed class ImportColumnFamilyOptions : RocksDbHandle
{
    /// <summary>Creates options with RocksDb's defaults.</summary>
    public ImportColumnFamilyOptions()
        : base(NativeMethods.rocksdb_import_column_family_options_create())
    {
    }

    /// <summary>
    /// Whether the files are moved out of the export directory rather than
    /// copied. Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Moving is far cheaper for a large column family, but it consumes the
    /// export: the directory can then be imported only once, and only into one
    /// database. Copy when the same export is needed more than once.
    /// </remarks>
    public bool MoveFiles
    {
        get => NativeMethods.rocksdb_import_column_family_options_get_move_files(Handle) != 0;
        set => NativeMethods.rocksdb_import_column_family_options_set_move_files(Handle, value ? (byte)1 : (byte)0);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_import_column_family_options_destroy(Handle);
    }
}

/// <summary>
/// Describes an exported column family: the comparator it was written with and
/// the files that make it up. Maps to
/// <c>rocksdb_export_import_files_metadata_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Produced by
/// <see cref="Checkpoint.ExportColumnFamily(ColumnFamilyHandle, string)"/> and
/// consumed by
/// <see cref="RocksDb.CreateColumnFamilyWithImport(string, DbOptions, ExportImportFilesMetadata, ImportColumnFamilyOptions?)"/>.
/// </para>
/// <para>
/// It is independent of the database that produced it, so it may outlive the
/// export, but the files it names must still be where it says they are.
/// </para>
/// <para>
/// Read-only, and produced only by an export. Assembling metadata by hand,
/// which is how an export and an import in separate processes would be joined,
/// needs RocksDb's file-list builder functions; those are not wrapped, so the
/// export and the import have to happen in the same process.
/// </para>
/// </remarks>
public sealed class ExportImportFilesMetadata : RocksDbHandle
{
    internal ExportImportFilesMetadata(nint handle)
        : base(handle)
    {
    }

    /// <summary>
    /// Name of the comparator the column family was written with.
    /// </summary>
    /// <remarks>
    /// An import fails unless the receiving column family uses the same
    /// comparator, because the files are ordered by it.
    /// </remarks>
    public string DbComparatorName
    {
        get
        {
            nint ptr = NativeMethods.rocksdb_export_import_files_metadata_get_db_comparator_name(Handle);
            if (ptr == nint.Zero)
            {
                return string.Empty;
            }

            string name = Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
            NativeMethods.rocksdb_free(ptr);
            return name;
        }
    }

    /// <summary>Returns the file list.</summary>
    /// <remarks>
    /// Read in full, so the result needs no disposal and does not depend on
    /// this metadata staying alive. Changing it does not change this metadata,
    /// which is read-only.
    /// </remarks>
    public IReadOnlyList<LiveFileMetadata> GetFiles()
        => LiveFileMetadata.ReadAndDestroy(
            NativeMethods.rocksdb_export_import_files_metadata_get_files(Handle));

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_export_import_files_metadata_destroy(Handle);
    }
}
