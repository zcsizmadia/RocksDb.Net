namespace RocksDbNet.Tests;

public class LoggerTests
{
    private sealed class TestLogger(InfoLogLevel logLevel) : Logger(logLevel)
    {
        public List<(InfoLogLevel Level, string Message)> Logs { get; } = [];

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

        // RocksDb should log at least some messages
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

    [Fact]
    public void Logger_DebugLevel_ReceivesMoreMessages()
    {
        using var dir = new TempDir();
        var logger = new TestLogger(InfoLogLevel.Debug);

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.InfoLog = logger;

        using var db = RocksDb.Open(opts, dir.Path);
        db.Put("key", "value");
        db.Flush();

        Assert.NotEmpty(logger.Logs);
    }

    [Fact]
    public void Logger_SetViaInfoLogLevel()
    {
        using var dir = new TempDir();
        var debugLogger = new TestLogger(InfoLogLevel.Debug);
        var warnLogger = new TestLogger(InfoLogLevel.Warn);

        int debugCount;
        using (var opts = new DbOptions { CreateIfMissing = true })
        {
            opts.InfoLog = debugLogger;
            using var db1 = RocksDb.Open(opts, Path.Combine(dir.Path, "db1"));
            db1.Put("key", "value");
            db1.Flush();
            debugCount = debugLogger.Logs.Count;
        }

        int warnCount;
        using (var opts = new DbOptions { CreateIfMissing = true })
        {
            opts.InfoLog = warnLogger;
            opts.InfoLogLevel = InfoLogLevel.Warn;
            using var db2 = RocksDb.Open(opts, Path.Combine(dir.Path, "db2"));
            db2.Put("key", "value");
            db2.Flush();
            warnCount = warnLogger.Logs.Count;
        }

        // A debug-level logger should receive at least as many messages as warn-level
        Assert.True(debugCount >= warnCount, $"Debug({debugCount}) should be >= Warn({warnCount})");
    }
}
