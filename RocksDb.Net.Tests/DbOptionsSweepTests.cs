namespace RocksDbNet.Tests;

/// <summary>
/// The database options that were previously reachable from nowhere, and the
/// compact-on-deletion collector. See issues #72 and #73.
/// </summary>
public class DbOptionsSweepTests
{
    // ── Booleans ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Every added boolean, round-tripped both ways. Two of these have a native
    /// setter taking an int and a getter returning a byte, so asserting both
    /// directions matters more than usual.
    /// </summary>
    [Fact]
    public void BooleanOptions_RoundTripBothWays()
    {
        using var opts = new DbOptions();

        var setters = new (string Name, Action<DbOptions, bool> Set, Func<DbOptions, bool> Get)[]
        {
            ("AdviseRandomOnOpen", (o, v) => o.AdviseRandomOnOpen = v, o => o.AdviseRandomOnOpen),
            ("AvoidUnnecessaryBlockingIo", (o, v) => o.AvoidUnnecessaryBlockingIo = v, o => o.AvoidUnnecessaryBlockingIo),
            ("CompressionOptionsUseZstdDictTrainer", (o, v) => o.CompressionOptionsUseZstdDictTrainer = v, o => o.CompressionOptionsUseZstdDictTrainer),
            ("EnablePipelinedWrite", (o, v) => o.EnablePipelinedWrite = v, o => o.EnablePipelinedWrite),
            ("EnableWriteThreadAdaptiveYield", (o, v) => o.EnableWriteThreadAdaptiveYield = v, o => o.EnableWriteThreadAdaptiveYield),
            ("InplaceUpdateSupport", (o, v) => o.InplaceUpdateSupport = v, o => o.InplaceUpdateSupport),
            ("IsFdCloseOnExec", (o, v) => o.IsFdCloseOnExec = v, o => o.IsFdCloseOnExec),
            ("OpenFilesAsync", (o, v) => o.OpenFilesAsync = v, o => o.OpenFilesAsync),
            ("OptimizeFiltersForHits", (o, v) => o.OptimizeFiltersForHits = v, o => o.OptimizeFiltersForHits),
            ("ReportBgIoStats", (o, v) => o.ReportBgIoStats = v, o => o.ReportBgIoStats),
            ("SkipStatsUpdateOnDbOpen", (o, v) => o.SkipStatsUpdateOnDbOpen = v, o => o.SkipStatsUpdateOnDbOpen),
            ("TrackAndVerifyWalsInManifest", (o, v) => o.TrackAndVerifyWalsInManifest = v, o => o.TrackAndVerifyWalsInManifest),
            ("UnorderedWrite", (o, v) => o.UnorderedWrite = v, o => o.UnorderedWrite),
            ("UseAdaptiveMutex", (o, v) => o.UseAdaptiveMutex = v, o => o.UseAdaptiveMutex),
            ("WriteDbIdToManifest", (o, v) => o.WriteDbIdToManifest = v, o => o.WriteDbIdToManifest),
            ("WriteIdentityFile", (o, v) => o.WriteIdentityFile = v, o => o.WriteIdentityFile),
        };

        Assert.Equal(16, setters.Length);

        foreach ((string name, Action<DbOptions, bool> set, Func<DbOptions, bool> get) in setters)
        {
            set(opts, true);
            Assert.True(get(opts), $"{name} should read back as true");

            set(opts, false);
            Assert.False(get(opts), $"{name} should read back as false");
        }
    }

    // ── Integers ─────────────────────────────────────────────────────────────

