namespace RocksDbNet;

/// <summary>
/// Labels the kind of work an I/O operation belongs to, which RocksDb passes
/// through to the filesystem and to its own statistics.
/// </summary>
/// <remarks>
/// <para>
/// This is experimental in RocksDb. Leaving it unset lets RocksDb label the
/// operation itself, which is normally what you want; setting it by hand
/// overrides that.
/// </para>
/// <para>
/// Values 11 through 0x7F are reserved by RocksDb for future use, and 0x80
/// upwards are available for application-defined activities. Mirrored from
/// <c>include/rocksdb/env.h</c> because the C API declares this parameter as a
/// plain <c>int</c>.
/// </para>
/// </remarks>
public enum IoActivity
{
    /// <summary>Writing a memtable out to an SST file.</summary>
    Flush = 0,

    /// <summary>Compaction.</summary>
    Compaction = 1,

    /// <summary>Opening the database.</summary>
    DbOpen = 2,

    /// <summary>A point lookup.</summary>
    Get = 3,

    /// <summary>A batched point lookup.</summary>
    MultiGet = 4,

    /// <summary>Iteration.</summary>
    DbIterator = 5,

    /// <summary>Verifying database checksums.</summary>
    VerifyDbChecksum = 6,

    /// <summary>Verifying file checksums.</summary>
    VerifyFileChecksums = 7,

    /// <summary>A wide-column point lookup.</summary>
    GetEntity = 8,

    /// <summary>A batched wide-column point lookup.</summary>
    MultiGetEntity = 9,

    /// <summary>Reading file checksums out of the current manifest.</summary>
    GetFileChecksumsFromCurrentManifest = 10,

    /// <summary>The first value available for application-defined activities.</summary>
    FirstCustom = 0x80,

    /// <summary>
    /// No activity recorded, which is what RocksDb labels an operation with
    /// unless something sets one.
    /// </summary>
    /// <remarks>
    /// <c>kUnknown</c>, which RocksDb keeps last in the enum for exactly this
    /// purpose. Without it the default was not expressible: reading
    /// <see cref="ReadOptions.IoActivity"/> on fresh options returned a value no
    /// member matched, so <c>Enum.IsDefined</c> said false and <c>ToString</c>
    /// gave a bare number, and a caller who had set an activity had no way to
    /// put it back.
    /// </remarks>
    Unknown = 0xFF,
}

/// <summary>
/// The priority an operation is given by the rate limiter, if one is configured.
/// </summary>
/// <remarks>
/// Mirrored from <c>Env::IOPriority</c> in <c>include/rocksdb/env.h</c> because
/// the C API declares this parameter as a plain <c>int</c>.
/// </remarks>
public enum RateLimiterPriority
{
    /// <summary>Low priority, yielding to everything else. The default for background work.</summary>
    Low = 0,

    /// <summary>Between <see cref="Low"/> and <see cref="High"/>.</summary>
    Mid = 1,

    /// <summary>High priority.</summary>
    High = 2,

    /// <summary>User-facing work, which the rate limiter should not delay.</summary>
    User = 3,

    /// <summary>
    /// Do not charge the rate limiter for this operation. The default.
    /// </summary>
    /// <remarks>
    /// The name is RocksDb's: <c>Env::IO_TOTAL</c> is the count of real
    /// priorities, and RocksDb reuses it as the "no rate limiting" value rather
    /// than declaring a separate one. <c>ReadOptions::rate_limiter_priority</c>
    /// is declared as <c>= Env::IO_TOTAL</c> with the comment that the special
    /// value disables charging the rate limiter, so this is both the default and
    /// a meaningful thing to pass. This used to be documented as a programming
    /// error to use, which had it exactly backwards.
    /// </remarks>
    Total = 4,
}

/// <summary>
/// Which tiers of storage a read is allowed to reach into.
/// </summary>
/// <remarks>
/// Mirrored from <c>include/rocksdb/options.h</c> because the C API declares
/// this parameter as a plain <c>int</c>.
/// </remarks>
public enum ReadTier
{
    /// <summary>
    /// Memtable, block cache, operating system cache or storage. The default,
    /// and the only tier that always answers.
    /// </summary>
    All = 0,

    /// <summary>Memtable or block cache only, so the read never touches storage.</summary>
    BlockCache = 1,

    /// <summary>
    /// Persisted data only. With the write-ahead log disabled this also skips
    /// the memtable. RocksDb supports this for point lookups only, not iterators.
    /// </summary>
    Persisted = 2,

    /// <summary>Memtable only, for memtable-only iterators.</summary>
    Memtable = 3,
}

/// <summary>
/// Whether a manual compaction should collect blob-file garbage.
/// </summary>
/// <remarks>
/// Mirrored from <c>include/rocksdb/options.h</c> because the C API declares
/// this parameter as a plain <c>int</c>.
/// </remarks>
public enum BlobGarbageCollectionPolicy
{
    /// <summary>Collect blob-file garbage regardless of the column family setting.</summary>
    Force = 0,

    /// <summary>Skip blob-file garbage collection regardless of the column family setting.</summary>
    Disable = 1,

    /// <summary>Follow the column family's own setting. This is the default.</summary>
    UseDefault = 2,
}
