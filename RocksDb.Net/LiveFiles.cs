using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>Metadata for a single live SST file in the database.</summary>
public sealed unsafe class LiveFileMetadata
{
    private readonly nint _liveFilesHandle;
    private readonly int _index;

    internal LiveFileMetadata(nint liveFilesHandle, int index)
    {
        _liveFilesHandle = liveFilesHandle;
        _index = index;
    }

    public string Name => Marshal.PtrToStringUTF8((nint)NativeMethods.rocksdb_livefiles_name(_liveFilesHandle, _index)) ?? string.Empty;

    public string Directory => Marshal.PtrToStringUTF8((nint)NativeMethods.rocksdb_livefiles_directory(_liveFilesHandle, _index)) ?? string.Empty;

    public int Level => NativeMethods.rocksdb_livefiles_level(_liveFilesHandle, _index);

    public ulong Size => NativeMethods.rocksdb_livefiles_size(_liveFilesHandle, _index);

    public byte[]? SmallestKey
    {
        get
        {
            byte* ptr = NativeMethods.rocksdb_livefiles_smallestkey(_liveFilesHandle, _index, out nuint len);
            return ptr == null ? null : CopyBytes((nint)ptr, len);
        }
    }

    public byte[]? LargestKey
    {
        get
        {
            byte* ptr = NativeMethods.rocksdb_livefiles_largestkey(_liveFilesHandle, _index, out nuint len);
            return ptr == null ? null : CopyBytes((nint)ptr, len);
        }
    }

    public ulong SmallestSequenceNumber => NativeMethods.rocksdb_livefiles_smallest_seqno(_liveFilesHandle, _index);

    public ulong LargestSequenceNumber => NativeMethods.rocksdb_livefiles_largest_seqno(_liveFilesHandle, _index);

    public ulong Entries => NativeMethods.rocksdb_livefiles_entries(_liveFilesHandle, _index);

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

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_livefiles_destroy(Handle);
    }
}
