namespace RocksDbNet;

/// <summary>
/// How RocksDb picks which file to compact next within a level, mapped from
/// <c>rocksdb::CompactionPri</c> in <c>advanced_options.h</c>.
/// </summary>
/// <remarks>Only meaningful for level-style compaction.</remarks>
public enum CompactionPri
{
    /// <summary>
    /// Prefer the file with the largest size, adjusted for how much of it is
    /// deletions.
    /// </summary>
    ByCompensatedSize = 0,

    /// <summary>
    /// Prefer the file whose newest data is oldest, which suits workloads that
    /// write in key order and then stop touching old keys.
    /// </summary>
    OldestLargestSeqFirst = 1,

    /// <summary>
    /// Prefer the file whose oldest data is oldest, which suits uniformly random
    /// updates.
    /// </summary>
    OldestSmallestSeqFirst = 2,

    /// <summary>
    /// Prefer the file whose compaction would rewrite the least data from the
    /// next level. RocksDb's default, and usually the cheapest.
    /// </summary>
    MinOverlappingRatio = 3,

    /// <summary>
    /// Work through the key space in order, so that every range is compacted
    /// eventually rather than only the hot ones.
    /// </summary>
    RoundRobin = 4,
}

/// <summary>
/// Whether newly written blobs are put straight into the blob cache, mapped
/// from <c>rocksdb::PrepopulateBlobCache</c> in <c>advanced_options.h</c>.
/// </summary>
public enum PrepopulateBlobCache
{
    /// <summary>Do not prepopulate. The default.</summary>
    Disable = 0,

    /// <summary>Prepopulate blobs written by a flush, but not by a compaction.</summary>
    FlushOnly = 1,
}

/// <summary>
/// How much RocksDb collects when statistics are enabled, mapped from
/// <c>rocksdb::StatsLevel</c> in <c>statistics.h</c>.
/// </summary>
/// <remarks>
/// Higher levels cost more. The timing levels in particular add measurable
/// overhead to every operation, so they are for investigation rather than
/// steady state.
/// </remarks>
public enum StatsLevel
{
    /// <summary>
    /// Collect nothing. Also spelled <c>kExceptTickers</c> natively, which is
    /// the same value.
    /// </summary>
    DisableAll = 0,

    /// <summary>Collect counters but no histograms or timers.</summary>
    ExceptHistogramOrTimers = 1,

    /// <summary>Collect counters and histograms but no timers.</summary>
    ExceptTimers = 2,

    /// <summary>Collect everything except the detailed timers.</summary>
    ExceptDetailedTimers = 3,

    /// <summary>Collect everything except time spent on mutexes.</summary>
    ExceptTimeForMutex = 4,

    /// <summary>Collect everything, including mutex timing.</summary>
    All = 5,
}
