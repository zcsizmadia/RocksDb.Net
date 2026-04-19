namespace RocksDbNet.Tests;

public class FlushOptionsTests
{
    [Fact]
    public void Create_DefaultWait()
    {
        using var opts = new FlushOptions();
        // Default wait is true in RocksDB
        Assert.True(opts.Wait);
    }

    [Fact]
    public void Wait_GetSet()
    {
        using var opts = new FlushOptions();

        opts.Wait = false;
        Assert.False(opts.Wait);

        opts.Wait = true;
        Assert.True(opts.Wait);
    }
}
