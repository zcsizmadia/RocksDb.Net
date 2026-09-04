namespace RocksDbNet;

/// <summary>
/// A counter available from a <see cref="PerfContext"/>, mapped from the
/// metric enumeration in RocksDb's <c>c.h</c>.
/// </summary>
/// <remarks>
/// The values are positional in the native header, so they must match it
/// exactly. Which counters are populated depends on the
/// <see cref="PerfLevel"/> in force: counts are collected by default, while
/// anything time-related needs a higher level.
/// </remarks>
public enum PerfMetric
{
    /// <summary>Maps to <c>rocksdb_user_key_comparison_count</c>.</summary>
    UserKeyComparisonCount = 0,

    /// <summary>Maps to <c>rocksdb_block_cache_hit_count</c>.</summary>
    BlockCacheHitCount = 1,

    /// <summary>Maps to <c>rocksdb_block_read_count</c>.</summary>
    BlockReadCount = 2,

    /// <summary>Maps to <c>rocksdb_block_read_byte</c>.</summary>
    BlockReadByte = 3,

    /// <summary>Maps to <c>rocksdb_block_read_time</c>.</summary>
    BlockReadTime = 4,

    /// <summary>Maps to <c>rocksdb_block_checksum_time</c>.</summary>
    BlockChecksumTime = 5,

    /// <summary>Maps to <c>rocksdb_block_decompress_time</c>.</summary>
    BlockDecompressTime = 6,

    /// <summary>Maps to <c>rocksdb_get_read_bytes</c>.</summary>
    GetReadBytes = 7,

    /// <summary>Maps to <c>rocksdb_multiget_read_bytes</c>.</summary>
    MultiGetReadBytes = 8,

    /// <summary>Maps to <c>rocksdb_iter_read_bytes</c>.</summary>
    IterReadBytes = 9,

    /// <summary>Maps to <c>rocksdb_internal_key_skipped_count</c>.</summary>
    InternalKeySkippedCount = 10,

    /// <summary>Maps to <c>rocksdb_internal_delete_skipped_count</c>.</summary>
    InternalDeleteSkippedCount = 11,

    /// <summary>Maps to <c>rocksdb_internal_recent_skipped_count</c>.</summary>
    InternalRecentSkippedCount = 12,

    /// <summary>Maps to <c>rocksdb_internal_merge_count</c>.</summary>
    InternalMergeCount = 13,

    /// <summary>Maps to <c>rocksdb_get_snapshot_time</c>.</summary>
    GetSnapshotTime = 14,

    /// <summary>Maps to <c>rocksdb_get_from_memtable_time</c>.</summary>
    GetFromMemtableTime = 15,

    /// <summary>Maps to <c>rocksdb_get_from_memtable_count</c>.</summary>
    GetFromMemtableCount = 16,

    /// <summary>Maps to <c>rocksdb_get_post_process_time</c>.</summary>
    GetPostProcessTime = 17,

    /// <summary>Maps to <c>rocksdb_get_from_output_files_time</c>.</summary>
    GetFromOutputFilesTime = 18,

    /// <summary>Maps to <c>rocksdb_seek_on_memtable_time</c>.</summary>
    SeekOnMemtableTime = 19,

    /// <summary>Maps to <c>rocksdb_seek_on_memtable_count</c>.</summary>
    SeekOnMemtableCount = 20,

    /// <summary>Maps to <c>rocksdb_next_on_memtable_count</c>.</summary>
    NextOnMemtableCount = 21,

    /// <summary>Maps to <c>rocksdb_prev_on_memtable_count</c>.</summary>
    PrevOnMemtableCount = 22,

    /// <summary>Maps to <c>rocksdb_seek_child_seek_time</c>.</summary>
    SeekChildSeekTime = 23,

    /// <summary>Maps to <c>rocksdb_seek_child_seek_count</c>.</summary>
    SeekChildSeekCount = 24,

    /// <summary>Maps to <c>rocksdb_seek_min_heap_time</c>.</summary>
    SeekMinHeapTime = 25,

    /// <summary>Maps to <c>rocksdb_seek_max_heap_time</c>.</summary>
    SeekMaxHeapTime = 26,

