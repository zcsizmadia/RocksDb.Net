using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// A flag a running <see cref="RocksDb.CompactFiles(CompactFilesOptions, IReadOnlyList{string}, int, int)"/>
/// polls so it can be stopped from another thread.
/// Maps to the <c>rocksdb_compaction_options_canceled_*</c> functions.
/// </summary>
/// <remarks>
/// <para>
/// The native object is a <c>std::atomic&lt;bool&gt;</c> that the C API hands
/// back as an <c>unsigned char*</c>. It is deliberately opaque here: reading the
/// byte directly would be a non-atomic read of an atomic, so this type only
/// writes to it.
/// </para>
/// <para>
/// <see cref="CompactFilesOptions"/> stores the raw pointer without taking
/// ownership, so this object must outlive every compaction that references it.
/// Set <see cref="CompactFilesOptions.CancellationFlag"/> back to <c>null</c>
/// before disposing it if the options are still in use.
/// </para>
/// </remarks>
public sealed class CompactionCancellationFlag : IDisposable
{
    private nint _flag;

    public unsafe CompactionCancellationFlag()
        => _flag = (nint)NativeMethods.rocksdb_compaction_options_canceled_create();

    internal nint Handle => _flag;

    /// <summary>Whether this flag has been released.</summary>
    public bool IsDisposed => _flag == nint.Zero;

    /// <summary>
    /// Requests cancellation of any compaction using this flag, or clears a
    /// previous request.
    /// </summary>
    /// <remarks>
    /// A cancelled compaction fails with a <see cref="RocksDbException"/> rather
    /// than returning a partial result.
    /// </remarks>
    public unsafe void Set(bool canceled)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        NativeMethods.rocksdb_compaction_options_canceled_set((byte*)_flag, canceled ? (byte)1 : (byte)0);
    }

    public unsafe void Dispose()
    {
        nint flag = Interlocked.Exchange(ref _flag, nint.Zero);
        if (flag != nint.Zero)
        {
            NativeMethods.rocksdb_compaction_options_canceled_destroy((byte*)flag);
        }

        GC.SuppressFinalize(this);
    }

    ~CompactionCancellationFlag() => Dispose();
}

/// <summary>
/// Options for <see cref="RocksDb.CompactFiles(CompactFilesOptions, IReadOnlyList{string}, int, int)"/>,
/// which compacts an explicit list of files.
/// Maps to <c>rocksdb_compaction_options_t</c>.
/// </summary>
/// <remarks>
/// Not to be confused with <see cref="CompactRangeOptions"/>, which configures
/// <see cref="RocksDb.CompactRange(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
/// and maps to the separate <c>rocksdb_compactoptions_t</c>.
/// </remarks>
public sealed class CompactFilesOptions : RocksDbHandle
{
    // Kept only so the flag is not collected while the native options hold its
    // raw pointer. The caller still owns it.
    private CompactionCancellationFlag? _cancellationFlag;

    public CompactFilesOptions()
        : base(NativeMethods.rocksdb_compaction_options_create())
    {
    }

    /// <summary>Compression to apply to the files this compaction writes.</summary>
    public Compression Compression
    {
        get => (Compression)NativeMethods.rocksdb_compaction_options_get_compression(Handle);
        set => NativeMethods.rocksdb_compaction_options_set_compression(Handle, (int)value);
    }

    /// <summary>
    /// Maximum size in bytes of each output file. 0 lets RocksDb decide.
    /// </summary>
    public ulong OutputFileSizeLimit
    {
        get => NativeMethods.rocksdb_compaction_options_get_output_file_size_limit(Handle);
        set => NativeMethods.rocksdb_compaction_options_set_output_file_size_limit(Handle, value);
    }

    /// <summary>Number of threads the compaction may split itself across.</summary>
    public uint MaxSubcompactions
    {
        get => NativeMethods.rocksdb_compaction_options_get_max_subcompactions(Handle);
        set => NativeMethods.rocksdb_compaction_options_set_max_subcompactions(Handle, value);
    }

    /// <summary>
    /// If true, a file may be moved to the output level untouched when nothing
    /// there overlaps it, instead of being rewritten.
    /// </summary>
    public bool AllowTrivialMove
    {
        get => NativeMethods.rocksdb_compaction_options_get_allow_trivial_move(Handle) != 0;
        set => NativeMethods.rocksdb_compaction_options_set_allow_trivial_move(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Storage temperature for the output files, overriding whatever the column
    /// family would otherwise choose.
    /// </summary>
    public Temperature OutputTemperatureOverride
    {
        get => (Temperature)NativeMethods.rocksdb_compaction_options_get_output_temperature_override(Handle);
        set => NativeMethods.rocksdb_compaction_options_set_output_temperature_override(Handle, (int)value);
    }

    /// <summary>
    /// A flag the compaction polls so it can be cancelled from another thread,
    /// or <c>null</c> for no cancellation.
    /// </summary>
    /// <remarks>
    /// RocksDb stores only the raw pointer, so the flag must stay alive for as
    /// long as these options are used. Assigning <c>null</c> clears it, which is
    /// what to do before disposing a flag while the options live on.
    /// </remarks>
    public unsafe CompactionCancellationFlag? CancellationFlag
    {
        get => _cancellationFlag;
        set
        {
            ThrowIfDisposed();

            if (value is not null)
            {
                ObjectDisposedException.ThrowIf(value.IsDisposed, value);
            }

            NativeMethods.rocksdb_compaction_options_set_canceled(Handle, (byte*)(value?.Handle ?? nint.Zero));
            _cancellationFlag = value;
        }
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_compaction_options_destroy(Handle);
    }

    protected override void DisposeUnmanagedResources()
    {
        base.DisposeUnmanagedResources();

        // Drop the reference only; the flag belongs to the caller.
        _cancellationFlag = null;
    }
}
