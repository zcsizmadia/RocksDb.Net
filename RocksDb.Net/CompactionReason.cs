namespace RocksDbNet;

/// <summary>
/// Why RocksDb ran a compaction, mapped from <c>rocksdb::CompactionReason</c> in
/// <c>listener.h</c>.
/// </summary>
/// <remarks>
/// The values are positional in the native header, so they must match it
/// exactly. They are written out here rather than left implicit, and asserted in
/// the tests, because a shifted value silently mislabels every compaction an
/// application observes.
/// </remarks>
public enum CompactionReason
{
    /// <summary>No reason recorded.</summary>
    Unknown = 0,

    /// <summary>
    /// Level: the number of level-0 files exceeded
    /// <see cref="DbOptions.Level0FileNumCompactionTrigger"/>.
    /// </summary>
    LevelL0FilesNum = 1,

    /// <summary>Level: the total size of a level exceeded its maximum.</summary>
    LevelMaxLevelSize = 2,

    /// <summary>Universal: compacting to reduce size amplification.</summary>
    UniversalSizeAmplification = 3,

    /// <summary>Universal: compacting to satisfy the size ratio.</summary>
    UniversalSizeRatio = 4,

    /// <summary>
    /// Universal: the number of sorted runs exceeded
    /// <see cref="DbOptions.Level0FileNumCompactionTrigger"/>.
    /// </summary>
    UniversalSortedRunNum = 5,

    /// <summary>FIFO: the total size exceeded the maximum table file size.</summary>
    FifoMaxSize = 6,

    /// <summary>FIFO: compacting to reduce the number of files.</summary>
    FifoReduceNumFiles = 7,

    /// <summary>FIFO: files older than the configured interval.</summary>
    FifoTtl = 8,

    /// <summary>A manual compaction, such as <see cref="RocksDb.CompactRange(CompactRangeOptions, System.ReadOnlySpan{byte}, System.ReadOnlySpan{byte})"/>.</summary>
    ManualCompaction = 9,

    /// <summary>
    /// Files marked for compaction, which is what
    /// <see cref="RocksDb.SuggestCompactRange(System.ReadOnlySpan{byte}, System.ReadOnlySpan{byte})"/>
    /// does.
    /// </summary>
    FilesMarkedForCompaction = 10,

    /// <summary>
    /// Level: an automatic compaction inside the bottommost level to clean up
    /// duplicate versions of the same user key, usually after a snapshot was
    /// released.
    /// </summary>
    BottommostFiles = 11,

    /// <summary>Compaction driven by time to live.</summary>
    Ttl = 12,

    /// <summary>A flush, which RocksDb accounts for as a level-0 compaction.</summary>
    Flush = 13,

    /// <summary>
    /// Internal only. External SST file ingestion, accounted for as a compaction
    /// so that it takes part in conflict checking.
    /// </summary>
    ExternalSstIngestion = 14,

    /// <summary>An SST file was older than the periodic compaction interval.</summary>
    PeriodicCompaction = 15,

    /// <summary>Compacting in order to move files to a different temperature.</summary>
    ChangeTemperature = 16,

    /// <summary>Scheduled to force garbage collection of blob files.</summary>
    ForcedBlobGC = 17,

    /// <summary>
    /// The round-robin policy's time-to-live compaction. Behaves like
    /// <see cref="LevelMaxLevelSize"/> but targets expired files.
    /// </summary>
    RoundRobinTtl = 18,

    /// <summary>
    /// Internal only. A level refit, accounted for as a compaction so that it
    /// takes part in conflict checking.
    /// </summary>
    RefitLevel = 19,

    /// <summary>Triggered by a high read frequency on SST files.</summary>
    ReadTriggered = 20,
}
