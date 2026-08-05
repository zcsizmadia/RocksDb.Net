using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// Metadata describing a column family, including its size and the levels currently stored.
/// </summary>
public sealed class ColumnFamilyMetadata : RocksDbHandle
{
    internal ColumnFamilyMetadata(nint handle)
        : base(handle)
    {
    }

    /// <summary>Gets the name of the column family.</summary>
    public string Name => Marshal.PtrToStringUTF8(NativeMethods.rocksdb_column_family_metadata_get_name(Handle)) ?? string.Empty;

    /// <summary>Gets the total size of files belonging to this column family in bytes.</summary>
    public ulong Size => NativeMethods.rocksdb_column_family_metadata_get_size(Handle);

    /// <summary>Gets the number of SST files in this column family.</summary>
    public nuint FileCount => NativeMethods.rocksdb_column_family_metadata_get_file_count(Handle);

    /// <summary>Gets the number of levels in this column family.</summary>
    public nuint LevelCount => NativeMethods.rocksdb_column_family_metadata_get_level_count(Handle);

    /// <summary>Gets metadata for each level in this column family.</summary>
    public IReadOnlyList<ColumnFamilyLevelMetadata> Levels
    {
        get
        {
            var count = LevelCount;
            var levels = new List<ColumnFamilyLevelMetadata>((int)count);
            for (nuint i = 0; i < count; i++)
            {
                nint levelMetadataHandle = NativeMethods.rocksdb_column_family_metadata_get_level_metadata(Handle, i);
                levels.Add(new ColumnFamilyLevelMetadata(levelMetadataHandle));
            }

            return levels;
        }
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_column_family_metadata_destroy(Handle);
    }
}

/// <summary>
/// Metadata describing the files stored at a single LSM level.
/// </summary>
public sealed class ColumnFamilyLevelMetadata : RocksDbHandle
{
    internal ColumnFamilyLevelMetadata(nint handle)
        : base(handle)
    {
    }

    /// <summary>Gets the level number.</summary>
    public int Level => NativeMethods.rocksdb_level_metadata_get_level(Handle);

    /// <summary>Gets the total size of files at this level in bytes.</summary>
    public ulong Size => NativeMethods.rocksdb_level_metadata_get_size(Handle);

    /// <summary>Gets the number of SST files at this level.</summary>
    public nuint FileCount => NativeMethods.rocksdb_level_metadata_get_file_count(Handle);

    /// <summary>Gets metadata for each SST file at this level.</summary>
    public IReadOnlyList<SstFileMetadata> Files
    {
        get
        {
            var count = FileCount;
            var files = new List<SstFileMetadata>((int)count);
            for (nuint i = 0; i < count; i++)
            {
                nint fileMetadataHandle = NativeMethods.rocksdb_level_metadata_get_sst_file_metadata(Handle, i);
                files.Add(new SstFileMetadata(fileMetadataHandle));
            }

            return files;
        }
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_level_metadata_destroy(Handle);
    }
}

/// <summary>
/// Metadata for a single SST file.
/// </summary>
public sealed class SstFileMetadata : RocksDbHandle
{
    internal SstFileMetadata(nint handle)
        : base(handle)
    {
    }

    /// <summary>Gets the file name relative to the DB directory.</summary>
    public string RelativeFilename => Marshal.PtrToStringUTF8(NativeMethods.rocksdb_sst_file_metadata_get_relative_filename(Handle)) ?? string.Empty;

    /// <summary>Gets the directory containing the file.</summary>
    public string Directory => Marshal.PtrToStringUTF8(NativeMethods.rocksdb_sst_file_metadata_get_directory(Handle)) ?? string.Empty;

    /// <summary>Gets the size of the SST file in bytes.</summary>
    public ulong Size => NativeMethods.rocksdb_sst_file_metadata_get_size(Handle);

    /// <summary>Gets the smallest key stored in the SST file.</summary>
    public byte[]? SmallestKey
    {
        get
        {
            nint ptr = NativeMethods.rocksdb_sst_file_metadata_get_smallestkey(Handle, out nuint len);
            return ptr == nint.Zero ? null : CopyBytes(ptr, len);
        }
    }

    /// <summary>Gets the largest key stored in the SST file.</summary>
    public byte[]? LargestKey
    {
        get
        {
            nint ptr = NativeMethods.rocksdb_sst_file_metadata_get_largestkey(Handle, out nuint len);
            return ptr == nint.Zero ? null : CopyBytes(ptr, len);
        }
    }

    private static byte[] CopyBytes(nint ptr, nuint len)
    {
        if (len == 0)
        {
            return [];
        }

        var bytes = new byte[checked((int)len)];
        Marshal.Copy(ptr, bytes, 0, bytes.Length);
        return bytes;
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_sst_file_metadata_destroy(Handle);
    }
}
