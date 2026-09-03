namespace RocksDbNet.Tests;

public class CompactRangeOptionsTests
{
    [Fact]
    public void Create_DoesNotThrow()
    {
        using var opts = new CompactRangeOptions();
        Assert.False(opts.IsDisposed);
    }

    [Fact]
    public void ExclusiveManualCompaction_Set()
    {
        using var opts = new CompactRangeOptions();
        opts.ExclusiveManualCompaction = true;
    }

    /// <summary>
    /// All four native values must round-trip. As a bool this could only ever
    /// select 0 or 1, and 1 is the default, so Force was unreachable.
    /// </summary>
    [Theory]
    [InlineData(BottommostLevelCompaction.Skip)]
    [InlineData(BottommostLevelCompaction.IfHaveCompactionFilter)]
    [InlineData(BottommostLevelCompaction.Force)]
    [InlineData(BottommostLevelCompaction.ForceOptimized)]
    public void BottommostLevelCompaction_RoundTrips(BottommostLevelCompaction value)
    {
        using var opts = new CompactRangeOptions();

        opts.BottommostLevelCompaction = value;

        Assert.Equal(value, opts.BottommostLevelCompaction);
    }

    /// <summary>
    /// The native default, which the wrapper must not change just by being
    /// constructed.
    /// </summary>
    [Fact]
    public void BottommostLevelCompaction_DefaultsToIfHaveCompactionFilter()
    {
        using var opts = new CompactRangeOptions();

        Assert.Equal(BottommostLevelCompaction.IfHaveCompactionFilter, opts.BottommostLevelCompaction);
    }

    /// <summary>A forced bottommost compaction must actually run.</summary>
    [Fact]
    public void BottommostLevelCompaction_Force_CompactsWithNoFilterPresent()
    {
        using var db = new TempDb();
        db.Db.WriteOverlappingSstFiles();

        using var opts = new CompactRangeOptions
        {
            BottommostLevelCompaction = BottommostLevelCompaction.Force,
        };

        db.Db.CompactRange(opts);

        Assert.Equal("1-updated", db.Db.GetString("a"));
        Assert.Single(db.Db.LiveFileNames());
    }

    [Fact]
    public void ChangeLevel_Set()
    {
        using var opts = new CompactRangeOptions();
        opts.ChangeLevel = true;
    }

    [Fact]
    public void TargetLevel_Set()
    {
        using var opts = new CompactRangeOptions();
        opts.TargetLevel = 2;
    }

    [Fact]
    public void MaxSubcompactions_Set()
    {
        using var opts = new CompactRangeOptions();
        opts.MaxSubcompactions = 4;
    }
}
