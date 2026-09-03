using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// Metadata describing a column family, including its size and the levels
/// currently stored.
/// </summary>
/// <param name="Name">The name of the column family.</param>
/// <param name="Size">Total size in bytes of the files belonging to it.</param>
/// <param name="FileCount">How many SST files it holds.</param>
/// <param name="LevelCount">How many LSM levels it has.</param>
/// <param name="Levels">Metadata for each level.</param>
/// <remarks>
/// A snapshot, read in full before it is handed over, so it needs no disposal
/// and can be kept and passed around freely. It used to read through a native
/// handle on every property access, which meant an instance was only valid
/// while that handle was alive and made the whole graph disposable. See the
/// ownership guide for why this library copies rather than lending.
/// </remarks>
public sealed record ColumnFamilyMetadata(
    string Name,
    ulong Size,
    int FileCount,
    int LevelCount,
    IReadOnlyList<ColumnFamilyLevelMetadata> Levels)
{
    /// <summary>
    /// Reads everything out of a native column-family metadata handle and
    /// destroys it, along with the level and file handles reached through it.
    /// </summary>
    internal static ColumnFamilyMetadata ReadAndDestroy(nint handle)
    {
        try
        {
            int levelCount = checked((int)NativeMethods.rocksdb_column_family_metadata_get_level_count(handle));
            var levels = new ColumnFamilyLevelMetadata[levelCount];

            for (int i = 0; i < levelCount; i++)
            {
                levels[i] = ColumnFamilyLevelMetadata.ReadAndDestroy(
                    NativeMethods.rocksdb_column_family_metadata_get_level_metadata(handle, (nuint)i));
            }

            return new ColumnFamilyMetadata(
                Name: Marshal.PtrToStringUTF8(
                    NativeMethods.rocksdb_column_family_metadata_get_name(handle)) ?? string.Empty,
                Size: NativeMethods.rocksdb_column_family_metadata_get_size(handle),
                FileCount: checked((int)NativeMethods.rocksdb_column_family_metadata_get_file_count(handle)),
                LevelCount: levelCount,
                Levels: levels);
        }
        finally
        {
            NativeMethods.rocksdb_column_family_metadata_destroy(handle);
        }
    }
}

/// <summary>
/// Metadata describing the files stored at a single LSM level.
/// </summary>
/// <param name="Level">The level number.</param>
/// <param name="Size">Total size in bytes of the files at this level.</param>
/// <param name="FileCount">How many SST files are at this level.</param>
/// <param name="Files">Metadata for each of those files.</param>
public sealed record ColumnFamilyLevelMetadata(
    int Level,
    ulong Size,
    int FileCount,
    IReadOnlyList<SstFileMetadata> Files)
{
    internal static ColumnFamilyLevelMetadata ReadAndDestroy(nint handle)
    {
        try
        {
            int fileCount = checked((int)NativeMethods.rocksdb_level_metadata_get_file_count(handle));
            var files = new SstFileMetadata[fileCount];

            for (int i = 0; i < fileCount; i++)
            {
                files[i] = SstFileMetadata.ReadAndDestroy(
                    NativeMethods.rocksdb_level_metadata_get_sst_file_metadata(handle, (nuint)i));
            }

            return new ColumnFamilyLevelMetadata(
                Level: NativeMethods.rocksdb_level_metadata_get_level(handle),
                Size: NativeMethods.rocksdb_level_metadata_get_size(handle),
                FileCount: fileCount,
                Files: files);
        }
        finally
        {
            NativeMethods.rocksdb_level_metadata_destroy(handle);
        }
    }
}

/// <summary>
/// Metadata for a single SST file.
/// </summary>
/// <param name="RelativeFilename">The file name, relative to its directory.</param>
/// <param name="Directory">The directory holding the file.</param>
/// <param name="Size">The file size in bytes.</param>
/// <param name="SmallestKey">
/// The lowest key in the file, or <see langword="null"/> if RocksDb reported
/// none.
/// </param>
/// <param name="LargestKey">
/// The highest key in the file, or <see langword="null"/> if RocksDb reported
/// none.
/// </param>
public sealed record SstFileMetadata(
    string RelativeFilename,
    string Directory,
    ulong Size,
    byte[]? SmallestKey,
    byte[]? LargestKey)
{
    internal static SstFileMetadata ReadAndDestroy(nint handle)
    {
        try
        {
            return new SstFileMetadata(
                RelativeFilename: Marshal.PtrToStringUTF8(
                    NativeMethods.rocksdb_sst_file_metadata_get_relative_filename(handle)) ?? string.Empty,
                Directory: Marshal.PtrToStringUTF8(
                    NativeMethods.rocksdb_sst_file_metadata_get_directory(handle)) ?? string.Empty,
                Size: NativeMethods.rocksdb_sst_file_metadata_get_size(handle),
                SmallestKey: ReadKey(NativeMethods.rocksdb_sst_file_metadata_get_smallestkey(handle, out nuint smallestLen), smallestLen),
                LargestKey: ReadKey(NativeMethods.rocksdb_sst_file_metadata_get_largestkey(handle, out nuint largestLen), largestLen));
        }
        finally
        {
            NativeMethods.rocksdb_sst_file_metadata_destroy(handle);
        }
    }

    private static byte[]? ReadKey(nint ptr, nuint len)
    {
        if (ptr == nint.Zero)
        {
            return null;
        }

        if (len == 0)
        {
            return [];
        }

        var bytes = new byte[checked((int)len)];
        Marshal.Copy(ptr, bytes, 0, bytes.Length);
        return bytes;
    }
}
