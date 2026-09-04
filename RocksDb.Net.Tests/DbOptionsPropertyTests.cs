namespace RocksDbNet.Tests;

/// <summary>
/// Round-trip coverage for the DbOptions properties.
/// See issue #25.
/// </summary>
/// <remarks>
/// Each test writes a non-default value and reads it back, so it exercises the
/// native setter and getter rather than a C# field. Most of these options are
/// only honoured when the database is opened, so a round trip is all that can
/// be asserted here.
/// </remarks>
public class DbOptionsPropertyTests
{
    [Fact]
    public void Allow2Pc_GetSet()
    {
        using var opts = new DbOptions();

        opts.Allow2Pc = true;
        Assert.True(opts.Allow2Pc);

        opts.Allow2Pc = false;
        Assert.False(opts.Allow2Pc);
    }

    [Fact]
    public void AllowDataInErrors_GetSet()
    {
        using var opts = new DbOptions();

        opts.AllowDataInErrors = true;
        Assert.True(opts.AllowDataInErrors);

        opts.AllowDataInErrors = false;
        Assert.False(opts.AllowDataInErrors);
    }

    [Fact]
    public void AllowFallocate_GetSet()
    {
        using var opts = new DbOptions();

        opts.AllowFallocate = true;
        Assert.True(opts.AllowFallocate);

        opts.AllowFallocate = false;
        Assert.False(opts.AllowFallocate);
    }

    [Fact]
    public void AsyncWalPrecreate_GetSet()
    {
        using var opts = new DbOptions();

        opts.AsyncWalPrecreate = true;
        Assert.True(opts.AsyncWalPrecreate);

        opts.AsyncWalPrecreate = false;
        Assert.False(opts.AsyncWalPrecreate);
    }

    [Fact]
    public void AvoidFlushDuringRecovery_GetSet()
    {
        using var opts = new DbOptions();

        opts.AvoidFlushDuringRecovery = true;
        Assert.True(opts.AvoidFlushDuringRecovery);

        opts.AvoidFlushDuringRecovery = false;
        Assert.False(opts.AvoidFlushDuringRecovery);
    }

    [Fact]
    public void AvoidFlushDuringShutdown_GetSet()
    {
        using var opts = new DbOptions();

        opts.AvoidFlushDuringShutdown = true;
        Assert.True(opts.AvoidFlushDuringShutdown);

        opts.AvoidFlushDuringShutdown = false;
        Assert.False(opts.AvoidFlushDuringShutdown);
    }

    [Fact]
    public void BackgroundCloseInactiveWals_GetSet()
    {
        using var opts = new DbOptions();

        opts.BackgroundCloseInactiveWals = true;
        Assert.True(opts.BackgroundCloseInactiveWals);

        opts.BackgroundCloseInactiveWals = false;
        Assert.False(opts.BackgroundCloseInactiveWals);
    }

    [Fact]
    public void BestEffortsRecovery_GetSet()
    {
        using var opts = new DbOptions();

        opts.BestEffortsRecovery = true;
        Assert.True(opts.BestEffortsRecovery);

        opts.BestEffortsRecovery = false;
        Assert.False(opts.BestEffortsRecovery);
    }

    [Fact]
    public void BgErrorResumeRetryInterval_GetSet()
    {
        using var opts = new DbOptions();

        opts.BgErrorResumeRetryInterval = 2000000UL;
        Assert.Equal(2000000UL, opts.BgErrorResumeRetryInterval);
    }

    [Fact]
    public void BlobDirectWritePartitions_GetSet()
    {
        using var opts = new DbOptions();

        opts.BlobDirectWritePartitions = 4U;
        Assert.Equal(4U, opts.BlobDirectWritePartitions);
    }

    [Fact]
    public void BlockProtectionBytesPerKey_GetSet()
    {
        using var opts = new DbOptions();

        opts.BlockProtectionBytesPerKey = 8;
        Assert.Equal((byte)8, opts.BlockProtectionBytesPerKey);
    }

    [Fact]
    public void BottommostFileCompactionDelay_GetSet()
    {
        using var opts = new DbOptions();

        opts.BottommostFileCompactionDelay = 3600U;
        Assert.Equal(3600U, opts.BottommostFileCompactionDelay);
    }