    [Fact]
    public void UnsignedNativeSizeOptions_RoundTrip()
    {
        using var opts = new DbOptions
        {
            ArenaBlockSize = 64 * 1024,
            HardPendingCompactionBytesLimit = (ulong)(1UL << 30),
            InplaceUpdateNumLocks = 500,
            LogFileTimeToRoll = 3600,
            ManifestPreallocationSize = 8 * 1024 * 1024,
            MaxManifestFileSize = 32 * 1024 * 1024,
            MaxSuccessiveMerges = 12,
            MemtableHugePageSize = 2 * 1024 * 1024,
            RecycleLogFileNum = 4,
            SoftPendingCompactionBytesLimit = (ulong)(1UL << 28),
        };

        Assert.Equal((ulong)(64 * 1024), opts.ArenaBlockSize);
        Assert.Equal((ulong)(1UL << 30), opts.HardPendingCompactionBytesLimit);
        Assert.Equal((ulong)500, opts.InplaceUpdateNumLocks);
        Assert.Equal((ulong)3600, opts.LogFileTimeToRoll);
        Assert.Equal((ulong)(8 * 1024 * 1024), opts.ManifestPreallocationSize);
        Assert.Equal((ulong)(32 * 1024 * 1024), opts.MaxManifestFileSize);
        Assert.Equal((ulong)12, opts.MaxSuccessiveMerges);
        Assert.Equal((ulong)(2 * 1024 * 1024), opts.MemtableHugePageSize);
        Assert.Equal((ulong)4, opts.RecycleLogFileNum);
        Assert.Equal((ulong)(1UL << 28), opts.SoftPendingCompactionBytesLimit);
    }

    [Fact]
    public void SixtyFourBitOptions_RoundTrip()
    {
        using var opts = new DbOptions
        {
            BlobCompactionReadaheadSize = 1 << 20,
            CompressionOptionsMaxDictBufferBytes = 1 << 22,
            DeleteObsoleteFilesPeriodMicros = 6_000_000,
            MaxSequentialSkipInIterations = 16,
            WritableFileMaxBufferSize = 2 << 20,
        };

        Assert.Equal(1UL << 20, opts.BlobCompactionReadaheadSize);
        Assert.Equal(1UL << 22, opts.CompressionOptionsMaxDictBufferBytes);
        Assert.Equal(6_000_000UL, opts.DeleteObsoleteFilesPeriodMicros);
        Assert.Equal(16UL, opts.MaxSequentialSkipInIterations);
        Assert.Equal(2UL << 20, opts.WritableFileMaxBufferSize);
    }

    [Fact]
    public void SignedAndUnsignedIntOptions_RoundTrip()
    {
        using var opts = new DbOptions
        {
            BlobFileStartingLevel = 2,
            BloomLocality = 1,
            CompressionOptionsParallelThreads = 4,
            CompressionOptionsZstdMaxTrainBytes = 1 << 16,
            MaxFileOpeningThreads = 8,
            MemtableAvgOpScanFlushTrigger = 100,
            MemtableOpScanFlushTrigger = 200,
            StatsPersistPeriodSec = 600,
            TableCacheNumShardBits = 5,
            TargetFileSizeMultiplier = 2,
        };

        Assert.Equal(2, opts.BlobFileStartingLevel);
        Assert.Equal(1u, opts.BloomLocality);
        Assert.Equal(4, opts.CompressionOptionsParallelThreads);
        Assert.Equal(1 << 16, opts.CompressionOptionsZstdMaxTrainBytes);
        Assert.Equal(8, opts.MaxFileOpeningThreads);
        Assert.Equal(100u, opts.MemtableAvgOpScanFlushTrigger);
        Assert.Equal(200u, opts.MemtableOpScanFlushTrigger);
        Assert.Equal(600u, opts.StatsPersistPeriodSec);
        Assert.Equal(5, opts.TableCacheNumShardBits);
        Assert.Equal(2, opts.TargetFileSizeMultiplier);
    }

    [Fact]
    public void DoubleOptions_RoundTrip()
    {
        using var opts = new DbOptions
        {
            BlobGarbageCollectionAgeCutoff = 0.4,
            BlobGarbageCollectionForceThreshold = 0.8,
            ExperimentalMempurgeThreshold = 1.5,
        };

        Assert.Equal(0.4, opts.BlobGarbageCollectionAgeCutoff, 6);
        Assert.Equal(0.8, opts.BlobGarbageCollectionForceThreshold, 6);
        Assert.Equal(1.5, opts.ExperimentalMempurgeThreshold, 6);
    }

