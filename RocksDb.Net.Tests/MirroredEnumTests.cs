namespace RocksDbNet.Tests;

/// <summary>
/// Asserts that an enum has exactly the expected members, with the expected
/// values and no extras.
/// </summary>
/// <remarks>
/// Shared with <see cref="NativeEnumValueTests"/>, which pins the four
/// event-listener enums. The check is deliberately in one place: an enum pinned
/// by a weaker rule than its neighbours is the gap that lets a value drift.
/// </remarks>
internal static class NativeEnum
{
    public static void AssertExactly<TEnum>(params (string Name, int Value)[] expected)
        where TEnum : struct, Enum
    {
        foreach ((string name, int value) in expected)
        {
            Assert.True(Enum.IsDefined(typeof(TEnum), name), $"{typeof(TEnum).Name}.{name} is missing");
            Assert.Equal(value, Convert.ToInt32(Enum.Parse<TEnum>(name)));
        }

        // No members beyond the native set. An extra one is a value some future
        // RocksDb release will claim for something else.
        Assert.Equal(
            expected.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal),
            Enum.GetNames<TEnum>().OrderBy(n => n, StringComparer.Ordinal));
    }
}

/// <summary>
/// Pins every enum the wrapper mirrors by hand against the RocksDb 11.8.1
/// numbers it is mirroring.
/// </summary>
/// <remarks>
/// <para>
/// Before this file, four enums were pinned against the native numbers and the
/// remaining two dozen were asserted only against the wrapper's own values —
/// tests that restate the declaration and cannot fail. That is how
/// <c>RateLimiterPriority.Total</c> and the missing <c>IoActivity.Unknown</c>
/// survived until someone read the headers by hand.
/// </para>
/// <para>
/// <strong>Which source is authoritative differs per enum, and getting that
/// wrong is its own defect.</strong> Where <c>c.h</c> declares named constants,
/// those are what the wrapper's integers are passed to and they win — the C API
/// sometimes exposes fewer members than C++ does, and mirroring the wider C++
/// set would invent values the C API will not accept. <c>IndexType</c> is the
/// live example: <c>table.h</c> has four members, <c>c.h</c> stops at three, and
/// the wrapper correctly stops at three. Where the C API takes a plain
/// <c>int</c>, nothing in <c>c.h</c> constrains the value and the C++ header is
/// the only source.
/// </para>
/// <para>
/// <c>PerfLevel</c> is the case that proves the rule matters in both directions:
/// <c>c.h</c> carries a stale six-member block missing <c>kEnableWait</c> and
/// <c>kEnableTimeAndCPUTimeExceptForMutex</c>, while the value is really cast to
/// the eight-member <c>PerfLevel</c> in <c>perf_level.h</c>. The wrapper follows
/// the C++ header, so pinning it to <c>c.h</c> would fail a correct wrapper.
/// </para>
/// <para>
/// Sentinels are excluded throughout: <c>kLastTemperature</c>,
/// <c>NUM_INFO_LOG_LEVELS</c>, <c>kWalProcessingOptionMax</c>,
/// <c>kOutOfBounds</c> and <c>kUninitialized</c> are not values a caller may
/// pass, and a member for one would be a value a later release claims.
/// </para>
/// </remarks>
public class MirroredEnumTests
{
    // ── Pinned against the named constants in c.h ────────────────────────────
    //
    // The wrapper passes these integers straight to a C API that declares what
    // they mean, so the C API is the contract.

    /// <summary>From the <c>rocksdb_wal_file_type_*</c> block in <c>c.h</c>.</summary>
    [Fact]
    public void WalFileType_MatchesTheCApi()
        => NativeEnum.AssertExactly<WalFileType>(
            ("ArchivedLog", 0),
            ("AliveLog", 1));

    /// <summary>
    /// From the <c>rocksdb_block_based_table_index_type_*</c> block in
    /// <c>c.h</c>, which stops at three. <c>table.h</c> also declares
    /// <c>kBinarySearchWithFirstKey = 3</c>, deliberately not mirrored: the C
    /// API does not name it, so it is not reachable through this wrapper.
    /// </summary>
    [Fact]
    public void BlockBasedTableIndexType_MatchesTheCApi()
        => NativeEnum.AssertExactly<BlockBasedTableIndexType>(
            ("BinarySearch", 0),
            ("HashSearch", 1),
            ("TwoLevelIndexSearch", 2));

    /// <summary>From the <c>rocksdb_block_based_table_data_block_index_type_*</c> block.</summary>
    [Fact]
    public void DataBlockIndexType_MatchesTheCApi()
        => NativeEnum.AssertExactly<DataBlockIndexType>(
            ("BinarySearch", 0),
            ("BinarySearchAndHash", 1));

