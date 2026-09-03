namespace RocksDbNet;

/// <summary>
/// Options for configuring the RocksDb environment used by
/// <see cref="SstFileWriter"/>.
/// </summary>
/// <remarks>
/// Pass an instance to <see cref="SstFileWriter.Create(EnvOptions, DbOptions)"/>.
/// The values are read when the writer is created, so changing them afterwards
/// has no effect on an existing writer.
/// </remarks>
public sealed class EnvOptions : RocksDbHandle
{
    public EnvOptions()
    {
        Handle = NativeMethods.rocksdb_envoptions_create();
    }

    /// <summary>
    /// If true, reads bypass the operating system page cache. Saves memory when
    /// the data will not be read again, and costs performance when it would.
    /// </summary>
    public bool UseDirectReads
    {
        get => NativeMethods.rocksdb_envoptions_get_use_direct_reads(Handle) != 0;
        set => NativeMethods.rocksdb_envoptions_set_use_direct_reads(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, writes bypass the operating system page cache.</summary>
    public bool UseDirectWrites
    {
        get => NativeMethods.rocksdb_envoptions_get_use_direct_writes(Handle) != 0;
        set => NativeMethods.rocksdb_envoptions_set_use_direct_writes(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, files are read through a memory mapping.</summary>
    public bool UseMmapReads
    {
        get => NativeMethods.rocksdb_envoptions_get_use_mmap_reads(Handle) != 0;
        set => NativeMethods.rocksdb_envoptions_set_use_mmap_reads(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, files are written through a memory mapping.</summary>
    public bool UseMmapWrites
    {
        get => NativeMethods.rocksdb_envoptions_get_use_mmap_writes(Handle) != 0;
        set => NativeMethods.rocksdb_envoptions_set_use_mmap_writes(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, file space is preallocated with fallocate. Turn this off on
    /// filesystems where preallocation is expensive or unsupported.
    /// </summary>
    public bool AllowFallocate
    {
        get => NativeMethods.rocksdb_envoptions_get_allow_fallocate(Handle) != 0;
        set => NativeMethods.rocksdb_envoptions_set_allow_fallocate(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, preallocation keeps the reported file size rather than extending
    /// it, so the file does not appear larger than the data written.
    /// </summary>
    public bool FallocateWithKeepSize
    {
        get => NativeMethods.rocksdb_envoptions_get_fallocate_with_keep_size(Handle) != 0;
        set => NativeMethods.rocksdb_envoptions_set_fallocate_with_keep_size(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, file descriptors are opened close-on-exec, so a child process
    /// does not inherit them.
    /// </summary>
    public bool FdCloexec
    {
        get => NativeMethods.rocksdb_envoptions_get_fd_cloexec(Handle) != 0;
        set => NativeMethods.rocksdb_envoptions_set_fd_cloexec(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Ask the operating system to flush this many bytes at a time while
    /// writing, which smooths out I/O instead of letting it burst. 0 disables it.
    /// </summary>
    public ulong BytesPerSync
    {
        get => NativeMethods.rocksdb_envoptions_get_bytes_per_sync(Handle);
        set => NativeMethods.rocksdb_envoptions_set_bytes_per_sync(Handle, value);
    }

    /// <summary>
    /// If true, <see cref="BytesPerSync"/> is a hard limit rather than a hint,
    /// giving more predictable I/O at some cost in throughput.
    /// </summary>
    public bool StrictBytesPerSync
    {
        get => NativeMethods.rocksdb_envoptions_get_strict_bytes_per_sync(Handle) != 0;
        set => NativeMethods.rocksdb_envoptions_set_strict_bytes_per_sync(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Readahead size in bytes used for compaction reads. 0 lets RocksDb choose.</summary>
    public ulong CompactionReadaheadSize
    {
        get => (ulong)NativeMethods.rocksdb_envoptions_get_compaction_readahead_size(Handle);
        set => NativeMethods.rocksdb_envoptions_set_compaction_readahead_size(Handle, (nuint)value);
    }

    /// <summary>Maximum size in bytes of the buffer used for writable files.</summary>
    public ulong WritableFileMaxBufferSize
    {
        get => (ulong)NativeMethods.rocksdb_envoptions_get_writable_file_max_buffer_size(Handle);
        set => NativeMethods.rocksdb_envoptions_set_writable_file_max_buffer_size(Handle, (nuint)value);
    }

    /// <summary>
    /// Throttles the I/O these options govern.
    /// </summary>
    /// <remarks>
    /// Write-only, because the C API offers no getter. RocksDb copies the shared
    /// pointer rather than taking ownership, so the caller keeps responsibility
    /// for disposing <paramref name="rateLimiter"/> and must keep it alive for as
    /// long as these options are in use.
    /// </remarks>
    public EnvOptions SetRateLimiter(RateLimiter? rateLimiter)
    {
        NativeMethods.rocksdb_envoptions_set_rate_limiter(Handle, rateLimiter?.Handle ?? nint.Zero);
        return this;
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_envoptions_destroy(Handle);
    }
}
