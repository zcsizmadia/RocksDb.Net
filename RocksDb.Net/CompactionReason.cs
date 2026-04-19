namespace RocksDbNet;

/// <summary>
/// Mapped from rocksdb::CompactionReason in options.h
/// </summary>
public enum CompactionReason : uint
{
    Unknown = 0,
    /// <summary> [Level] Number of L0 files > level0_file_num_compaction_trigger </summary>
    LevelL0FilesNum = 1,
    /// <summary> [Level] Total size of level > MaxBytesForLevel </summary>
    LevelMaxLevelSize = 2,
    /// <summary> [Universal] Number of files > level0_file_num_compaction_trigger </summary>
    UniversalSizeEnumeration = 3,
    /// <summary> [Universal] Size amplification > max_size_amplification_percent </summary>
    UniversalSizeAmplification = 4,
    /// <summary> [Universal] min_merge_width..max_merge_width files can be merged </summary>
    UniversalSizeRatio = 5,
    /// <summary> [FIFO] Total size > max_table_files_size </summary>
    FIFOMaxSize = 6,
    /// <summary> [FIFO] At least one file has expired based on TTL </summary>
    FIFOTtl = 7,
    /// <summary> [FIFO] Files with old data are being compacted </summary>
    FIFOFillCache = 8,
    /// <summary> [Manual] DB::CompactRange() was called </summary>
    ExternalSstIngestion = 9,
    /// <summary> DB::CompactRange() or DB::CompactFiles() was called </summary>
    ManualCompaction = 10,
    /// <summary> db_options.periodic_compaction_seconds is exceeded </summary>
    FilesMarkedForCompaction = 11,
    /// <summary> [Bottommost] Compaction to level output_level was completed </summary>
    BottommostLevel = 12,
    /// <summary> Ttl reached or other criteria in TtlCompactionFilter </summary>
    Ttl = 13,
    /// <summary> Periodic compaction </summary>
    Flush = 14,
    /// <summary> External sst ingestion </summary>
    ExternalSstIngestionJob = 15,
    /// <summary> Range deletion tombstone overflow </summary>
    PeriodicCompaction = 16,
    /// <summary> Change in internal stats (e.g. deletion ratio) </summary>
    ChangeLevel = 17,
    /// <summary> Compaction forced by TTL </summary>
    ForcedTtl = 18,
}