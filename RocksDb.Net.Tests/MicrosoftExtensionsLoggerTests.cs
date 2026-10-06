using Microsoft.Extensions.Logging;

namespace RocksDbNet.Tests;

/// <summary>RocksDb's info log through Microsoft.Extensions.Logging. See issue #191.</summary>
public class MicrosoftExtensionsLoggerTests
{
    private sealed class RecordingLogger(LogLevel minimum, bool throwOnLog = false) : ILogger, ILoggerFactory
    {
        private readonly object _gate = new();
        private readonly List<(LogLevel Level, string Message, EventId EventId)> _entries = [];

        public string? Category { get; private set; }

        public IReadOnlyList<(LogLevel Level, string Message, EventId EventId)> Entries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _entries];
                }
            }
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (throwOnLog)
            {
                throw new InvalidOperationException("logger boom");
            }

            lock (_gate)
            {
                _entries.Add((logLevel, formatter(state, exception), eventId));
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public ILogger CreateLogger(string categoryName)
        {
            Category = categoryName;
            return this;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    [Theory]
    [InlineData(InfoLogLevel.Debug, LogLevel.Debug)]
    [InlineData(InfoLogLevel.Info, LogLevel.Information)]
    [InlineData(InfoLogLevel.Warn, LogLevel.Warning)]
    [InlineData(InfoLogLevel.Error, LogLevel.Error)]
    [InlineData(InfoLogLevel.Fatal, LogLevel.Critical)]
    [InlineData(InfoLogLevel.Header, LogLevel.Debug)]
    public void Levels_MapAsDocumented(InfoLogLevel rocksDb, LogLevel expected)
        => Assert.Equal(expected, MicrosoftExtensionsLogger.ToLogLevel(rocksDb));

    /// <summary>RocksDb's text is passed through, braces and all, rather than read as a template.</summary>
    [Fact]
    public void Messages_KeepTheirBraces()
    {
        var recorder = new RecordingLogger(LogLevel.Trace);
        using var logger = new MicrosoftExtensionsLogger(recorder);

        logger.Log(InfoLogLevel.Warn, "compaction {L0 -> L1} at {0}");

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("compaction {L0 -> L1} at {0}", entry.Message);
        Assert.Equal(1, entry.EventId.Id);
    }

    /// <summary>
    /// Opening a database logs through the bridge, including the options dump,
    /// which RocksDb writes without a level and so arrives as Information.
    /// </summary>
    [Fact]
    public void OpeningADatabase_LogsThroughTheBridge()
    {
        var recorder = new RecordingLogger(LogLevel.Debug);

        using (var db = new TempDb(o => o.UseLogging(recorder, "Storage.RocksDb")))
        {
            db.Db.Put("k", "v");
            db.Db.Flush();
        }

        Assert.Equal("Storage.RocksDb", recorder.Category);
        Assert.Contains(recorder.Entries, e => e.Level == LogLevel.Information && e.Message.StartsWith("RocksDB version:", StringComparison.Ordinal));
        Assert.Contains(recorder.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("Options.", StringComparison.Ordinal));
    }

    /// <summary>
    /// A logger with only warnings enabled sets RocksDb's own level to match,
    /// so RocksDb does not produce the informational lines at all.
    /// </summary>
    [Fact]
    public void TheNativeLevel_FollowsTheLowestEnabledLevel()
    {
        var recorder = new RecordingLogger(LogLevel.Warning);

        using (var db = new TempDb(o => o.UseLogging(recorder)))
        {
            db.Db.Put("k", "v");
            db.Db.Flush();
        }

        Assert.DoesNotContain(recorder.Entries, e => e.Level < LogLevel.Warning);
    }

    /// <summary>
    /// The lowest enabled level is what the native logger is created with. The
    /// recorder above filters for itself, so it cannot show this on its own.
    /// </summary>
    [Fact]
    public void TheNativeLevel_IsTheLowestEnabledLevel()
    {
        Assert.Equal(LogLevel.Warning, LowestNativeLevel(new RecordingLogger(LogLevel.Warning)));
        Assert.Equal(LogLevel.Debug, LowestNativeLevel(new RecordingLogger(LogLevel.Trace)));
        Assert.Equal(LogLevel.Critical, LowestNativeLevel(new RecordingLogger(LogLevel.None)));
    }

    private static LogLevel LowestNativeLevel(RecordingLogger logger)
    {
        using var probe = new MicrosoftExtensionsLogger(logger);
        return MicrosoftExtensionsLogger.ToLogLevel(probe.NativeLevel);
    }

    [Fact]
    public void AnExplicitMinimumLevel_OverridesTheLogger()
    {
        var recorder = new RecordingLogger(LogLevel.Trace);

        using (var db = new TempDb(o =>
        {
            using var logger = new MicrosoftExtensionsLogger(recorder, InfoLogLevel.Error);
            o.InfoLog = logger;
        }))
        {
            db.Db.Put("k", "v");
            db.Db.Flush();
        }

        // Nothing below Error, including the lines RocksDb logs without a
        // level, which its own filtering lets through tagged Info, and the
        // options dump it writes at header level.
        Assert.DoesNotContain(recorder.Entries, e => e.Level < LogLevel.Error);
    }

    [Fact]
    public void ALoggerThatThrows_IsReported_AndTheDatabaseCarriesOn()
    {
        var recorder = new RecordingLogger(LogLevel.Information, throwOnLog: true);
        using var reported = new CallbackExceptionRecorder();

        using (var db = new TempDb(o => o.UseLogging(recorder)))
        {
            db.Db.Put("k", "v");
            db.Db.Flush();
            Assert.Equal("v", db.Db.GetString("k"));
        }

        Assert.Contains(reported.Reported, r => r.Exception is InvalidOperationException { Message: "logger boom" });
    }

    [Fact]
    public void UseLogging_RejectsMissingArguments()
    {
        using var options = new DbOptions();
        var recorder = new RecordingLogger(LogLevel.Information);

        Assert.Throws<ArgumentNullException>(() => options.UseLogging(null!));
        Assert.Throws<ArgumentException>(() => options.UseLogging(recorder, ""));
        Assert.Throws<ArgumentNullException>(() => new MicrosoftExtensionsLogger(null!));
    }
}
