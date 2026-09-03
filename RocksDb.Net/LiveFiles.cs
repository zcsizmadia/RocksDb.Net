using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>Metadata for a single live SST file in the database.</summary>
/// <param name="Name">The SST file name, without a directory part.</param>
/// <param name="Directory">
/// The directory holding the file, which is not always the database directory
/// when several paths are configured.
/// </param>
/// <param name="Level">
/// The LSM level the file sits at. Zero is the newest level, written directly
/// by flushes, and higher numbers hold older, larger, compacted data.
/// </param>
/// <param name="Size">The file size in bytes.</param>
/// <param name="SmallestKey">
/// The lowest key in the file, or <see langword="null"/> if RocksDb reported
/// none.
/// </param>
/// <param name="LargestKey">
/// The highest key in the file, or <see langword="null"/> if RocksDb reported
/// none.
/// </param>
/// <param name="SmallestSequenceNumber">
/// The lowest sequence number in the file. With
/// <paramref name="LargestSequenceNumber"/> this bounds when the file's writes
/// happened relative to the rest of the database.
/// </param>
/// <param name="LargestSequenceNumber">The highest sequence number in the file.</param>
/// <param name="Entries">
/// How many entries the file holds, counting tombstones as entries. Subtract
/// <paramref name="Deletions"/> for a rough live-key count.
/// </param>
/// <param name="Deletions">
/// How many of the file's entries are tombstones. A file that is mostly
/// tombstones is a candidate for compaction, since every read across its key
/// range has to walk them.
/// </param>
/// <remarks>
/// A snapshot, read in full before it is handed over, so it needs no disposal
/// and can be kept and passed around freely. It used to read through its
/// parent on every property access, which meant an instance was only valid
/// while that parent was alive. See the ownership guide for why this library
/// copies rather than lending.
/// </remarks>
public sealed record LiveFileMetadata(
    string Name,
    string Directory,
    int Level,
    ulong Size,
    byte[]? SmallestKey,
    byte[]? LargestKey,
    ulong SmallestSequenceNumber,
    ulong LargestSequenceNumber,
    ulong Entries,
    ulong Deletions)
{
    /// <summary>
    /// Reads every entry out of a native live-files handle and destroys it.
    /// </summary>
    internal static unsafe IReadOnlyList<LiveFileMetadata> ReadAndDestroy(nint handle)
    {
        try
        {
            int count = NativeMethods.rocksdb_livefiles_count(handle);
            var files = new LiveFileMetadata[count];

            for (int i = 0; i < count; i++)
            {
                byte* smallest = NativeMethods.rocksdb_livefiles_smallestkey(handle, i, out nuint smallestLen);
                byte* largest = NativeMethods.rocksdb_livefiles_largestkey(handle, i, out nuint largestLen);

                files[i] = new LiveFileMetadata(
                    Name: Marshal.PtrToStringUTF8(
                        (nint)NativeMethods.rocksdb_livefiles_name(handle, i)) ?? string.Empty,
                    Directory: Marshal.PtrToStringUTF8(
                        (nint)NativeMethods.rocksdb_livefiles_directory(handle, i)) ?? string.Empty,
                    Level: NativeMethods.rocksdb_livefiles_level(handle, i),
                    Size: NativeMethods.rocksdb_livefiles_size(handle, i),
                    SmallestKey: ReadKey(smallest, smallestLen),
                    LargestKey: ReadKey(largest, largestLen),
                    SmallestSequenceNumber: NativeMethods.rocksdb_livefiles_smallest_seqno(handle, i),
                    LargestSequenceNumber: NativeMethods.rocksdb_livefiles_largest_seqno(handle, i),
                    Entries: NativeMethods.rocksdb_livefiles_entries(handle, i),
                    Deletions: NativeMethods.rocksdb_livefiles_deletions(handle, i));
            }

            return files;
        }
        finally
        {
            NativeMethods.rocksdb_livefiles_destroy(handle);
        }
    }

    private static unsafe byte[]? ReadKey(byte* ptr, nuint len)
    {
        if (ptr is null)
        {
            return null;
        }

        return len == 0 ? [] : new ReadOnlySpan<byte>(ptr, checked((int)len)).ToArray();
    }
}