    [Fact]
    public void CfAllowIngestBehind_GetSet()
    {
        using var opts = new DbOptions();

        opts.CfAllowIngestBehind = true;
        Assert.True(opts.CfAllowIngestBehind);

        opts.CfAllowIngestBehind = false;
        Assert.False(opts.CfAllowIngestBehind);
    }

    [Fact]
    public void CompactionVerifyRecordCount_GetSet()
    {
        using var opts = new DbOptions();

        opts.CompactionVerifyRecordCount = true;
        Assert.True(opts.CompactionVerifyRecordCount);

        opts.CompactionVerifyRecordCount = false;
        Assert.False(opts.CompactionVerifyRecordCount);
    }

    [Fact]
    public void DelayedWriteRate_GetSet()
    {
        using var opts = new DbOptions();

        opts.DelayedWriteRate = 16777216UL;
        Assert.Equal(16777216UL, opts.DelayedWriteRate);
    }

    [Fact]
    public void DisallowMemtableWrites_GetSet()
    {
        using var opts = new DbOptions();

        opts.DisallowMemtableWrites = true;
        Assert.True(opts.DisallowMemtableWrites);

        opts.DisallowMemtableWrites = false;
        Assert.False(opts.DisallowMemtableWrites);
    }

    [Fact]
    public void EnableBlobDirectWrite_GetSet()
    {
        using var opts = new DbOptions();

        opts.EnableBlobDirectWrite = true;
        Assert.True(opts.EnableBlobDirectWrite);

        opts.EnableBlobDirectWrite = false;
        Assert.False(opts.EnableBlobDirectWrite);
    }

    [Fact]
    public void EnableThreadTracking_GetSet()
    {
        using var opts = new DbOptions();

        opts.EnableThreadTracking = true;
        Assert.True(opts.EnableThreadTracking);

        opts.EnableThreadTracking = false;
        Assert.False(opts.EnableThreadTracking);
    }

    [Fact]
    public void EnforceSingleDelContracts_GetSet()
    {
        using var opts = new DbOptions();

        opts.EnforceSingleDelContracts = true;
        Assert.True(opts.EnforceSingleDelContracts);

        opts.EnforceSingleDelContracts = false;
        Assert.False(opts.EnforceSingleDelContracts);
    }

    [Fact]
    public void EnforceWriteBufferManagerDuringRecovery_GetSet()
    {
        using var opts = new DbOptions();

        opts.EnforceWriteBufferManagerDuringRecovery = true;
        Assert.True(opts.EnforceWriteBufferManagerDuringRecovery);

        opts.EnforceWriteBufferManagerDuringRecovery = false;
        Assert.False(opts.EnforceWriteBufferManagerDuringRecovery);
    }

    [Fact]
    public void FastSstOpen_GetSet()
    {
        using var opts = new DbOptions();

        opts.FastSstOpen = true;
        Assert.True(opts.FastSstOpen);

        opts.FastSstOpen = false;
        Assert.False(opts.FastSstOpen);
    }

    [Fact]
    public void FlushVerifyMemtableCount_GetSet()
    {
        using var opts = new DbOptions();

        opts.FlushVerifyMemtableCount = true;
        Assert.True(opts.FlushVerifyMemtableCount);

        opts.FlushVerifyMemtableCount = false;
        Assert.False(opts.FlushVerifyMemtableCount);
    }

    [Fact]
    public void FollowerCatchupRetryCount_GetSet()
    {
        using var opts = new DbOptions();

        opts.FollowerCatchupRetryCount = 7UL;
        Assert.Equal(7UL, opts.FollowerCatchupRetryCount);
    }

    [Fact]
    public void FollowerCatchupRetryWaitMs_GetSet()
    {
        using var opts = new DbOptions();

        opts.FollowerCatchupRetryWaitMs = 250UL;
        Assert.Equal(250UL, opts.FollowerCatchupRetryWaitMs);
    }

    [Fact]
    public void FollowerRefreshCatchupPeriodMs_GetSet()
    {
        using var opts = new DbOptions();

        opts.FollowerRefreshCatchupPeriodMs = 5000UL;
        Assert.Equal(5000UL, opts.FollowerRefreshCatchupPeriodMs);
    }

    [Fact]
    public void ForceConsistencyChecks_GetSet()
    {
        using var opts = new DbOptions();

        opts.ForceConsistencyChecks = true;
        Assert.True(opts.ForceConsistencyChecks);

        opts.ForceConsistencyChecks = false;
        Assert.False(opts.ForceConsistencyChecks);
    }

