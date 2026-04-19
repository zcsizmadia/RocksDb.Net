using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>
/// Writes key-value pairs in sorted order to a standalone SST file that can
/// later be ingested into a database via <see cref="RocksDb.IngestExternalFile(System.Collections.Generic.IReadOnlyList{string}, IngestExternalFileOptions)"/>.
/// Keys must be added in ascending order.
/// </summary>
public sealed class SstFileWriter : RocksDbHandle
{
    private SstFileWriter(nint handle)
    {
        Handle = handle;
    }

    /// <summary>
    /// Creates a new <see cref="SstFileWriter"/> using default environment options
    /// and the provided database options (for comparator / compression settings).
    /// </summary>
    public static SstFileWriter Create(DbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        nint envOpts = NativeMethods.rocksdb_envoptions_create();
        // rocksdb_sstfilewriter_create takes EnvOptions + Options
        nint writer = NativeMethods.rocksdb_sstfilewriter_create(envOpts, options.Handle);
        NativeMethods.rocksdb_envoptions_destroy(envOpts);
        return new SstFileWriter(writer);
    }

    /// <summary>Opens <paramref name="filePath"/> for writing. Call before any <c>Put</c>/<c>Merge</c>/<c>Delete</c>.</summary>
    public void Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        nint err = default;
        NativeMethods.rocksdb_sstfilewriter_open(Handle, filePath, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Writes a key-value pair. Keys must be added in ascending order.</summary>
    public unsafe void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_sstfilewriter_put(Handle, k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Writes a merge operand. Keys must be added in ascending order.</summary>
    public unsafe void Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_sstfilewriter_merge(Handle, k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Writes a deletion record for <paramref name="key"/>.</summary>
    public unsafe void Delete(ReadOnlySpan<byte> key)
    {
        nint err = default;
        fixed (byte* k = key)
            NativeMethods.rocksdb_sstfilewriter_delete(Handle, k, (nuint)key.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Finalizes and closes the SST file. Must be called before ingestion.</summary>
    public void Finish()
    {
        nint err = default;
        NativeMethods.rocksdb_sstfilewriter_finish(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Returns the size of the written file in bytes (available after <see cref="Finish"/>).</summary>
    public unsafe ulong FileSize
    {
        get
        {
            ulong size;
            NativeMethods.rocksdb_sstfilewriter_file_size(Handle, (nint)(&size));
            return size;
        }
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_sstfilewriter_destroy(Handle);
    }

}