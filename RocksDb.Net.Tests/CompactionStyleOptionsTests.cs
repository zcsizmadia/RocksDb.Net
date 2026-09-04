using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Tuning for the universal and FIFO compaction styles. See issue #80.
/// </summary>
/// <remarks>
/// Both styles were already selectable through
/// <see cref="DbOptions.CompactionStyle"/> while none of their settings were
/// reachable, which is arguably worse than not offering the style at all.
/// </remarks>
public class CompactionStyleOptionsTests
{
    // ── Universal ────────────────────────────────────────────────────────────

    [Fact]
    public void UniversalCompactionOptions_ScalarsRoundTrip()
    {
        using var opts = new UniversalCompactionOptions
        {
            SizeRatio = 5,
            MinMergeWidth = 3,
            MaxMergeWidth = 12,
            MaxSizeAmplificationPercent = 150,
            CompressionSizePercent = 80,
            MaxReadAmp = 6,
        };

        Assert.Equal(5, opts.SizeRatio);
        Assert.Equal(3, opts.MinMergeWidth);
        Assert.Equal(12, opts.MaxMergeWidth);
        Assert.Equal(150, opts.MaxSizeAmplificationPercent);
        Assert.Equal(80, opts.CompressionSizePercent);
        Assert.Equal(6, opts.MaxReadAmp);
    }

    [Fact]
    public void UniversalCompactionOptions_FlagsRoundTrip()
    {
        using var opts = new UniversalCompactionOptions();

        foreach (bool value in new[] { true, false })
        {
            opts.AllowTrivialMove = value;
            opts.Incremental = value;
            opts.ReduceFileLocking = value;

            Assert.Equal(value, opts.AllowTrivialMove);
            Assert.Equal(value, opts.Incremental);
            Assert.Equal(value, opts.ReduceFileLocking);
        }
    }

    [Fact]
    public void UniversalCompactionOptions_StopStyleRoundTripsEveryValue()
    {
        using var opts = new UniversalCompactionOptions();

        foreach (CompactionStopStyle value in Enum.GetValues<CompactionStopStyle>())
        {
            opts.StopStyle = value;
            Assert.Equal(value, opts.StopStyle);
        }
    }

    /// <summary>
    /// The values must match the C++ header, which here agrees with the C one.
    /// Worth pinning given how many enums in this library did not.
    /// </summary>
    [Fact]
    public void CompactionStopStyle_MatchesTheNativeValues()
    {
        Assert.Equal(0, (int)CompactionStopStyle.SimilarSize);
        Assert.Equal(1, (int)CompactionStopStyle.TotalSize);
        Assert.Equal(2, Enum.GetValues<CompactionStopStyle>().Length);
    }

    /// <summary>
    /// The behavioural test: a database opens and works with universal
    /// compaction actually configured, which a wrong native type would break.
    /// </summary>
    [Fact]
    public void UniversalCompaction_DatabaseOpensAndCompacts()
    {
        var opts = new DbOptions
        {
            CreateIfMissing = true,
            CompactionStyle = CompactionStyle.Universal,
            WriteBufferSize = 16 * 1024,
            Level0FileNumCompactionTrigger = 2,
        };

        using (var universal = new UniversalCompactionOptions
        {
            SizeRatio = 10,
            MinMergeWidth = 2,
            MaxMergeWidth = 8,
            MaxSizeAmplificationPercent = 120,
            StopStyle = CompactionStopStyle.TotalSize,
            AllowTrivialMove = true,
        })
        {
            opts.UniversalCompactionOptions = universal;
        }

        // Disposed above, because RocksDb copied the values.
        using var db = TestDb.OpenInMemory(opts);

        for (int i = 0; i < 600; i++)
        {
            db.Put($"key{i:D4}", new string('v', 256));
        }

        db.Flush();
        db.CompactRange();
        db.WaitForCompact();

        Assert.Equal(new string('v', 256), db.GetString("key0300"));

        ColumnFamilyMetadata? metadata = db.GetColumnFamilyMetadata();
        Assert.NotNull(metadata);
        Assert.True(metadata.FileCount > 0);
    }