    /// <summary>Maps to <c>rocksdb_seek_internal_seek_time</c>.</summary>
    SeekInternalSeekTime = 27,

    /// <summary>Maps to <c>rocksdb_find_next_user_entry_time</c>.</summary>
    FindNextUserEntryTime = 28,

    /// <summary>Maps to <c>rocksdb_write_wal_time</c>.</summary>
    WriteWalTime = 29,

    /// <summary>Maps to <c>rocksdb_write_memtable_time</c>.</summary>
    WriteMemtableTime = 30,

    /// <summary>Maps to <c>rocksdb_write_delay_time</c>.</summary>
    WriteDelayTime = 31,

    /// <summary>Maps to <c>rocksdb_write_pre_and_post_process_time</c>.</summary>
    WritePreAndPostProcessTime = 32,

    /// <summary>Maps to <c>rocksdb_db_mutex_lock_nanos</c>.</summary>
    DbMutexLockNanos = 33,

    /// <summary>Maps to <c>rocksdb_db_condition_wait_nanos</c>.</summary>
    DbConditionWaitNanos = 34,

    /// <summary>Maps to <c>rocksdb_merge_operator_time_nanos</c>.</summary>
    MergeOperatorTimeNanos = 35,

    /// <summary>Maps to <c>rocksdb_read_index_block_nanos</c>.</summary>
    ReadIndexBlockNanos = 36,

    /// <summary>Maps to <c>rocksdb_read_filter_block_nanos</c>.</summary>
    ReadFilterBlockNanos = 37,

    /// <summary>Maps to <c>rocksdb_new_table_block_iter_nanos</c>.</summary>
    NewTableBlockIterNanos = 38,

    /// <summary>Maps to <c>rocksdb_new_table_iterator_nanos</c>.</summary>
    NewTableIteratorNanos = 39,

    /// <summary>Maps to <c>rocksdb_block_seek_nanos</c>.</summary>
    BlockSeekNanos = 40,

    /// <summary>Maps to <c>rocksdb_find_table_nanos</c>.</summary>
    FindTableNanos = 41,

    /// <summary>Maps to <c>rocksdb_bloom_memtable_hit_count</c>.</summary>
    BloomMemtableHitCount = 42,

    /// <summary>Maps to <c>rocksdb_bloom_memtable_miss_count</c>.</summary>
    BloomMemtableMissCount = 43,

    /// <summary>Maps to <c>rocksdb_bloom_sst_hit_count</c>.</summary>
    BloomSstHitCount = 44,

    /// <summary>Maps to <c>rocksdb_bloom_sst_miss_count</c>.</summary>
    BloomSstMissCount = 45,

    /// <summary>Maps to <c>rocksdb_key_lock_wait_time</c>.</summary>
    KeyLockWaitTime = 46,

    /// <summary>Maps to <c>rocksdb_key_lock_wait_count</c>.</summary>
    KeyLockWaitCount = 47,

    /// <summary>Maps to <c>rocksdb_env_new_sequential_file_nanos</c>.</summary>
    EnvNewSequentialFileNanos = 48,

    /// <summary>Maps to <c>rocksdb_env_new_random_access_file_nanos</c>.</summary>
    EnvNewRandomAccessFileNanos = 49,

    /// <summary>Maps to <c>rocksdb_env_new_writable_file_nanos</c>.</summary>
    EnvNewWritableFileNanos = 50,

    /// <summary>Maps to <c>rocksdb_env_reuse_writable_file_nanos</c>.</summary>
    EnvReuseWritableFileNanos = 51,

    /// <summary>Maps to <c>rocksdb_env_new_random_rw_file_nanos</c>.</summary>
    EnvNewRandomRwFileNanos = 52,

    /// <summary>Maps to <c>rocksdb_env_new_directory_nanos</c>.</summary>
    EnvNewDirectoryNanos = 53,

    /// <summary>Maps to <c>rocksdb_env_file_exists_nanos</c>.</summary>
    EnvFileExistsNanos = 54,