    /// <summary>From the <c>rocksdb_block_based_table_index_block_search_type_*</c> block.</summary>
    [Fact]
    public void IndexBlockSearchType_MatchesTheCApi()
        => NativeEnum.AssertExactly<IndexBlockSearchType>(
            ("Binary", 0),
            ("Interpolation", 1),
            ("Auto", 2));

    /// <summary>From the <c>rocksdb_block_based_k_*_pinning_tier</c> block.</summary>
    [Fact]
    public void PinningTier_MatchesTheCApi()
        => NativeEnum.AssertExactly<PinningTier>(
            ("Fallback", 0),
            ("None", 1),
            ("FlushedAndSimilar", 2),
            ("All", 3));

    /// <summary>
    /// From the <c>rocksdb_statistics_level_*</c> block, which agrees with
    /// <c>statistics.h</c>. Native also declares <c>kExceptTickers</c> as an
    /// alias of <c>kDisableAll</c>; an alias is not a distinct value, so the
    /// wrapper names it once.
    /// </summary>
    [Fact]
    public void StatsLevel_MatchesTheCApi()
        => NativeEnum.AssertExactly<StatsLevel>(
            ("DisableAll", 0),
            ("ExceptHistogramOrTimers", 1),
            ("ExceptTimers", 2),
            ("ExceptDetailedTimers", 3),
            ("ExceptTimeForMutex", 4),
            ("All", 5));

    /// <summary>From the <c>rocksdb_prepopulate_blob_*</c> block.</summary>
    [Fact]
    public void PrepopulateBlobCache_MatchesTheCApi()
        => NativeEnum.AssertExactly<PrepopulateBlobCache>(
            ("Disable", 0),
            ("FlushOnly", 1));

    /// <summary>From the <c>rocksdb_*_recovery</c> block, which agrees with <c>options.h</c>.</summary>
    [Fact]
    public void WalRecoveryMode_MatchesTheCApi()
        => NativeEnum.AssertExactly<WalRecoveryMode>(
            ("TolerateCorruptedTailRecords", 0),
            ("AbsoluteConsistency", 1),
            ("PointInTime", 2),
            ("SkipAnyCorruptedRecords", 3));

    /// <summary>
    /// From the <c>rocksdb_*_compaction</c> block, which stops at three.
    /// <c>advanced_options.h</c> also declares <c>kCompactionStyleNone = 3</c>,
    /// which the C API does not name.
    /// </summary>
    [Fact]
    public void CompactionStyle_MatchesTheCApi()
        => NativeEnum.AssertExactly<CompactionStyle>(
            ("Level", 0),
            ("Universal", 1),
            ("Fifo", 2));

    /// <summary>From the <c>rocksdb_k_*_compaction_pri</c> block.</summary>
    [Fact]
    public void CompactionPri_MatchesTheCApi()
        => NativeEnum.AssertExactly<CompactionPri>(
            ("ByCompensatedSize", 0),
            ("OldestLargestSeqFirst", 1),
            ("OldestSmallestSeqFirst", 2),
            ("MinOverlappingRatio", 3),
            ("RoundRobin", 4));

    /// <summary>
    /// From the <c>rocksdb_wal_filter_*</c> block. Native's
    /// <c>kWalProcessingOptionMax</c> is a bound, not an option.
    /// </summary>
    [Fact]
    public void WalProcessingOption_MatchesTheCApi()
        => NativeEnum.AssertExactly<WalProcessingOption>(
            ("ContinueProcessing", 0),
            ("IgnoreCurrentRecord", 1),
            ("StopReplay", 2),
            ("CorruptedRecord", 3));

    /// <summary>From the <c>rocksdb_*_compaction_stop_style</c> block.</summary>
    [Fact]
    public void CompactionStopStyle_MatchesTheCApi()
        => NativeEnum.AssertExactly<CompactionStopStyle>(
            ("SimilarSize", 0),
            ("TotalSize", 1));

    /// <summary>From the <c>rocksdb_txndb_write_policy_*</c> block.</summary>
    [Fact]
    public void TransactionDbWritePolicy_MatchesTheCApi()
        => NativeEnum.AssertExactly<TransactionDbWritePolicy>(
            ("WriteCommitted", 0),
            ("WritePrepared", 1),
            ("WriteUnprepared", 2));

