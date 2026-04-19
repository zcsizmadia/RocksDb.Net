using System.Text;

namespace RocksDbNet.Tests;

public class DbOptionsTests
{
    [Fact]
    public void CreateIfMissing_GetSet()
    {
        using var opts = new DbOptions();

        opts.CreateIfMissing = true;
        Assert.True(opts.CreateIfMissing);

        opts.CreateIfMissing = false;
        Assert.False(opts.CreateIfMissing);
    }

    [Fact]
    public void CreateMissingColumnFamilies_GetSet()
    {
        using var opts = new DbOptions();

        opts.CreateMissingColumnFamilies = true;
        Assert.True(opts.CreateMissingColumnFamilies);
    }

    [Fact]
    public void ErrorIfExists_GetSet()
    {
        using var opts = new DbOptions();

        opts.ErrorIfExists = true;
        Assert.True(opts.ErrorIfExists);
    }

    [Fact]
    public void ParanoidChecks_GetSet()
    {
        using var opts = new DbOptions();

        opts.ParanoidChecks = true;
        Assert.True(opts.ParanoidChecks);
    }

    [Fact]
    public void WriteBufferSize_GetSet()
    {
        using var opts = new DbOptions();

        opts.WriteBufferSize = 64 * 1024 * 1024;
        Assert.Equal(64UL * 1024 * 1024, opts.WriteBufferSize);
    }

    [Fact]
    public void MaxOpenFiles_GetSet()
    {
        using var opts = new DbOptions();

        opts.MaxOpenFiles = 1000;
        Assert.Equal(1000, opts.MaxOpenFiles);
    }

    [Fact]
    public void MaxWriteBufferNumber_GetSet()
    {
        using var opts = new DbOptions();

        opts.MaxWriteBufferNumber = 4;
        Assert.Equal(4, opts.MaxWriteBufferNumber);
    }

    [Fact]
    public void MinWriteBufferNumberToMerge_GetSet()
    {
        using var opts = new DbOptions();

        opts.MinWriteBufferNumberToMerge = 2;
        Assert.Equal(2, opts.MinWriteBufferNumberToMerge);
    }

    [Fact]
    public void Compression_GetSet()
    {
        using var opts = new DbOptions();

        opts.Compression = Compression.Snappy;
        Assert.Equal(Compression.Snappy, opts.Compression);

        opts.Compression = Compression.Lz4;
        Assert.Equal(Compression.Lz4, opts.Compression);
    }

    [Fact]
    public void BottommostCompression_GetSet()
    {
        using var opts = new DbOptions();

        opts.BottommostCompression = Compression.Zstd;
        Assert.Equal(Compression.Zstd, opts.BottommostCompression);
    }

    [Fact]
    public void CompactionStyle_GetSet()
    {
        using var opts = new DbOptions();

        opts.CompactionStyle = CompactionStyle.Universal;
        Assert.Equal(CompactionStyle.Universal, opts.CompactionStyle);
    }

    [Fact]
    public void DisableAutoCompactions_GetSet()
    {
        using var opts = new DbOptions();

        opts.DisableAutoCompactions = true;
        Assert.True(opts.DisableAutoCompactions);
    }

    [Fact]
    public void NumLevels_GetSet()
    {
        using var opts = new DbOptions();

        opts.NumLevels = 5;
        Assert.Equal(5, opts.NumLevels);
    }

    [Fact]
    public void Level0Triggers_GetSet()
    {
        using var opts = new DbOptions();

        opts.Level0FileNumCompactionTrigger = 8;
        Assert.Equal(8, opts.Level0FileNumCompactionTrigger);

        opts.Level0SlowdownWritesTrigger = 20;
        Assert.Equal(20, opts.Level0SlowdownWritesTrigger);

        opts.Level0StopWritesTrigger = 36;
        Assert.Equal(36, opts.Level0StopWritesTrigger);
    }

    [Fact]
    public void TargetFileSizeBase_GetSet()
    {
        using var opts = new DbOptions();

        opts.TargetFileSizeBase = 128 * 1024 * 1024;
        Assert.Equal(128UL * 1024 * 1024, opts.TargetFileSizeBase);
    }

    [Fact]
    public void MaxBytesForLevelBase_GetSet()
    {
        using var opts = new DbOptions();

        opts.MaxBytesForLevelBase = 512 * 1024 * 1024;
        Assert.Equal(512UL * 1024 * 1024, opts.MaxBytesForLevelBase);
    }

    [Fact]
    public void MaxBytesForLevelMultiplier_GetSet()
    {
        using var opts = new DbOptions();

        opts.MaxBytesForLevelMultiplier = 8.0;
        Assert.Equal(8.0, opts.MaxBytesForLevelMultiplier);
    }

    [Fact]
    public void LevelCompactionDynamicLevelBytes_GetSet()
    {
        using var opts = new DbOptions();

        opts.LevelCompactionDynamicLevelBytes = true;
        Assert.True(opts.LevelCompactionDynamicLevelBytes);
    }

    [Fact]
    public void MaxBackgroundJobs_GetSet()
    {
        using var opts = new DbOptions();

        opts.MaxBackgroundJobs = 4;
        Assert.Equal(4, opts.MaxBackgroundJobs);
    }

    [Fact]
    public void UseDirectReads_GetSet()
    {
        using var opts = new DbOptions();

        opts.UseDirectReads = true;
        Assert.True(opts.UseDirectReads);
    }

    [Fact]
    public void AllowMmapReads_GetSet()
    {
        using var opts = new DbOptions();

        opts.AllowMmapReads = true;
        Assert.True(opts.AllowMmapReads);
    }