    [Fact]
    public void LogReadaheadSize_GetSet()
    {
        using var opts = new DbOptions();

        opts.LogReadaheadSize = 65536UL;
        Assert.Equal(65536UL, opts.LogReadaheadSize);
    }

    [Fact]
    public void MaxBgErrorResumeCount_GetSet()
    {
        using var opts = new DbOptions();

        opts.MaxBgErrorResumeCount = 5;
        Assert.Equal(5, opts.MaxBgErrorResumeCount);
    }

    [Fact]
    public void MaxCompactionTriggerWakeupSeconds_GetSet()
    {
        using var opts = new DbOptions();

        opts.MaxCompactionTriggerWakeupSeconds = 30UL;
        Assert.Equal(30UL, opts.MaxCompactionTriggerWakeupSeconds);
    }

    [Fact]
    public void MaxManifestSpaceAmpPct_GetSet()
    {
        using var opts = new DbOptions();

        opts.MaxManifestSpaceAmpPct = 150;
        Assert.Equal(150, opts.MaxManifestSpaceAmpPct);
    }

    [Fact]
    public void MaxWriteBatchGroupSizeBytes_GetSet()
    {
        using var opts = new DbOptions();

        opts.MaxWriteBatchGroupSizeBytes = 2097152UL;
        Assert.Equal(2097152UL, opts.MaxWriteBatchGroupSizeBytes);
    }

    [Fact]
    public void MemtableBatchLookupOptimization_GetSet()
    {
        using var opts = new DbOptions();

        opts.MemtableBatchLookupOptimization = true;
        Assert.True(opts.MemtableBatchLookupOptimization);

        opts.MemtableBatchLookupOptimization = false;
        Assert.False(opts.MemtableBatchLookupOptimization);
    }

    [Fact]
    public void MemtableMaxRangeDeletions_GetSet()
    {
        using var opts = new DbOptions();

        opts.MemtableMaxRangeDeletions = 1000U;
        Assert.Equal(1000U, opts.MemtableMaxRangeDeletions);
    }

    [Fact]
    public void MemtableProtectionBytesPerKey_GetSet()
    {
        using var opts = new DbOptions();

        opts.MemtableProtectionBytesPerKey = 8U;
        Assert.Equal(8U, opts.MemtableProtectionBytesPerKey);
    }

    [Fact]
    public void MemtableVerifyPerKeyChecksumOnSeek_GetSet()
    {
        using var opts = new DbOptions();

        opts.MemtableVerifyPerKeyChecksumOnSeek = true;
        Assert.True(opts.MemtableVerifyPerKeyChecksumOnSeek);

        opts.MemtableVerifyPerKeyChecksumOnSeek = false;
        Assert.False(opts.MemtableVerifyPerKeyChecksumOnSeek);
    }

    [Fact]
    public void MinTombstonesForRangeConversion_GetSet()
    {
        using var opts = new DbOptions();

        opts.MinTombstonesForRangeConversion = 64U;
        Assert.Equal(64U, opts.MinTombstonesForRangeConversion);
    }

    [Fact]
    public void OptimizeManifestForRecovery_GetSet()
    {
        using var opts = new DbOptions();

        opts.OptimizeManifestForRecovery = true;
        Assert.True(opts.OptimizeManifestForRecovery);

        opts.OptimizeManifestForRecovery = false;
        Assert.False(opts.OptimizeManifestForRecovery);
    }

    [Fact]
    public void ParanoidFileChecks_GetSet()
    {
        using var opts = new DbOptions();

        opts.ParanoidFileChecks = true;
        Assert.True(opts.ParanoidFileChecks);

        opts.ParanoidFileChecks = false;
        Assert.False(opts.ParanoidFileChecks);
    }

    [Fact]
    public void ParanoidMemoryChecks_GetSet()
    {
        using var opts = new DbOptions();

        opts.ParanoidMemoryChecks = true;
        Assert.True(opts.ParanoidMemoryChecks);

        opts.ParanoidMemoryChecks = false;
        Assert.False(opts.ParanoidMemoryChecks);
    }

    [Fact]
    public void PersistStatsToDisk_GetSet()
    {
        using var opts = new DbOptions();

        opts.PersistStatsToDisk = true;
        Assert.True(opts.PersistStatsToDisk);

        opts.PersistStatsToDisk = false;
        Assert.False(opts.PersistStatsToDisk);
    }

