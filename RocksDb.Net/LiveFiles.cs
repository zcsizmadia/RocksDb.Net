using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>Metadata for a single live SST file in the database.</summary>
/// <remarks>
/// <para>
/// Every property here reads through the parent <see cref="LiveFiles"/> on
/// each access rather than having been copied out, so an instance is only
/// valid while that parent is alive. Reading one after the parent has been
/// disposed reads freed native memory. Copy out what you need before
/// disposing the parent, or keep the parent alive for as long as you hold
/// these.
/// </para>
/// <para>
/// The values describe the file as of when the parent was obtained. They do
/// not update as compaction changes the file set.
/// </para>
/// </remarks>
public sealed unsafe class LiveFileMetadata
{
    private readonly nint _liveFilesHandle;
    private readonly int _index;

    internal LiveFileMetadata(nint liveFilesHandle, int index)
    {
        _liveFilesHandle = liveFilesHandle;
        _index = index;
    }

    /// <summary>The SST file name, without a directory part.</summary>
    public string Name => Marshal.PtrToStringUTF8((nint)NativeMethods.rocksdb_livefiles_name(_liveFilesHandle, _index)) ?? string.Empty;

    /// <summary>
    /// The directory holding the file, which is not always the database
    /// directory when several paths are configured.
    /// </summary>
    public string Directory => Marshal.PtrToStringUTF8((nint)NativeMethods.rocksdb_livefiles_directory(_liveFilesHandle, _index)) ?? string.Empty;

    /// <summary>
    /// The LSM level the file sits at. Zero is the newest level, written
    /// directly by flushes, and higher numbers hold older, larger, compacted
    /// data.
    /// </summary>
    public int Level => NativeMethods.rocksdb_livefiles_level(_liveFilesHandle, _index);

    /// <summary>The file size in bytes.</summary>
    public ulong Size => NativeMethods.rocksdb_livefiles_size(_liveFilesHandle, _index);

    /// <summary>
    /// The lowest key in the file, or <see langword="null"/> if RocksDb
    /// reported none. A fresh copy on each access.
    /// </summary>
    public byte[]? SmallestKey
    {
        get
        {
            byte* ptr = NativeMethods.rocksdb_livefiles_smallestkey(_liveFilesHandle, _index, out nuint len);
            return ptr == null ? null : CopyBytes((nint)ptr, len);
        }
    }

    /// <summary>
    /// The highest key in the file, or <see langword="null"/> if RocksDb
    /// reported none. A fresh copy on each access.
    /// </summary>
    public byte[]? LargestKey
    {
        get
        {
            byte* ptr = NativeMethods.rocksdb_livefiles_largestkey(_liveFilesHandle, _index, out nuint len);
            return ptr == null ? null : CopyBytes((nint)ptr, len);
        }
    }

    /// <summary>
    /// The lowest sequence number in the file. Together with
    /// <see cref="LargestSequenceNumber"/> this bounds when the file's writes
    /// happened relative to the rest of the database.
    /// </summary>
    public ulong SmallestSequenceNumber => NativeMethods.rocksdb_livefiles_smallest_seqno(_liveFilesHandle, _index);

    /// <summary>The highest sequence number in the file.</summary>
    public ulong LargestSequenceNumber => NativeMethods.rocksdb_livefiles_largest_seqno(_liveFilesHandle, _index);

    /// <summary>
    /// How many entries the file holds, counting tombstones as entries.
    /// Subtract <see cref="Deletions"/> for a rough live-key count.
    /// </summary>
    public ulong Entries => NativeMethods.rocksdb_livefiles_entries(_liveFilesHandle, _index);

    /// <summary>
    /// How many of the file's entries are tombstones. A file that is mostly
    /// tombstones is a candidate for compaction, since every read across its
    /// key range has to walk them.
    /// </summary>
    public ulong Deletions => NativeMethods.rocksdb_livefiles_deletions(_liveFilesHandle, _index);

    private static byte[] CopyBytes(nint ptr, nuint len)
    {
        if (len == 0) return [];
        var bytes = new byte[checked((int)len)];
        Marshal.Copy(ptr, bytes, 0, bytes.Length);
        return bytes;
    }
}

/// <summary>Container for live file metadata returned by RocksDB.</summary>
public sealed class LiveFiles : RocksDbHandle
{
    internal LiveFiles(nint handle)
        : base(handle)
    {
    }

    /// <summary>
    /// The files in the set. Each element borrows this object, so none of them
    /// may be used after this <see cref="LiveFiles"/> is disposed.
    /// </summary>
    public IReadOnlyList<LiveFileMetadata> Files
    {
        get
        {
            int count = NativeMethods.rocksdb_livefiles_count(Handle);
            var files = new List<LiveFileMetadata>(count);
            for (int i = 0; i < count; i++)
            {
                files.Add(new LiveFileMetadata(Handle, i));
            }

            return files;
        }
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_livefiles_destroy(Handle);
    }
}
