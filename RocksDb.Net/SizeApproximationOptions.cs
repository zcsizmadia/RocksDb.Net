namespace RocksDbNet;

/// <summary>
/// Controls what <see cref="RocksDb.ApproximateSizes(SizeApproximationOptions, IEnumerable{ValueTuple{string, string}})"/>
/// counts when estimating the size of a key range.
/// Maps to <c>rocksdb_size_approximation_options_t</c>.
/// </summary>
/// <remarks>
/// Without options RocksDb counts only SST files, so data still sitting in a
/// memtable is invisible to the estimate. Set <see cref="IncludeMemtables"/> to
/// account for it.
/// </remarks>
public sealed class SizeApproximationOptions : RocksDbHandle
{
    public SizeApproximationOptions()
        : base(NativeMethods.rocksdb_size_approximation_options_create())
    {
    }

    /// <summary>
    /// If true, data still in memtables counts toward the estimate. Off by
    /// default, which is why a freshly written range can estimate as zero.
    /// </summary>
    public bool IncludeMemtables
    {
        get => NativeMethods.rocksdb_size_approximation_options_get_include_memtables(Handle) != 0;
        set => NativeMethods.rocksdb_size_approximation_options_set_include_memtables(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, SST files count toward the estimate. On by default.</summary>
    public bool IncludeFiles
    {
        get => NativeMethods.rocksdb_size_approximation_options_get_include_files(Handle) != 0;
        set => NativeMethods.rocksdb_size_approximation_options_set_include_files(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, blob files count toward the estimate.</summary>
    public bool IncludeBlobFiles
    {
        get => NativeMethods.rocksdb_size_approximation_options_get_include_blob_files(Handle) != 0;
        set => NativeMethods.rocksdb_size_approximation_options_set_include_blob_files(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// How much error RocksDb may accept in the file-size portion of the
    /// estimate, as a fraction. A larger margin lets it answer with less I/O.
    /// </summary>
    public double FilesSizeErrorMargin
    {
        get => NativeMethods.rocksdb_size_approximation_options_get_files_size_error_margin(Handle);
        set => NativeMethods.rocksdb_size_approximation_options_set_files_size_error_margin(Handle, value);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_size_approximation_options_destroy(Handle);
    }
}
