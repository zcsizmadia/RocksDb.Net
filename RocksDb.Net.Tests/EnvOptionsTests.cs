namespace RocksDbNet.Tests;

public class EnvOptionsTests
{
    [Fact]
    public void Create_DoesNotThrow()
    {
        using var opts = new EnvOptions();
        Assert.False(opts.IsDisposed);
    }

    [Fact]
    public void Dispose_SetsDisposed()
    {
        var opts = new EnvOptions();
        opts.Dispose();

        Assert.True(opts.IsDisposed);
    }
}
