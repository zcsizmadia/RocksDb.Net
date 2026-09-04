namespace RocksDbNet;

/// <summary>
/// Writes key-value pairs in sorted order to a standalone SST file that can
/// later be ingested into a database via <see cref="RocksDb.IngestExternalFile(System.Collections.Generic.IReadOnlyList{string}, IngestExternalFileOptions)"/>.
/// Keys must be added in ascending order.
/// </summary>
public sealed class SstFileWriter : RocksDbHandle
{
    // The options the writer was created from, held for its lifetime.
    //
    // RocksDb's SstFileWriter keeps two things out of them by raw pointer:
    // the comparator, as the user_comparator inside its InternalKeyComparator,
    // and the env, inside the ImmutableOptions it copies. Both are read on
    // every Open, Put and Finish. This used to be documented the other way
    // round — that neither argument was retained and the caller could dispose
    // them once the writer existed — so following the documentation destroyed
    // the comparator under a live writer.
    //
    // A plain reference rather than a hold. A hold would be released when the
    // writer is disposed, and a release at zero holders disposes: that would
    // destroy options the caller still owns and may still be about to open a
    // database with, which is ordinary code and what the tests here do. This
    // keeps the options from being collected and finalized under a live writer,
    // which is the part the caller cannot control; disposing them early is still
    // the caller's mistake, and the remarks on Create now say so.
    private readonly DbOptions _options;

    private SstFileWriter(nint handle, DbOptions options)
    {
        Handle = handle;
        _options = options;
    }

    /// <summary>
    /// Creates a new <see cref="SstFileWriter"/> using default environment options
    /// and the provided database options (for comparator / compression settings).
    /// </summary>
    /// <remarks>
    /// The writer keeps a reference to <paramref name="options"/> for its own
    /// lifetime, because RocksDb keeps the comparator and the env out of them by
    /// raw pointer and reads both on every write. That stops them being collected
    /// under a live writer; disposing them yourself while it is open is still your
    /// mistake to avoid.
    /// </remarks>
    public static SstFileWriter Create(DbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        nint envOpts = NativeMethods.rocksdb_envoptions_create();
        // rocksdb_sstfilewriter_create takes EnvOptions + Options
        nint writer = NativeMethods.rocksdb_sstfilewriter_create(envOpts, options.Handle);
        NativeMethods.rocksdb_envoptions_destroy(envOpts);
        return new SstFileWriter(writer, options);
    }

    /// <summary>
    /// Creates a new <see cref="SstFileWriter"/> with explicit environment
    /// options, for control over how the file is written: direct or memory
    /// mapped I/O, preallocation, sync behaviour and rate limiting.
    /// </summary>
    /// <remarks>
    /// <paramref name="envOptions"/> is read here and not retained: RocksDb copies
    /// what it needs out of it, so the caller may dispose it once the writer
    /// exists. <paramref name="options"/> is different — the writer keeps a
    /// reference to it, because RocksDb keeps the comparator and the env out of it
    /// by raw pointer and reads both on every write. Do not dispose those while
    /// the writer is open.
    /// </remarks>
    public static SstFileWriter Create(EnvOptions envOptions, DbOptions options)
    {
        ArgumentNullException.ThrowIfNull(envOptions);
        ArgumentNullException.ThrowIfNull(options);

        nint writer = NativeMethods.rocksdb_sstfilewriter_create(envOptions.Handle, options.Handle);
        return new SstFileWriter(writer, options);
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
            NativeMethods.rocksdb_sstfilewriter_file_size(Handle, &size);
            return size;
        }
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_sstfilewriter_destroy(Handle);

        // The options have to outlive the destroy above, which flushes and closes
        // the file and so reads the env out of them one last time.
        GC.KeepAlive(_options);
    }


}