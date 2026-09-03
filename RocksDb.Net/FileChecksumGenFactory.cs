namespace RocksDbNet;

/// <summary>
/// Produces the whole-file checksums RocksDb records for each SST file it
/// writes.
/// Maps to <c>rocksdb_file_checksum_gen_factory_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Assign one to <see cref="DbOptions.SetFileChecksumGenFactory"/> before
/// opening the database. Without it RocksDb records no file checksums, and
/// <see cref="RocksDb.VerifyFileChecksums()"/> fails rather than silently
/// passing, because there is nothing to verify against.
/// </para>
/// <para>
/// Only files written while a factory was configured have a checksum, so
/// enabling this on an existing database covers new files only.
/// </para>
/// </remarks>
public sealed class FileChecksumGenFactory : RocksDbHandle
{
    private FileChecksumGenFactory(nint handle)
        : base(handle)
    {
    }

    /// <summary>
    /// Creates the built-in CRC32C checksum generator, the only one the C API
    /// exposes.
    /// </summary>
    public static FileChecksumGenFactory CreateCrc32c()
        => new(NativeMethods.rocksdb_file_checksum_gen_crc32c_factory_create());

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_file_checksum_gen_factory_destroy(Handle);
    }
}
