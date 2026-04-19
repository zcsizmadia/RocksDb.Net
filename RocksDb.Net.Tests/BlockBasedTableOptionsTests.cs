namespace RocksDbNet.Tests;

public class BlockBasedTableOptionsTests
{
    [Fact]
    public void Create_DoesNotThrow()
    {
        using var bbto = new BlockBasedTableOptions();
        Assert.False(bbto.IsDisposed);
    }

    [Fact]
    public void SetBlockCache_DoesNotThrow()
    {
        using var bbto = new BlockBasedTableOptions();
        using var cache = Cache.CreateLru(64 * 1024 * 1024);

        bbto.SetBlockCache(cache);
    }

    [Fact]
    public void SetBlockCache_Null_DoesNotThrow()
    {
        using var bbto = new BlockBasedTableOptions();

        bbto.SetBlockCache(null);
    }

    [Fact]
    public void NoBlockCache_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.NoBlockCache = true;
    }

    [Fact]
    public void BlockSize_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.BlockSize = 16384;
    }

    [Fact]
    public void SetFilterPolicy_Bloom()
    {
        using var bbto = new BlockBasedTableOptions();
        using var fp = FilterPolicy.CreateBloom(10);

        bbto.SetFilterPolicy(fp);
    }

    [Fact]
    public void WholeKeyFiltering_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.WholeKeyFiltering = true;
    }

    [Fact]
    public void FormatVersion_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.FormatVersion = 5;
    }

    [Fact]
    public void IndexType_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.IndexType = BlockBasedTableIndexType.TwoLevelIndexSearch;
    }

    [Fact]
    public void CacheIndexAndFilterBlocks_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.CacheIndexAndFilterBlocks = true;
    }

    [Fact]
    public void CacheIndexAndFilterBlocksWithHighPriority_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.CacheIndexAndFilterBlocksWithHighPriority = true;
    }

    [Fact]
    public void PinL0FilterAndIndexBlocksInCache_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.PinL0FilterAndIndexBlocksInCache = true;
    }

    [Fact]
    public void BlockSizeDeviation_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.BlockSizeDeviation = 5;
    }

    [Fact]
    public void BlockRestartInterval_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.BlockRestartInterval = 32;
    }

    [Fact]
    public void PartitionFilters_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.PartitionFilters = true;
    }

    [Fact]
    public void MetadataBlockSize_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.MetadataBlockSize = 8192;
    }

    [Fact]
    public void UseDeltaEncoding_Set()
    {
        using var bbto = new BlockBasedTableOptions();
        bbto.UseDeltaEncoding = true;
    }

    [Fact]
    public void FullConfiguration_Works()
    {
        using var dir = new TempDir();
        using var cache = Cache.CreateLru(64 * 1024 * 1024);
        using var fp = FilterPolicy.CreateBloomFull(10);
        using var bbto = new BlockBasedTableOptions();

        bbto.SetBlockCache(cache);
        bbto.SetFilterPolicy(fp);
        bbto.BlockSize = 16384;
        bbto.WholeKeyFiltering = true;
        bbto.CacheIndexAndFilterBlocks = true;
        bbto.FormatVersion = 5;

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.BlockBasedTableFactory = bbto;

        using var db = RocksDb.Open(opts, dir.Path);
        db.Put("key", "value");
        Assert.Equal("value", db.GetString("key"));
    }
}
