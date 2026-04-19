namespace RocksDbNet.Tests;

public class ReadOptionsTests
{
    [Fact]
    public void VerifyChecksums_GetSet()
    {
        using var opts = new ReadOptions();

        opts.VerifyChecksums = true;
        Assert.True(opts.VerifyChecksums);

        opts.VerifyChecksums = false;
        Assert.False(opts.VerifyChecksums);
    }

    [Fact]
    public void FillCache_GetSet()
    {
        using var opts = new ReadOptions();

        opts.FillCache = false;
        Assert.False(opts.FillCache);
    }

    [Fact]
    public void ReadTier_GetSet()
    {
        using var opts = new ReadOptions();

        opts.ReadTier = 1;
        Assert.Equal(1, opts.ReadTier);
    }

    [Fact]
    public void Tailing_GetSet()
    {
        using var opts = new ReadOptions();

        opts.Tailing = true;
        Assert.True(opts.Tailing);
    }

    [Fact]
    public void ReadaheadSize_GetSet()
    {
        using var opts = new ReadOptions();

        opts.ReadaheadSize = 2 * 1024 * 1024;
        Assert.Equal(2UL * 1024 * 1024, opts.ReadaheadSize);
    }

    [Fact]
    public void PrefixSameAsStart_GetSet()
    {
        using var opts = new ReadOptions();

        opts.PrefixSameAsStart = true;
        Assert.True(opts.PrefixSameAsStart);
    }

    [Fact]
    public void PinData_GetSet()
    {
        using var opts = new ReadOptions();

        opts.PinData = true;
        Assert.True(opts.PinData);
    }

    [Fact]
    public void TotalOrderSeek_GetSet()
    {
        using var opts = new ReadOptions();

        opts.TotalOrderSeek = true;
        Assert.True(opts.TotalOrderSeek);
    }

    [Fact]
    public void AsyncIo_GetSet()
    {
        using var opts = new ReadOptions();

        opts.AsyncIo = true;
        Assert.True(opts.AsyncIo);
    }

    [Fact]
    public void IgnoreRangeDeletions_GetSet()
    {
        using var opts = new ReadOptions();

        opts.IgnoreRangeDeletions = true;
        Assert.True(opts.IgnoreRangeDeletions);
    }

    [Fact]
    public void SetSnapshot_DoesNotThrow()
    {
        using var opts = new ReadOptions();

        opts.SetSnapshot(null);
    }
}
