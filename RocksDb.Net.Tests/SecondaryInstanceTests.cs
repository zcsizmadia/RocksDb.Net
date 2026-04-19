using System.Text;

namespace RocksDbNet.Tests;

public class SecondaryInstanceTests
{
    [Fact]
    public void OpenAsSecondary_CanRead()
    {
        using var dir = new TempDir();
        string primaryPath = dir.Sub("primary");
        string secondaryPath = dir.Sub("secondary");

        using var opts = new DbOptions { CreateIfMissing = true };

        // Create primary and write data
        using var primary = RocksDb.Open(opts, primaryPath);
        primary.Put("key", "value");
        primary.Flush();

        // Open secondary
        using var secOpts = new DbOptions();
        using var secondary = RocksDb.OpenAsSecondary(secOpts, primaryPath, secondaryPath);

        secondary.TryCatchUpWithPrimary();

        Assert.Equal("value", secondary.GetString("key"));
    }

    [Fact]
    public void Secondary_CatchesUpWithPrimary()
    {
        using var dir = new TempDir();
        string primaryPath = dir.Sub("primary");
        string secondaryPath = dir.Sub("secondary");

        using var opts = new DbOptions { CreateIfMissing = true };

        using var primary = RocksDb.Open(opts, primaryPath);
        primary.Put("k1", "v1");
        primary.Flush();

        using var secOpts = new DbOptions();
        using var secondary = RocksDb.OpenAsSecondary(secOpts, primaryPath, secondaryPath);
        secondary.TryCatchUpWithPrimary();

        Assert.Equal("v1", secondary.GetString("k1"));

        // Write more to primary
        primary.Put("k2", "v2");
        primary.Flush();

        // Catch up
        secondary.TryCatchUpWithPrimary();
        Assert.Equal("v2", secondary.GetString("k2"));
    }
}