    [Fact]
    public void PersistUserDefinedTimestamps_GetSet()
    {
        using var opts = new DbOptions();

        opts.PersistUserDefinedTimestamps = true;
        Assert.True(opts.PersistUserDefinedTimestamps);

        opts.PersistUserDefinedTimestamps = false;
        Assert.False(opts.PersistUserDefinedTimestamps);
    }

    [Fact]
    public void PrecludeLastLevelDataSeconds_GetSet()
    {
        using var opts = new DbOptions();

        opts.PrecludeLastLevelDataSeconds = 86400UL;
        Assert.Equal(86400UL, opts.PrecludeLastLevelDataSeconds);
    }

    [Fact]
    public void PrefixSeekOptInOnly_GetSet()
    {
        using var opts = new DbOptions();

        opts.PrefixSeekOptInOnly = true;
        Assert.True(opts.PrefixSeekOptInOnly);

        opts.PrefixSeekOptInOnly = false;
        Assert.False(opts.PrefixSeekOptInOnly);
    }

    [Fact]
    public void PreserveInternalTimeSeconds_GetSet()
    {
        using var opts = new DbOptions();

        opts.PreserveInternalTimeSeconds = 3600UL;
        Assert.Equal(3600UL, opts.PreserveInternalTimeSeconds);
    }

    [Fact]
    public void ReadIoExecutorThreads_GetSet()
    {
        using var opts = new DbOptions();

        opts.ReadIoExecutorThreads = 4;
        Assert.Equal(4, opts.ReadIoExecutorThreads);
    }

    [Fact]
    public void ReadTriggeredCompactionThreshold_GetSet()
    {
        using var opts = new DbOptions();

        opts.ReadTriggeredCompactionThreshold = 0.25;
        Assert.Equal(0.25, opts.ReadTriggeredCompactionThreshold);
    }

    [Fact]
    public void ReuseManifestOnOpen_GetSet()
    {
        using var opts = new DbOptions();

        opts.ReuseManifestOnOpen = true;
        Assert.True(opts.ReuseManifestOnOpen);

        opts.ReuseManifestOnOpen = false;
        Assert.False(opts.ReuseManifestOnOpen);
    }

    [Fact]
    public void SampleForCompression_GetSet()
    {
        using var opts = new DbOptions();

        opts.SampleForCompression = 100UL;
        Assert.Equal(100UL, opts.SampleForCompression);
    }

    [Fact]
    public void StatsHistoryBufferSize_GetSet()
    {
        using var opts = new DbOptions();

        opts.StatsHistoryBufferSize = 2097152UL;
        Assert.Equal(2097152UL, opts.StatsHistoryBufferSize);
    }

    [Fact]
    public void StrictBytesPerSync_GetSet()
    {
        using var opts = new DbOptions();

        opts.StrictBytesPerSync = true;
        Assert.True(opts.StrictBytesPerSync);

        opts.StrictBytesPerSync = false;
        Assert.False(opts.StrictBytesPerSync);
    }

    [Fact]
    public void StrictMaxSuccessiveMerges_GetSet()
    {
        using var opts = new DbOptions();

        opts.StrictMaxSuccessiveMerges = true;
        Assert.True(opts.StrictMaxSuccessiveMerges);

        opts.StrictMaxSuccessiveMerges = false;
        Assert.False(opts.StrictMaxSuccessiveMerges);
    }

    [Fact]
    public void TargetFileSizeIsUpperBound_GetSet()
    {
        using var opts = new DbOptions();

        opts.TargetFileSizeIsUpperBound = true;
        Assert.True(opts.TargetFileSizeIsUpperBound);

        opts.TargetFileSizeIsUpperBound = false;
        Assert.False(opts.TargetFileSizeIsUpperBound);
    }

    [Fact]
    public void TrackAndVerifyWals_GetSet()
    {
        using var opts = new DbOptions();

        opts.TrackAndVerifyWals = true;
        Assert.True(opts.TrackAndVerifyWals);

        opts.TrackAndVerifyWals = false;
        Assert.False(opts.TrackAndVerifyWals);
    }

    [Fact]
    public void TwoWriteQueues_GetSet()
    {
        using var opts = new DbOptions();

        opts.TwoWriteQueues = true;
        Assert.True(opts.TwoWriteQueues);

        opts.TwoWriteQueues = false;
        Assert.False(opts.TwoWriteQueues);
    }