    /// <summary>Maps to <c>rocksdb_env_get_children_nanos</c>.</summary>
    EnvGetChildrenNanos = 55,

    /// <summary>Maps to <c>rocksdb_env_get_children_file_attributes_nanos</c>.</summary>
    EnvGetChildrenFileAttributesNanos = 56,

    /// <summary>Maps to <c>rocksdb_env_delete_file_nanos</c>.</summary>
    EnvDeleteFileNanos = 57,

    /// <summary>Maps to <c>rocksdb_env_create_dir_nanos</c>.</summary>
    EnvCreateDirNanos = 58,

    /// <summary>Maps to <c>rocksdb_env_create_dir_if_missing_nanos</c>.</summary>
    EnvCreateDirIfMissingNanos = 59,

    /// <summary>Maps to <c>rocksdb_env_delete_dir_nanos</c>.</summary>
    EnvDeleteDirNanos = 60,

    /// <summary>Maps to <c>rocksdb_env_get_file_size_nanos</c>.</summary>
    EnvGetFileSizeNanos = 61,

    /// <summary>Maps to <c>rocksdb_env_get_file_modification_time_nanos</c>.</summary>
    EnvGetFileModificationTimeNanos = 62,

    /// <summary>Maps to <c>rocksdb_env_rename_file_nanos</c>.</summary>
    EnvRenameFileNanos = 63,

    /// <summary>Maps to <c>rocksdb_env_link_file_nanos</c>.</summary>
    EnvLinkFileNanos = 64,

    /// <summary>Maps to <c>rocksdb_env_lock_file_nanos</c>.</summary>
    EnvLockFileNanos = 65,

    /// <summary>Maps to <c>rocksdb_env_unlock_file_nanos</c>.</summary>
    EnvUnlockFileNanos = 66,

    /// <summary>Maps to <c>rocksdb_env_new_logger_nanos</c>.</summary>
    EnvNewLoggerNanos = 67,

    /// <summary>Maps to <c>rocksdb_number_async_seek</c>.</summary>
    NumberAsyncSeek = 68,

    /// <summary>Maps to <c>rocksdb_blob_cache_hit_count</c>.</summary>
    BlobCacheHitCount = 69,

    /// <summary>Maps to <c>rocksdb_blob_read_count</c>.</summary>
    BlobReadCount = 70,

    /// <summary>Maps to <c>rocksdb_blob_read_byte</c>.</summary>
    BlobReadByte = 71,

    /// <summary>Maps to <c>rocksdb_blob_read_time</c>.</summary>
    BlobReadTime = 72,

    /// <summary>Maps to <c>rocksdb_blob_checksum_time</c>.</summary>
    BlobChecksumTime = 73,

    /// <summary>Maps to <c>rocksdb_blob_decompress_time</c>.</summary>
    BlobDecompressTime = 74,

    /// <summary>Maps to <c>rocksdb_internal_range_del_reseek_count</c>.</summary>
    InternalRangeDelReseekCount = 75,

    /// <summary>Maps to <c>rocksdb_block_read_cpu_time</c>.</summary>
    BlockReadCpuTime = 76,

    /// <summary>Maps to <c>rocksdb_internal_merge_point_lookup_count</c>.</summary>
    InternalMergePointLookupCount = 77,

    /// <summary>Maps to <c>rocksdb_data_block_read_byte</c>.</summary>
    DataBlockReadByte = 78,

    /// <summary>Maps to <c>rocksdb_index_block_read_byte</c>.</summary>
    IndexBlockReadByte = 79,

    /// <summary>Maps to <c>rocksdb_filter_block_read_byte</c>.</summary>
    FilterBlockReadByte = 80,

    /// <summary>Maps to <c>rocksdb_compression_dict_block_read_byte</c>.</summary>
    CompressionDictBlockReadByte = 81,

    /// <summary>Maps to <c>rocksdb_metadata_block_read_byte</c>.</summary>
    MetadataBlockReadByte = 82,

    // rocksdb_blob_cache_read_byte, 83, is deliberately absent. RocksDb names
    // the metric but its C accessor has no case for it, so it could only ever
    // read back as zero. It was the last value, so nothing after it shifts.
}
