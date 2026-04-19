using System.Text;

namespace RocksDbNet.Tests;

public class LoggerTests
{
    private sealed class TestLogger : Logger
    {
        public List<(InfoLogLevel Level, string Message)> Logs { get; } = new();

        public TestLogger(InfoLogLevel logLevel) : base(logLevel) { }

        public override void Log(InfoLogLevel logLevel, string message)
        {
            Logs.Add((logLevel, message));
        }
    }

    [Fact]
    public void Logger_ReceivesMessages()
    {
        using var dir = new TempDir();
        var logger = new TestLogger(InfoLogLevel.Info);

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.InfoLog = logger;

        using var db = RocksDb.Open(opts, dir.Path);
        db.Put("key", "value");
        db.Flush();

        // RocksDB should log at least some messages
        Assert.NotEmpty(logger.Logs);
    }

    [Fact]
    public void InfoLogLevel_Values()
    {
        Assert.Equal(0, (int)InfoLogLevel.Debug);
        Assert.Equal(1, (int)InfoLogLevel.Info);
        Assert.Equal(2, (int)InfoLogLevel.Warn);
        Assert.Equal(3, (int)InfoLogLevel.Error);
        Assert.Equal(4, (int)InfoLogLevel.Fatal);
        Assert.Equal(5, (int)InfoLogLevel.Header);
    }
}
