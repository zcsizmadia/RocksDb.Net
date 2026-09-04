namespace RocksDbNet.Tests;

public class LoggerTests
{
    /// <summary>Records what RocksDb logged, from whichever thread logged it.</summary>
    /// <remarks>
    /// Both sides lock. Flush and compaction log from background threads, and
    /// this used to append to a plain List with no synchronisation at all.
    /// </remarks>
    private sealed class TestLogger(InfoLogLevel logLevel) : Logger(logLevel)
    {
        private readonly object _gate = new();
        private readonly List<(InfoLogLevel Level, string Message)> _logs = [];

        public IReadOnlyList<(InfoLogLevel Level, string Message)> Logs
        {
            get
            {
                lock (_gate)
                {
                    return [.. _logs];
                }
            }
        }

        public override void Log(InfoLogLevel logLevel, string message)
        {
            lock (_gate)
            {
                _logs.Add((logLevel, message));
            }
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

    /// <summary>Opens a database, writes and flushes, and reports what was logged.</summary>
    private static TestLogger LogsFrom(string path, InfoLogLevel loggerLevel, InfoLogLevel? optionsLevel = null)
    {
        var logger = new TestLogger(loggerLevel);

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.InfoLog = logger;

        if (optionsLevel is not null)
        {
            opts.InfoLogLevel = optionsLevel.Value;
        }

        using (RocksDb db = RocksDb.Open(opts, path))
        {
            db.Put("key", "value");
            db.Flush();
        }

        return logger;
    }

    /// <summary>
    /// A logger constructed at a higher level receives strictly fewer messages,
    /// and none below its level that RocksDb tags with one.
    /// </summary>
    /// <remarks>
    /// This used to assert only that the debug count was greater than or equal
    /// to the warn count, which equal counts satisfy, so it held even if the
    /// level were ignored entirely. Measured here: 383 messages at Debug against
    /// 355 at Warn.
    /// </remarks>
    [Fact]
    public void Logger_LevelFiltersOutTheLevelsBelowIt()
    {
        using var dir = new TempDir();

        TestLogger debug = LogsFrom(Path.Combine(dir.Path, "debug"), InfoLogLevel.Debug);
        TestLogger warn = LogsFrom(Path.Combine(dir.Path, "warn"), InfoLogLevel.Warn);

        Assert.NotEmpty(debug.Logs);
        Assert.NotEmpty(warn.Logs);

        Assert.True(
            debug.Logs.Count > warn.Logs.Count,
            $"a debug logger saw {debug.Logs.Count} messages and a warn logger {warn.Logs.Count}");

        // The debug logger sees debug messages and the warn logger sees none,
        // which is the difference between them.
        Assert.Contains(debug.Logs, l => l.Level == InfoLogLevel.Debug);
        Assert.DoesNotContain(warn.Logs, l => l.Level == InfoLogLevel.Debug);
    }

    /// <summary>
    /// Messages below the level a logger was constructed with still reach it, so
    /// a logger that cares has to filter for itself.
    /// </summary>
    /// <remarks>
    /// Measured: a logger constructed at Warn received 354 messages tagged Info
    /// and one tagged Error. RocksDb logs a great deal through a call that
    /// carries no level, and those arrive tagged Info whatever the logger asked
    /// for. Only the calls that do carry a level are filtered, which is why the
    /// test above still sees a difference.
    /// </remarks>
    [Fact]
    public void Logger_StillReceivesMessagesBelowItsLevel()
    {
        using var dir = new TempDir();

        TestLogger warn = LogsFrom(Path.Combine(dir.Path, "warn"), InfoLogLevel.Warn);

        Assert.Contains(warn.Logs, l => l.Level < InfoLogLevel.Warn);
    }

    /// <summary>
    /// <see cref="DbOptions.InfoLogLevel"/> does not filter a custom logger at
    /// all. The level a logger is constructed with is the only one that counts.
    /// </summary>
    /// <remarks>
    /// Measured: identical message counts with the option left alone and with it
    /// set to Warn, at 383 each, against 355 for a logger constructed at Warn.
    /// Worth a test of its own because the option looks like the way to control
    /// this and quietly is not. See issue #129.
    /// </remarks>
    [Fact]
    public void InfoLogLevel_DoesNotFilterACustomLogger()
    {
        using var dir = new TempDir();

        TestLogger untouched = LogsFrom(Path.Combine(dir.Path, "untouched"), InfoLogLevel.Debug);

        TestLogger optionSet = LogsFrom(
            Path.Combine(dir.Path, "option-set"), InfoLogLevel.Debug, InfoLogLevel.Warn);

        Assert.Contains(optionSet.Logs, l => l.Level < InfoLogLevel.Warn);

        // Not merely "not fewer": the same. Both counts come from the same work
        // against a fresh database, so they match exactly.
        Assert.Equal(untouched.Logs.Count, optionSet.Logs.Count);
    }
}