    [Fact]
    public void UncacheAggressiveness_GetSet()
    {
        using var opts = new DbOptions();

        opts.UncacheAggressiveness = 100U;
        Assert.Equal(100U, opts.UncacheAggressiveness);
    }

    [Fact]
    public void UseDirectIoForCompactionReads_GetSet()
    {
        using var opts = new DbOptions();

        opts.UseDirectIoForCompactionReads = true;
        Assert.True(opts.UseDirectIoForCompactionReads);

        opts.UseDirectIoForCompactionReads = false;
        Assert.False(opts.UseDirectIoForCompactionReads);
    }

    [Fact]
    public void VerifyManifestContentOnClose_GetSet()
    {
        using var opts = new DbOptions();

        opts.VerifyManifestContentOnClose = true;
        Assert.True(opts.VerifyManifestContentOnClose);

        opts.VerifyManifestContentOnClose = false;
        Assert.False(opts.VerifyManifestContentOnClose);
    }

    /// <summary>
    /// Every flag, and combinations across the two groups, round-trip.
    /// </summary>
    [Fact]
    public void VerifyOutputFlags_GetSet()
    {
        using var opts = new DbOptions();

        Assert.Equal(VerifyOutputFlags.None, opts.VerifyOutputFlags);

        foreach (VerifyOutputFlags flag in Enum.GetValues<VerifyOutputFlags>())
        {
            opts.VerifyOutputFlags = flag;
            Assert.Equal(flag, opts.VerifyOutputFlags);
        }

        // What to verify, and when to verify it: a usable value needs one from
        // each group, so the combination has to survive the round trip.
        VerifyOutputFlags combined =
            VerifyOutputFlags.BlockChecksum
            | VerifyOutputFlags.FileChecksum
            | VerifyOutputFlags.EnableForLocalCompaction;

        opts.VerifyOutputFlags = combined;

        Assert.Equal(combined, opts.VerifyOutputFlags);
        Assert.True(opts.VerifyOutputFlags.HasFlag(VerifyOutputFlags.EnableForLocalCompaction));
        Assert.False(opts.VerifyOutputFlags.HasFlag(VerifyOutputFlags.Iteration));
    }

    /// <summary>
    /// The flag values are the bit positions RocksDb defines, not an invented
    /// sequence.
    /// </summary>
    /// <remarks>
    /// Taken from <c>VerifyOutputFlags</c> in
    /// <c>include/rocksdb/advanced_options.h</c> at the pinned version. Pinned
    /// here because a wrong bit position would silently ask for a different
    /// verification than the caller named, and nothing else would notice: the C
    /// API validates none of this.
    /// </remarks>
    [Fact]
    public void VerifyOutputFlags_MatchTheBitPositionsRocksDbDefines()
    {
        Assert.Equal(0U, (uint)VerifyOutputFlags.None);
        Assert.Equal(1U << 0, (uint)VerifyOutputFlags.BlockChecksum);
        Assert.Equal(1U << 1, (uint)VerifyOutputFlags.Iteration);
        Assert.Equal(1U << 2, (uint)VerifyOutputFlags.FileChecksum);
        Assert.Equal(1U << 10, (uint)VerifyOutputFlags.EnableForLocalCompaction);
        Assert.Equal(1U << 11, (uint)VerifyOutputFlags.EnableForRemoteCompaction);
        Assert.Equal(uint.MaxValue, (uint)VerifyOutputFlags.All);
    }

    /// <summary>
    /// A database opens and compacts with verification asked for, so the flags
    /// reach RocksDb rather than merely round-tripping on the options.
    /// </summary>
    [Fact]
    public void VerifyOutputFlags_AreAcceptedByAnOpenDatabase()
    {
        var options = new DbOptions
        {
            CreateIfMissing = true,
            VerifyOutputFlags =
                VerifyOutputFlags.BlockChecksum
                | VerifyOutputFlags.Iteration
                | VerifyOutputFlags.FileChecksum
                | VerifyOutputFlags.EnableForLocalCompaction,
        };

        using RocksDb db = TestDb.OpenInMemory(options);

        for (int i = 0; i < 200; i++)
        {
            db.Put($"key{i:D4}", $"value{i:D4}");
        }

        db.Flush();
        db.CompactRange();

        Assert.Equal("value0100", db.GetString("key0100"));
    }