    /// <summary>
    /// From the <c>rocksdb_*_compression</c> block, plus
    /// <c>kDisableCompressionOption = 0xff</c> from
    /// <c>compression_type.h</c>, which the C API does not name but
    /// bottommost compression needs in order to mean "inherit".
    /// </summary>
    /// <remarks>
    /// Native reserves <c>0x80</c>-<c>0xFE</c> for custom compressors, which the
    /// wrapper does not mirror because it exposes no way to register one. The
    /// exhaustive check below is what keeps a member from appearing in that
    /// range by accident.
    /// </remarks>
    [Fact]
    public void Compression_MatchesTheCApi()
        => NativeEnum.AssertExactly<Compression>(
            ("None", 0),
            ("Snappy", 1),
            ("Zlib", 2),
            ("Bz2", 3),
            ("Lz4", 4),
            ("Lz4Hc", 5),
            ("Xpress", 6),
            ("Zstd", 7),
            ("Inherit", 0xff));

    // ── Pinned against the C++ headers ───────────────────────────────────────
    //
    // The C API takes these as a plain int and says nothing about what the
    // integers mean, so the C++ header is the only contract there is.

    /// <summary>
    /// From <c>include/rocksdb/types.h</c>. Deliberately non-contiguous:
    /// RocksDb reserves the gaps for tiers inserted later.
    /// </summary>
    [Fact]
    public void Temperature_MatchesTypesHeader()
        => NativeEnum.AssertExactly<Temperature>(
            ("Unknown", 0),
            ("Hot", 0x04),
            ("Warm", 0x08),
            ("Cool", 0x0A),
            ("Cold", 0x0C),
            ("Ice", 0x10));

    /// <summary>
    /// From <c>include/rocksdb/types.h</c>. Positional in the header, so
    /// inserting a member shifts everything after it.
    /// </summary>
    [Fact]
    public void FileType_MatchesTypesHeader()
        => NativeEnum.AssertExactly<FileType>(
            ("WalFile", 0),
            ("DbLockFile", 1),
            ("TableFile", 2),
            ("DescriptorFile", 3),
            ("CurrentFile", 4),
            ("TempFile", 5),
            ("InfoLogFile", 6),
            ("MetaDatabase", 7),
            ("IdentityFile", 8),
            ("OptionsFile", 9),
            ("BlobFile", 10),
            ("CompactionProgressFile", 11));

    /// <summary>From <c>include/rocksdb/advanced_options.h</c>.</summary>
    [Fact]
    public void CacheTier_MatchesAdvancedOptionsHeader()
        => NativeEnum.AssertExactly<CacheTier>(
            ("Volatile", 0),
            ("VolatileCompressed", 1),
            ("NonVolatileBlock", 2));

    /// <summary>From <c>include/rocksdb/table.h</c>.</summary>
    [Fact]
    public void ChecksumType_MatchesTableHeader()
        => NativeEnum.AssertExactly<ChecksumType>(
            ("None", 0),
            ("Crc32c", 1),
            ("XxHash", 2),
            ("XxHash64", 3),
            ("Xxh3", 4));

    /// <summary>
    /// From <c>IndexShorteningMode</c> in <c>include/rocksdb/table.h</c>.
    /// Positional in the header.
    /// </summary>
    [Fact]
    public void IndexShortening_MatchesTableHeader()
        => NativeEnum.AssertExactly<IndexShortening>(
            ("NoShortening", 0),
            ("ShortenSeparators", 1),
            ("ShortenSeparatorsAndSuccessor", 2));

    /// <summary>
    /// From <c>PrepopulateBlockCache</c> in <c>include/rocksdb/table.h</c>.
    /// Positional, and distinct from <see cref="PrepopulateBlobCache"/>, which
    /// has one member fewer.
    /// </summary>
    [Fact]
    public void PrepopulateBlockCache_MatchesTableHeader()
        => NativeEnum.AssertExactly<PrepopulateBlockCache>(
            ("Disable", 0),
            ("FlushOnly", 1),
            ("FlushAndCompaction", 2));

    /// <summary>From <c>include/rocksdb/options.h</c>. Positional.</summary>
    [Fact]
    public void BottommostLevelCompaction_MatchesOptionsHeader()
        => NativeEnum.AssertExactly<BottommostLevelCompaction>(
            ("Skip", 0),
            ("IfHaveCompactionFilter", 1),
            ("Force", 2),
            ("ForceOptimized", 3));

    /// <summary>From <c>include/rocksdb/options.h</c>.</summary>
    [Fact]
    public void ReadTier_MatchesOptionsHeader()
        => NativeEnum.AssertExactly<ReadTier>(
            ("All", 0),
            ("BlockCache", 1),
            ("Persisted", 2),
            ("Memtable", 3));

    /// <summary>From <c>include/rocksdb/options.h</c>. Positional.</summary>
    [Fact]
    public void BlobGarbageCollectionPolicy_MatchesOptionsHeader()
        => NativeEnum.AssertExactly<BlobGarbageCollectionPolicy>(
            ("Force", 0),
            ("Disable", 1),
            ("UseDefault", 2));