    // ── Enums ────────────────────────────────────────────────────────────────

    [Fact]
    public void CompactionPri_RoundTripsEveryValue()
    {
        using var opts = new DbOptions();

        // RocksDb's documented default.
        Assert.Equal(CompactionPri.MinOverlappingRatio, opts.CompactionPri);

        foreach (CompactionPri value in Enum.GetValues<CompactionPri>())
        {
            opts.CompactionPri = value;
            Assert.Equal(value, opts.CompactionPri);
        }
    }

    /// <summary>
    /// The level lives on the statistics object, so it round-trips only once
    /// statistics exist.
    /// </summary>
    [Fact]
    public void StatisticsLevel_RoundTripsOnceStatisticsAreEnabled()
    {
        using var opts = new DbOptions();
        opts.EnableStatistics();

        foreach (StatsLevel value in Enum.GetValues<StatsLevel>())
        {
            opts.StatisticsLevel = value;
            Assert.Equal(value, opts.StatisticsLevel);
        }
    }

    /// <summary>
    /// Without statistics the setter is a silent no-op and the getter always
    /// reports DisableAll. Worth pinning, because assigning a level and having
    /// it ignored is exactly the kind of thing that goes unnoticed.
    /// </summary>
    [Fact]
    public void StatisticsLevel_WithoutStatistics_IsASilentNoOp()
    {
        using var opts = new DbOptions();

        opts.StatisticsLevel = StatsLevel.All;

        Assert.Equal(StatsLevel.DisableAll, opts.StatisticsLevel);
    }

    [Fact]
    public void PrepopulateBlobCache_RoundTripsEveryValue()
    {
        using var opts = new DbOptions();

        Assert.Equal(RocksDbNet.PrepopulateBlobCache.Disable, opts.PrepopulateBlobCache);

        foreach (PrepopulateBlobCache value in Enum.GetValues<PrepopulateBlobCache>())
        {
            opts.PrepopulateBlobCache = value;
            Assert.Equal(value, opts.PrepopulateBlobCache);
        }
    }

    [Fact]
    public void BlobCompression_RoundTrips()
    {
        using var opts = new DbOptions
        {
            BlobCompression = Compression.Zstd,
        };

        Assert.Equal(Compression.Zstd, opts.BlobCompression);

        opts.BlobCompression = Compression.None;
        Assert.Equal(Compression.None, opts.BlobCompression);
    }

    /// <summary>
    /// The one that could not be a property: the native setter writes two
    /// fields and its getter reports only the first.
    /// </summary>
    [Fact]
    public void BottommostCompressionOptionsUseZstdDictTrainer_IsSetThroughAMethod()
    {
        using var opts = new DbOptions();

        Assert.Same(opts, opts.SetBottommostCompressionOptionsUseZstdDictTrainer(true, enabled: true));
        Assert.True(opts.BottommostCompressionOptionsUseZstdDictTrainer);

        opts.SetBottommostCompressionOptionsUseZstdDictTrainer(false, enabled: true);
        Assert.False(opts.BottommostCompressionOptionsUseZstdDictTrainer);
    }

    // ── Actually opening with them ───────────────────────────────────────────

