namespace RocksDbNet.Tests;

public class WriteOptionsTests
{
    [Fact]
    public void Sync_GetSet()
    {
        using var opts = new WriteOptions();

        opts.Sync = true;
        Assert.True(opts.Sync);

        opts.Sync = false;
        Assert.False(opts.Sync);
    }

    [Fact]
    public void DisableWal_GetSet()
    {
        using var opts = new WriteOptions();

        opts.DisableWal = true;
        Assert.True(opts.DisableWal);
    }

    [Fact]
    public void NoSlowdown_GetSet()
    {
        using var opts = new WriteOptions();

        opts.NoSlowdown = true;
        Assert.True(opts.NoSlowdown);
    }

    [Fact]
    public void LowPriority_GetSet()
    {
        using var opts = new WriteOptions();

        opts.LowPriority = true;
        Assert.True(opts.LowPriority);
    }

    [Fact]
    public void IgnoreMissingColumnFamilies_GetSet()
    {
        using var opts = new WriteOptions();

        opts.IgnoreMissingColumnFamilies = true;
        Assert.True(opts.IgnoreMissingColumnFamilies);
    }

    [Fact]
    public void WriteWithCustomOptions()
    {
        using var db = new TempDb();
        using var writeOpts = new WriteOptions { Sync = true };

        db.Db.Put("key", "value", writeOpts);

        Assert.Equal("value", db.Db.GetString("key"));
    }
}