    [Fact]
    public void VerifySstUniqueIdInManifest_GetSet()
    {
        using var opts = new DbOptions();

        opts.VerifySstUniqueIdInManifest = true;
        Assert.True(opts.VerifySstUniqueIdInManifest);

        opts.VerifySstUniqueIdInManifest = false;
        Assert.False(opts.VerifySstUniqueIdInManifest);
    }

    [Fact]
    public void WriteThreadMaxYieldUsec_GetSet()
    {
        using var opts = new DbOptions();

        opts.WriteThreadMaxYieldUsec = 200UL;
        Assert.Equal(200UL, opts.WriteThreadMaxYieldUsec);
    }

    [Fact]
    public void WriteThreadSlowYieldUsec_GetSet()
    {
        using var opts = new DbOptions();

        opts.WriteThreadSlowYieldUsec = 5UL;
        Assert.Equal(5UL, opts.WriteThreadSlowYieldUsec);
    }

    [Fact]
    public void DumpMallocStats_GetSet()
    {
        using var opts = new DbOptions();

        opts.DumpMallocStats = true;
        Assert.True(opts.DumpMallocStats);

        opts.DumpMallocStats = false;
        Assert.False(opts.DumpMallocStats);
    }

    [Fact]
    public void MemtableWholeKeyFiltering_GetSet()
    {
        using var opts = new DbOptions();

        opts.MemtableWholeKeyFiltering = true;
        Assert.True(opts.MemtableWholeKeyFiltering);

        opts.MemtableWholeKeyFiltering = false;
        Assert.False(opts.MemtableWholeKeyFiltering);
    }

    [Fact]
    public void DailyOffpeakTimeUtc_GetSet()
    {
        using var opts = new DbOptions();

        opts.DailyOffpeakTimeUtc = "02:00-05:00";
        Assert.Equal("02:00-05:00", opts.DailyOffpeakTimeUtc);
    }

    [Fact]
    public void DbHostId_GetSet()
    {
        using var opts = new DbOptions();

        opts.DbHostId = "test-host-id";
        Assert.Equal("test-host-id", opts.DbHostId);
    }

    [Fact]
    public void DbLogDir_GetSet()
    {
        using var opts = new DbOptions();

        opts.DbLogDir = "/tmp/rocksdbnet-logs";
        Assert.Equal("/tmp/rocksdbnet-logs", opts.DbLogDir);
    }

    [Fact]
    public void WalDir_GetSet()
    {
        using var opts = new DbOptions();

        opts.WalDir = "/tmp/rocksdbnet-wal";
        Assert.Equal("/tmp/rocksdbnet-wal", opts.WalDir);
    }

    [Theory]
    [InlineData(Temperature.Unknown)]
    [InlineData(Temperature.Hot)]
    [InlineData(Temperature.Warm)]
    [InlineData(Temperature.Cool)]
    [InlineData(Temperature.Cold)]
    [InlineData(Temperature.Ice)]
    public void DefaultTemperature_GetSet(Temperature temperature)
    {
        using var opts = new DbOptions();

        opts.DefaultTemperature = temperature;
        Assert.Equal(temperature, opts.DefaultTemperature);
    }

    [Theory]
    [InlineData(Temperature.Unknown)]
    [InlineData(Temperature.Hot)]
    [InlineData(Temperature.Warm)]
    [InlineData(Temperature.Cool)]
    [InlineData(Temperature.Cold)]
    [InlineData(Temperature.Ice)]
    public void DefaultWriteTemperature_GetSet(Temperature temperature)
    {
        using var opts = new DbOptions();

        opts.DefaultWriteTemperature = temperature;
        Assert.Equal(temperature, opts.DefaultWriteTemperature);
    }

    [Theory]
    [InlineData(Temperature.Unknown)]
    [InlineData(Temperature.Hot)]
    [InlineData(Temperature.Warm)]
    [InlineData(Temperature.Cool)]
    [InlineData(Temperature.Cold)]
    [InlineData(Temperature.Ice)]
    public void LastLevelTemperature_GetSet(Temperature temperature)
    {
        using var opts = new DbOptions();

        opts.LastLevelTemperature = temperature;
        Assert.Equal(temperature, opts.LastLevelTemperature);
    }

