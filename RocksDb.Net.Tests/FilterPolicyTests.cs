namespace RocksDbNet.Tests;

public class FilterPolicyTests
{
    [Fact]
    public void CreateBloom_Works()
    {
        using var fp = FilterPolicy.CreateBloom(10);
        Assert.False(fp.IsDisposed);
    }

    [Fact]
    public void CreateBloomFull_Works()
    {
        using var fp = FilterPolicy.CreateBloomFull(10);
        Assert.False(fp.IsDisposed);
    }

    [Fact]
    public void CreateRibbon_Works()
    {
        using var fp = FilterPolicy.CreateRibbon(10);
        Assert.False(fp.IsDisposed);
    }

    [Fact]
    public void CreateRibbonHybrid_Works()
    {
        using var fp = FilterPolicy.CreateRibbonHybrid(10, bloomBeforeLevel: 1);
        Assert.False(fp.IsDisposed);
    }

    [Fact]
    public void BloomFilter_WithDatabase()
    {
        using var dir = new TempDir();
        using var fp = FilterPolicy.CreateBloomFull(10);
        using var bbto = new BlockBasedTableOptions();
        bbto.SetFilterPolicy(fp);

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.BlockBasedTableFactory = bbto;

        using var db = RocksDb.Open(opts, dir.Path);

        // Write and read with bloom filter active
        for (int i = 0; i < 100; i++)
            db.Put($"key{i}", $"val{i}");

        db.Flush();

        for (int i = 0; i < 100; i++)
            Assert.Equal($"val{i}", db.GetString($"key{i}"));
    }
}