    /// <summary>
    /// From <c>include/rocksdb/utilities/optimistic_transaction_db.h</c>.
    /// Positional, and the C API takes it as a plain <c>int</c>.
    /// </summary>
    [Fact]
    public void OccValidationPolicy_MatchesOptimisticTransactionDbHeader()
        => NativeEnum.AssertExactly<OccValidationPolicy>(
            ("ValidateSerial", 0),
            ("ValidateParallel", 1));

    /// <summary>From <c>include/rocksdb/port_defs.h</c>.</summary>
    [Fact]
    public void CpuPriority_MatchesPortDefsHeader()
        => NativeEnum.AssertExactly<CpuPriority>(
            ("Idle", 0),
            ("Low", 1),
            ("Normal", 2),
            ("High", 3));

    /// <summary>
    /// From <c>RestoreOptions::Mode</c> in
    /// <c>include/rocksdb/utilities/backup_engine.h</c>. Note the default,
    /// <c>kPurgeAllFiles</c>, is <c>0xffff</c> rather than a small number, so a
    /// wrapper that renumbered it densely would silently select a different
    /// mode.
    /// </summary>
    [Fact]
    public void RestoreMode_MatchesBackupEngineHeader()
        => NativeEnum.AssertExactly<RestoreMode>(
            ("KeepLatestDbSessionIdFiles", 1),
            ("VerifyChecksum", 2),
            ("PurgeAllFiles", 0xFFFF));

    /// <summary>
    /// From <c>include/rocksdb/perf_level.h</c>, <em>not</em> from the stale
    /// block of the same name in <c>c.h</c>. The two disagree from the fourth
    /// member on, and the value is really cast to the C++ enum.
    /// <c>kUninitialized</c> and <c>kOutOfBounds</c> are bounds rather than
    /// levels, so they are not mirrored.
    /// </summary>
    [Fact]
    public void PerfLevel_MatchesPerfLevelHeader()
        => NativeEnum.AssertExactly<PerfLevel>(
            ("Disable", 1),
            ("EnableCount", 2),
            ("EnableWait", 3),
            ("EnableTimeExceptForMutex", 4),
            ("EnableTimeAndCpuTimeExceptForMutex", 5),
            ("EnableTime", 6));

    /// <summary>
    /// From <c>include/rocksdb/env.h</c>. Positional, and
    /// <c>NUM_INFO_LOG_LEVELS</c> is a count rather than a level.
    /// </summary>
    [Fact]
    public void InfoLogLevel_MatchesEnvHeader()
        => NativeEnum.AssertExactly<InfoLogLevel>(
            ("Debug", 0),
            ("Info", 1),
            ("Warn", 2),
            ("Error", 3),
            ("Fatal", 4),
            ("Header", 5));

    /// <summary>
    /// From <c>Env::IOPriority</c> in <c>include/rocksdb/env.h</c>.
    /// <c>IO_TOTAL</c> is a real member here rather than a sentinel — it is the
    /// value that means "not charged to any pool", and it was missing from the
    /// wrapper until the headers were read by hand.
    /// </summary>
    [Fact]
    public void RateLimiterPriority_MatchesEnvHeader()
        => NativeEnum.AssertExactly<RateLimiterPriority>(
            ("Low", 0),
            ("Mid", 1),
            ("High", 2),
            ("User", 3),
            ("Total", 4));

    /// <summary>
    /// From <c>Env::IOActivity</c> in <c>include/rocksdb/env.h</c>.
    /// <c>kUnknown = 0xFF</c> is the default a fresh <see cref="ReadOptions"/>
    /// reports, and was the member whose absence made that default unnameable.
    /// </summary>
    /// <remarks>
    /// Native reserves <c>0x80</c>-<c>0xFE</c> for custom activities and names
    /// each one; the wrapper mirrors only the first as
    /// <see cref="IoActivity.FirstCustom"/>, because the rest carry no meaning
    /// the wrapper can express.
    /// </remarks>
    [Fact]
    public void IoActivity_MatchesEnvHeader()
        => NativeEnum.AssertExactly<IoActivity>(
            ("Flush", 0),
            ("Compaction", 1),
            ("DbOpen", 2),
            ("Get", 3),
            ("MultiGet", 4),
            ("DbIterator", 5),
            ("VerifyDbChecksum", 6),
            ("VerifyFileChecksums", 7),
            ("GetEntity", 8),
            ("MultiGetEntity", 9),
            ("GetFileChecksumsFromCurrentManifest", 10),
            ("FirstCustom", 0x80),
            ("Unknown", 0xFF));
}