    [Fact]
    public void WalRecoveryMode_GetSet()
    {
        using var opts = new DbOptions();

        opts.WalRecoveryMode = WalRecoveryMode.PointInTime;
        Assert.Equal(WalRecoveryMode.PointInTime, opts.WalRecoveryMode);
    }

    [Fact]
    public void WalTtlSeconds_GetSet()
    {
        using var opts = new DbOptions();

        opts.WalTtlSeconds = 3600;
        Assert.Equal(3600UL, opts.WalTtlSeconds);
    }

    [Fact]
    public void ManualWalFlush_GetSet()
    {
        using var opts = new DbOptions();

        opts.ManualWalFlush = true;
        Assert.True(opts.ManualWalFlush);
    }

    [Fact]
    public void InfoLogLevel_GetSet()
    {
        using var opts = new DbOptions();

        opts.InfoLogLevel = InfoLogLevel.Warn;
        Assert.Equal(InfoLogLevel.Warn, opts.InfoLogLevel);
    }

    [Fact]
    public void AtomicFlush_GetSet()
    {
        using var opts = new DbOptions();

        opts.AtomicFlush = true;
        Assert.True(opts.AtomicFlush);
    }

    [Fact]
    public void Ttl_GetSet()
    {
        using var opts = new DbOptions();

        opts.Ttl = 7200;
        Assert.Equal(7200UL, opts.Ttl);
    }

    [Fact]
    public void EnableBlobFiles_GetSet()
    {
        using var opts = new DbOptions();

        opts.EnableBlobFiles = true;
        Assert.True(opts.EnableBlobFiles);
    }

    [Fact]
    public void MinBlobSize_GetSet()
    {
        using var opts = new DbOptions();

        opts.MinBlobSize = 1024;
        Assert.Equal(1024UL, opts.MinBlobSize);
    }

    [Fact]
    public void EnableBlobGc_GetSet()
    {
        using var opts = new DbOptions();

        opts.EnableBlobGc = true;
        Assert.True(opts.EnableBlobGc);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        using var opts = new DbOptions();
        opts.MaxOpenFiles = 500;
        opts.WriteBufferSize = 128 * 1024 * 1024;

        using var clone = opts.Clone();

        Assert.Equal(500, clone.MaxOpenFiles);
        Assert.Equal(128UL * 1024 * 1024, clone.WriteBufferSize);

        // Changing clone should not affect original
        clone.MaxOpenFiles = 1000;
        Assert.Equal(500, opts.MaxOpenFiles);
    }

    [Fact]
    public void IncreaseParallelism_DoesNotThrow()
    {
        using var opts = new DbOptions();
        opts.IncreaseParallelism(4);
    }

    [Fact]
    public void OptimizeForPointLookup_DoesNotThrow()
    {
        using var opts = new DbOptions();
        opts.OptimizeForPointLookup(128);
    }

    [Fact]
    public void OptimizeLevelStyleCompaction_DoesNotThrow()
    {
        using var opts = new DbOptions();
        opts.OptimizeLevelStyleCompaction();
    }

    [Fact]
    public void OptimizeUniversalStyleCompaction_DoesNotThrow()
    {
        using var opts = new DbOptions();
        opts.OptimizeUniversalStyleCompaction();
    }

    [Fact]
    public void PrepareForBulkLoad_DoesNotThrow()
    {
        using var opts = new DbOptions();
        opts.PrepareForBulkLoad();
    }

    [Fact]
    public void EnableStatistics_GetStats()
    {
        using var opts = new DbOptions();
        opts.EnableStatistics();

        string? stats = opts.GetStatisticsString();
        Assert.NotNull(stats);
    }

    [Fact]
    public void BlockBasedTableFactory_Set()
    {
        using var opts = new DbOptions();
        using var bbto = new BlockBasedTableOptions();

        opts.BlockBasedTableFactory = bbto;
    }

    [Fact]
    public void RowCache_Set()
    {
        using var opts = new DbOptions();
        using var cache = Cache.CreateLru(64 * 1024 * 1024);

        opts.RowCache = cache;
    }

    [Fact]
    public void RateLimiter_Set()
    {
        using var opts = new DbOptions();
        using var limiter = new RateLimiter(100 * 1024 * 1024);

        opts.RateLimiter = limiter;
    }

    [Fact]
    public void PrefixExtractor_Set()
    {
        using var opts = new DbOptions();
        using var pe = SliceTransform.CreateFixedPrefix(4);

        opts.PrefixExtractor = pe;
    }

    [Fact]
    public void DbLogDir_Set()
    {
        using var opts = new DbOptions();
        using var dir = new TempDir();

        opts.DbLogDir = dir.Path;
    }

    [Fact]
    public void WalDir_Set()
    {
        using var opts = new DbOptions();
        using var dir = new TempDir();

        opts.WalDir = dir.Path;
    }

    [Fact]
    public void BytesPerSync_GetSet()
    {
        using var opts = new DbOptions();

        opts.BytesPerSync = 1024 * 1024;
        Assert.Equal(1024UL * 1024, opts.BytesPerSync);
    }

    [Fact]
    public void StatsDumpPeriodSec_GetSet()
    {
        using var opts = new DbOptions();

        opts.StatsDumpPeriodSec = 300;
        Assert.Equal(300u, opts.StatsDumpPeriodSec);
    }

    [Fact]
    public void PeriodicCompactionSeconds_GetSet()
    {
        using var opts = new DbOptions();

        opts.PeriodicCompactionSeconds = 86400;
        Assert.Equal(86400UL, opts.PeriodicCompactionSeconds);
    }

    [Fact]
    public void MemtablePrefixBloomSizeRatio_GetSet()
    {
        using var opts = new DbOptions();

        opts.MemtablePrefixBloomSizeRatio = 0.05;
        Assert.Equal(0.05, opts.MemtablePrefixBloomSizeRatio, 0.001);
    }
}
