namespace RocksDbNet;

/// <summary>
/// A hint about how hot the data in a file is, which lets RocksDb place files on
/// storage suited to how often they are read.
/// </summary>
/// <remarks>
/// <para>
/// RocksDb only passes this through to the environment; a plain filesystem
/// ignores it. It matters when the <see cref="Env"/> in use maps temperatures
/// onto different storage tiers.
/// </para>
/// <para>
/// The values are not contiguous, and are mirrored from
/// <c>include/rocksdb/types.h</c> because the C API declares this parameter as a
/// plain <c>int</c>.
/// </para>
/// </remarks>
public enum Temperature
{
    /// <summary>No temperature information. This is the default.</summary>
    Unknown = 0,

    /// <summary>Read very frequently. Belongs on the fastest storage.</summary>
    Hot = 0x04,

    /// <summary>Read regularly, but less than <see cref="Hot"/>.</summary>
    Warm = 0x08,

    /// <summary>Read occasionally.</summary>
    Cool = 0x0A,

    /// <summary>Read rarely. Suited to cheaper, slower storage.</summary>
    Cold = 0x0C,

    /// <summary>Effectively archival, read almost never.</summary>
    Ice = 0x10,
}

/// <summary>
/// The kinds of file RocksDb manages inside a database directory.
/// </summary>
/// <remarks>
/// Mirrored from <c>include/rocksdb/types.h</c> because the C API declares these
/// parameters as a plain <c>int</c>.
/// </remarks>
public enum FileType
{
    /// <summary>A write-ahead log file.</summary>
    WalFile = 0,

    /// <summary>The legacy lock file.</summary>
    DbLockFile = 1,

    /// <summary>An SST table file.</summary>
    TableFile = 2,

    /// <summary>A manifest describing the current set of files.</summary>
    DescriptorFile = 3,

    /// <summary>The CURRENT file naming the live manifest.</summary>
    CurrentFile = 4,

    /// <summary>A temporary file being written before being renamed into place.</summary>
    TempFile = 5,

    /// <summary>An informational log file, meaning RocksDb's own LOG output.</summary>
    InfoLogFile = 6,

    /// <summary>A metadata database.</summary>
    MetaDatabase = 7,

    /// <summary>The IDENTITY file holding the database's unique identifier.</summary>
    IdentityFile = 8,

    /// <summary>An OPTIONS file recording the options the database was opened with.</summary>
    OptionsFile = 9,

    /// <summary>A blob file holding values stored outside the SST files.</summary>
    BlobFile = 10,

    /// <summary>A file recording compaction progress.</summary>
    CompactionProgressFile = 11,
}

/// <summary>
/// The tiers of cache a read is permitted to reach into.
/// </summary>
/// <remarks>
/// Mirrored from <c>include/rocksdb/advanced_options.h</c> because the C API
/// declares this parameter as a plain <c>int</c>.
/// </remarks>
public enum CacheTier
{
    /// <summary>Only the in-memory block cache.</summary>
    Volatile = 0,

    /// <summary>The in-memory block cache, including its compressed portion.</summary>
    VolatileCompressed = 1,

    /// <summary>Any tier, including a non-volatile secondary cache backed by storage.</summary>
    NonVolatileBlock = 2,
}
