namespace RocksDbNet.Tests;

public class RocksDbHandleTests
{
    [Fact]
    public void Dispose_SetsIsDisposed()
    {
        var opts = new DbOptions();
        Assert.False(opts.IsDisposed);

        opts.Dispose();
        Assert.True(opts.IsDisposed);
    }

    [Fact]
    public void Dispose_Idempotent()
    {
        var opts = new DbOptions();

        opts.Dispose();
        opts.Dispose(); // Should not throw
    }

    [Fact]
    public void ThrowIfDisposed_ThrowsAfterDispose()
    {
        var opts = new DbOptions();
        opts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => opts.ThrowIfDisposed());
    }

    [Fact]
    public void ThrowIfDisposed_DoesNotThrowBeforeDispose()
    {
        using var opts = new DbOptions();

        opts.ThrowIfDisposed(); // Should not throw
    }

    [Fact]
    public void Handle_ZeroAfterDispose()
    {
        var opts = new DbOptions();
        opts.Dispose();

        Assert.Equal(IntPtr.Zero, opts.Handle);
    }
}
