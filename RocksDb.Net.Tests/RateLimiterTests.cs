namespace RocksDbNet.Tests;

public class RateLimiterTests
{
    [Fact]
    public void Create_DefaultParams()
    {
        using var limiter = new RateLimiter(100 * 1024 * 1024);
        Assert.False(limiter.IsDisposed);
    }

    [Fact]
    public void Create_CustomParams()
    {
        using var limiter = new RateLimiter(
            rateBytesPerSec: 50 * 1024 * 1024,
            refillPeriodMicros: 50_000,
            fairness: 5);

        Assert.False(limiter.IsDisposed);
    }

    [Fact]
    public void RateLimiter_WithDatabase()
    {
        using var dir = new TempDir();
        using var limiter = new RateLimiter(100 * 1024 * 1024);
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.RateLimiter = limiter;

        using var db = RocksDb.Open(opts, dir.Path);
        db.Put("key", "value");
        Assert.Equal("value", db.GetString("key"));
    }
}
