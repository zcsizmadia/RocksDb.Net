namespace RocksDbNet.Tests;

public class RocksDbExceptionTests
{
    [Fact]
    public void Constructor_SetsMessage()
    {
        var ex = new RocksDbException("test error");

        Assert.Equal("test error", ex.Message);
    }

    [Fact]
    public void Constructor_WithInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new RocksDbException("outer", inner);

        Assert.Equal("outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void IsException()
    {
        var ex = new RocksDbException("test");

        Assert.IsAssignableFrom<Exception>(ex);
    }
}
