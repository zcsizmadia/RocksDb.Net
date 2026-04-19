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

    [Fact]
    public void BottommostLevelCompaction_Set()
    {
        using var opts = new CompactRangeOptions();
        opts.BottommostLevelCompaction = true;
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