    [Theory]
    [InlineData(Temperature.Unknown)]
    [InlineData(Temperature.Hot)]
    [InlineData(Temperature.Warm)]
    [InlineData(Temperature.Cool)]
    [InlineData(Temperature.Cold)]
    [InlineData(Temperature.Ice)]
    public void MetadataWriteTemperature_GetSet(Temperature temperature)
    {
        using var opts = new DbOptions();

        opts.MetadataWriteTemperature = temperature;
        Assert.Equal(temperature, opts.MetadataWriteTemperature);
    }

    [Theory]
    [InlineData(Temperature.Unknown)]
    [InlineData(Temperature.Hot)]
    [InlineData(Temperature.Warm)]
    [InlineData(Temperature.Cool)]
    [InlineData(Temperature.Cold)]
    [InlineData(Temperature.Ice)]
    public void WalWriteTemperature_GetSet(Temperature temperature)
    {
        using var opts = new DbOptions();

        opts.WalWriteTemperature = temperature;
        Assert.Equal(temperature, opts.WalWriteTemperature);
    }

    [Theory]
    [InlineData(CacheTier.Volatile)]
    [InlineData(CacheTier.VolatileCompressed)]
    [InlineData(CacheTier.NonVolatileBlock)]
    public void LowestUsedCacheTier_GetSet(CacheTier tier)
    {
        using var opts = new DbOptions();

        opts.LowestUsedCacheTier = tier;
        Assert.Equal(tier, opts.LowestUsedCacheTier);
    }

    [Fact]
    public void ChecksumHandoffFileTypes_AddRemoveContainsCountClear()
    {
        using var opts = new DbOptions();

        Assert.Equal(0, opts.ChecksumHandoffFileTypeCount);
        Assert.False(opts.ContainsChecksumHandoffFileType(FileType.TableFile));

        opts.AddChecksumHandoffFileType(FileType.TableFile);
        opts.AddChecksumHandoffFileType(FileType.WalFile);

        Assert.Equal(2, opts.ChecksumHandoffFileTypeCount);
        Assert.True(opts.ContainsChecksumHandoffFileType(FileType.TableFile));
        Assert.True(opts.ContainsChecksumHandoffFileType(FileType.WalFile));
        Assert.False(opts.ContainsChecksumHandoffFileType(FileType.BlobFile));

        opts.RemoveChecksumHandoffFileType(FileType.WalFile);

        Assert.Equal(1, opts.ChecksumHandoffFileTypeCount);
        Assert.False(opts.ContainsChecksumHandoffFileType(FileType.WalFile));

        opts.ClearChecksumHandoffFileTypes();

        Assert.Equal(0, opts.ChecksumHandoffFileTypeCount);
        Assert.False(opts.ContainsChecksumHandoffFileType(FileType.TableFile));
    }

    [Fact]
    public void CalculateSstWriteLifetimeHint_AddRemoveContainsCountClear()
    {
        using var opts = new DbOptions();

        // RocksDb ships a non-empty default for this set, so start from a known
        // state rather than assuming it is empty.
        opts.ClearCalculateSstWriteLifetimeHints();

        Assert.Equal(0, opts.CalculateSstWriteLifetimeHintCount);
        Assert.False(opts.ContainsCalculateSstWriteLifetimeHint(CompactionStyle.Level));

        opts.AddCalculateSstWriteLifetimeHint(CompactionStyle.Level);
        opts.AddCalculateSstWriteLifetimeHint(CompactionStyle.Universal);

        Assert.Equal(2, opts.CalculateSstWriteLifetimeHintCount);
        Assert.True(opts.ContainsCalculateSstWriteLifetimeHint(CompactionStyle.Level));
        Assert.True(opts.ContainsCalculateSstWriteLifetimeHint(CompactionStyle.Universal));

        opts.RemoveCalculateSstWriteLifetimeHint(CompactionStyle.Universal);

        Assert.Equal(1, opts.CalculateSstWriteLifetimeHintCount);
        Assert.False(opts.ContainsCalculateSstWriteLifetimeHint(CompactionStyle.Universal));

        opts.ClearCalculateSstWriteLifetimeHints();

        Assert.Equal(0, opts.CalculateSstWriteLifetimeHintCount);
    }

    [Fact]
    public void CalculateSstWriteLifetimeHint_HasANonEmptyDefault()
    {
        // Worth pinning down, since it is the reason the test above clears the
        // set before asserting on it.
        using var opts = new DbOptions();

        Assert.True(opts.CalculateSstWriteLifetimeHintCount > 0);
    }
}