    // ── FIFO ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FifoCompactionOptions_ScalarsRoundTrip()
    {
        using var opts = new FifoCompactionOptions
        {
            MaxTableFilesSize = 64 * 1024 * 1024,
            MaxDataFilesSize = 32 * 1024 * 1024,
            AgeForWarm = 3600,
            TrivialCopyBufferSize = 1 << 20,
        };

        Assert.Equal(64UL * 1024 * 1024, opts.MaxTableFilesSize);
        Assert.Equal(32UL * 1024 * 1024, opts.MaxDataFilesSize);
        Assert.Equal(3600UL, opts.AgeForWarm);
        Assert.Equal(1UL << 20, opts.TrivialCopyBufferSize);
    }

    [Fact]
    public void FifoCompactionOptions_FlagsRoundTrip()
    {
        using var opts = new FifoCompactionOptions();

        Assert.False(opts.AllowCompaction);

        foreach (bool value in new[] { true, false })
        {
            opts.AllowCompaction = value;
            opts.AllowTrivialCopyWhenChangeTemperature = value;
            opts.UseKvRatioCompaction = value;

            Assert.Equal(value, opts.AllowCompaction);
            Assert.Equal(value, opts.AllowTrivialCopyWhenChangeTemperature);
            Assert.Equal(value, opts.UseKvRatioCompaction);
        }
    }

    /// <summary>
    /// The setting that makes FIFO worth having: past the size bound, the
    /// oldest data is dropped rather than compacted, so old keys disappear
    /// while recent ones survive.
    /// </summary>
    [Fact]
    public void FifoCompaction_DropsTheOldestDataPastTheSizeBound()
    {
        var opts = new DbOptions
        {
            CreateIfMissing = true,
            CompactionStyle = CompactionStyle.Fifo,
            WriteBufferSize = 16 * 1024,
        };

        using (var fifo = new FifoCompactionOptions { MaxTableFilesSize = 128 * 1024 })
        {
            opts.FifoCompactionOptions = fifo;
        }

        using var db = TestDb.OpenInMemory(opts);

        // Incompressible values, deliberately. A repeated character compresses
        // to almost nothing, so 1500 of them occupied 44 KB against a 128 KB
        // bound and FIFO had no reason to drop anything.
        var random = new Random(1);
        byte[] value = new byte[512];

        for (int i = 0; i < 1500; i++)
        {
            random.NextBytes(value);
            db.Put(Encoding.UTF8.GetBytes($"key{i:D5}"), value);

            if (i % 100 == 0)
            {
                db.Flush();
            }
        }

        db.Flush();
        db.WaitForCompact();

        // The most recent write is retained.
        Assert.NotNull(db.Get(Encoding.UTF8.GetBytes("key01499")));

        // Something from the beginning has been dropped. FIFO deletes whole
        // files, so which keys go depends on file boundaries; asserting that
        // some early key is gone is the honest form.
        int survivingEarly = 0;
        for (int i = 0; i < 200; i++)
        {
            if (db.Get(Encoding.UTF8.GetBytes($"key{i:D5}")) is not null)
            {
                survivingEarly++;
            }
        }

        Assert.True(
            survivingEarly < 200,
            $"FIFO should have dropped some of the oldest keys, but all 200 survived");
    }

    [Fact]
    public void FifoCompaction_WithoutASizeBound_KeepsEverything()
    {
        var opts = new DbOptions
        {
            CreateIfMissing = true,
            CompactionStyle = CompactionStyle.Fifo,
            WriteBufferSize = 16 * 1024,
        };

        using var db = TestDb.OpenInMemory(opts);

        var random = new Random(1);
        byte[] value = new byte[512];

        for (int i = 0; i < 500; i++)
        {
            random.NextBytes(value);
            db.Put(Encoding.UTF8.GetBytes($"key{i:D5}"), value);
        }

        db.Flush();
        db.WaitForCompact();

        // No retention bound configured, so nothing is dropped. This is the
        // control for the test above.
        Assert.NotNull(db.Get(Encoding.UTF8.GetBytes("key00000")));
        Assert.NotNull(db.Get(Encoding.UTF8.GetBytes("key00499")));
    }

    [Fact]
    public void DbOptions_RejectNullCompactionOptions()
    {
        using var opts = new DbOptions();

        Assert.Throws<ArgumentNullException>(() => opts.UniversalCompactionOptions = null!);
        Assert.Throws<ArgumentNullException>(() => opts.FifoCompactionOptions = null!);
    }
}
