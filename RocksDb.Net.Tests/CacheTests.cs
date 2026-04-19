namespace RocksDbNet.Tests;

public class CacheTests
{
    [Fact]
    public void CreateLru_Works()
    {
        using var cache = Cache.CreateLru(64 * 1024 * 1024);

        Assert.Equal(64UL * 1024 * 1024, cache.Capacity);
    }

    [Fact]
    public void CreateLruWithStrictCapacityLimit_Works()
    {
        using var cache = Cache.CreateLruWithStrictCapacityLimit(32 * 1024 * 1024);

        Assert.Equal(32UL * 1024 * 1024, cache.Capacity);
    }

    [Fact]
    public void CreateHyperClock_Works()
    {
        using var cache = Cache.CreateHyperClock(64 * 1024 * 1024, 4096);

        Assert.Equal(64UL * 1024 * 1024, cache.Capacity);
    }

    [Fact]
    public void Capacity_SetGet()
    {
        using var cache = Cache.CreateLru(64 * 1024 * 1024);

        cache.Capacity = 128 * 1024 * 1024;
        Assert.Equal(128UL * 1024 * 1024, cache.Capacity);
    }

    [Fact]
    public void Usage_ReturnsValue()
    {
        using var cache = Cache.CreateLru(64 * 1024 * 1024);

        // Empty cache should have very little or no usage
        ulong usage = cache.Usage;
        Assert.True(usage < 1024 * 1024); // Less than 1MB for empty cache
    }

    [Fact]
    public void PinnedUsage_ReturnsValue()
    {
        using var cache = Cache.CreateLru(64 * 1024 * 1024);

        ulong pinnedUsage = cache.PinnedUsage;
        Assert.True(pinnedUsage < 1024 * 1024);
    }

    [Fact]
    public void Cache_UsedWithBlockBasedTable()
    {
        using var dir = new TempDir();
        using var cache = Cache.CreateLru(64 * 1024 * 1024);
        using var bbto = new BlockBasedTableOptions();
        bbto.SetBlockCache(cache);

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.BlockBasedTableFactory = bbto;

        using var db = RocksDb.Open(opts, dir.Path);
        db.Put("key", "value");
        db.Flush();

        // Read to populate cache
        _ = db.GetString("key");
    }
}