    /// <summary>
    /// Round trips prove the value reaches the options object. This proves the
    /// options object is still usable, which a wrong native type would break.
    /// </summary>
    [Fact]
    public void ADatabaseOpensAndWorksWithAllOfThemSet()
    {
        var opts = new DbOptions
        {
            CreateIfMissing = true,
            AdviseRandomOnOpen = true,
            ArenaBlockSize = 64 * 1024,
            AvoidUnnecessaryBlockingIo = true,
            BloomLocality = 1,
            CompactionPri = CompactionPri.RoundRobin,
            DeleteObsoleteFilesPeriodMicros = 6_000_000,
            EnablePipelinedWrite = true,
            HardPendingCompactionBytesLimit = (ulong)(1UL << 30),
            IsFdCloseOnExec = true,
            MaxFileOpeningThreads = 4,
            MaxManifestFileSize = 16 * 1024 * 1024,
            MaxSequentialSkipInIterations = 8,
            OptimizeFiltersForHits = true,
            RecycleLogFileNum = 2,
            ReportBgIoStats = true,
            SoftPendingCompactionBytesLimit = (ulong)(1UL << 28),
            StatisticsLevel = StatsLevel.ExceptDetailedTimers,
            StatsPersistPeriodSec = 60,
            TableCacheNumShardBits = 4,
            TargetFileSizeMultiplier = 2,
            TrackAndVerifyWalsInManifest = true,
            UseAdaptiveMutex = true,
            WritableFileMaxBufferSize = 1 << 20,
        };

        opts.EnableStatistics();

        using var db = TestDb.OpenInMemory(opts);

        db.Put("key", "value");
        db.Flush();

        Assert.Equal("value", db.GetString("key"));
    }

    // ── Compact-on-deletion collector, issue #73 ─────────────────────────────

    [Fact]
    public void AddCompactOnDeletionCollector_IsFluent()
    {
        using var opts = new DbOptions();

        Assert.Same(opts, opts.AddCompactOnDeletionCollector(100, 50));
    }

    /// <summary>
    /// The behavioural test: a file whose entries are mostly tombstones must be
    /// marked for compaction, and the listener must see that reason.
    /// </summary>
    [Fact]
    public void AddCompactOnDeletionCollector_MarksATombstoneHeavyFile()
    {
        var listener = new RecordingListener();

        var opts = new DbOptions { CreateIfMissing = true };
        opts.AddEventListener(listener);

        // Any window of 100 entries holding 50 deletions marks the file.
        opts.AddCompactOnDeletionCollector(windowSize: 100, deletionTrigger: 50);

        using var db = TestDb.OpenInMemory(opts);

        for (int i = 0; i < 400; i++)
        {
            db.Put($"key{i:D4}", "value");
        }

        db.Flush();

        // Delete most of them, so the next flush writes a tombstone-heavy file.
        for (int i = 0; i < 400; i++)
        {
            if (i % 4 != 0)
            {
                db.Delete($"key{i:D4}");
            }
        }

        db.Flush();
        db.WaitForCompact();

        Assert.True(
            Wait.Until(() => listener.CompactionCompleted.Any(
                c => c.CompactionReason == CompactionReason.FilesMarkedForCompaction)),
            "no compaction was reported as being of marked files");
    }

    /// <summary>
    /// Without the collector the same writes must not produce that reason, or
    /// the test above would prove nothing.
    /// </summary>
    [Fact]
    public void WithoutTheCollector_NoFileIsMarked()
    {
        var listener = new RecordingListener();

        var opts = new DbOptions { CreateIfMissing = true };
        opts.AddEventListener(listener);

        using var db = TestDb.OpenInMemory(opts);

        for (int i = 0; i < 400; i++)
        {
            db.Put($"key{i:D4}", "value");
        }

        db.Flush();

        for (int i = 0; i < 400; i++)
        {
            if (i % 4 != 0)
            {
                db.Delete($"key{i:D4}");
            }
        }

        db.Flush();
        db.WaitForCompact();

        Assert.DoesNotContain(
            listener.CompactionCompleted,
            c => c.CompactionReason == CompactionReason.FilesMarkedForCompaction);
    }

    [Fact]
    public void AddCompactOnDeletionCollector_AcceptsTheRatioAndMinimumSize()
    {
        var opts = new DbOptions { CreateIfMissing = true };

        // Repeated calls append collectors rather than replacing.
        opts.AddCompactOnDeletionCollector(100, 50, deletionRatio: 0.5, minFileSize: 1024);
        opts.AddCompactOnDeletionCollector(200, 100);

        using var db = TestDb.OpenInMemory(opts);

        db.Put("key", "value");
        db.Flush();

        Assert.Equal("value", db.GetString("key"));
    }
}
