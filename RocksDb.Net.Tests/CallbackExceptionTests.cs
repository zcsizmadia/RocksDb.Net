namespace RocksDbNet.Tests;

/// <summary>
/// A managed exception that reaches native code terminates the process, so every
/// callback this library installs must contain its own exceptions. See issue #29.
/// </summary>
/// <remarks>
/// These tests are the harness for that: each one throws from a callback and
/// asserts that the process survives, the exception is reported, and the
/// operation degrades in the documented way. The <see cref="Comparator"/> path is
/// deliberately not covered here because it fails fast by design; that is
/// asserted in <see cref="ComparatorTests"/> instead.
/// </remarks>
[Collection(nameof(CallbackExceptionTests))]
public class CallbackExceptionTests
{
    /// <summary>
    /// Subscribes to the reporting event for the duration of a test and collects
    /// what was reported. The event is process-wide, hence the test collection.
    /// </summary>
    private sealed class ExceptionRecorder : IDisposable
    {
        private readonly List<CallbackExceptionEventArgs> _reported = [];
        private readonly Lock _gate = new();

        public ExceptionRecorder()
            => RocksDbCallbacks.UnhandledException += OnUnhandled;

        private void OnUnhandled(object? sender, CallbackExceptionEventArgs e)
        {
            lock (_gate)
            {
                _reported.Add(e);
            }
        }

        public IReadOnlyList<CallbackExceptionEventArgs> Reported
        {
            get
            {
                lock (_gate)
                {
                    return [.. _reported];
                }
            }
        }

        public void Dispose()
            => RocksDbCallbacks.UnhandledException -= OnUnhandled;
    }

    private sealed class ThrowingCompactionFilter() : CompactionFilter("throwing-filter")
    {
        public int Calls;

        protected override FilterDecision Filter(int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("filter boom");
        }
    }

    [Fact]
    public void CompactionFilter_Throwing_KeepsDataAndReports()
    {
        using var recorder = new ExceptionRecorder();
        using var filter = new ThrowingCompactionFilter();
        using var db = new TempDb(o => o.SetCompactionFilter(filter));

        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Flush();
        db.Db.CompactRange();

        // The filter threw for every entry, so nothing was filtered: the
        // documented fallback is to keep the entry unchanged.
        Assert.Equal("1", db.Db.GetString("a"));
        Assert.Equal("2", db.Db.GetString("b"));

        Assert.True(filter.Calls > 0, "the filter should have been invoked");
        Assert.Contains(recorder.Reported, r => r.CallbackName == "Filter" && r.Exception is InvalidOperationException);
        Assert.All(recorder.Reported, r => Assert.False(r.IsFatal));
    }

    private sealed class ThrowingMergeOperator() : MergeOperator("throwing-merge")
    {
        public override bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue, IEnumerable<byte[]> operands, out byte[] newValue)
            => throw new InvalidOperationException("merge boom");
    }

    [Fact]
    public void MergeOperator_Throwing_FailsMergeAndReports()
    {
        using var recorder = new ExceptionRecorder();
        using var merge = new ThrowingMergeOperator();
        using var db = new TempDb(o => o.SetMergeOperator(merge));

        db.Db.Merge("k", "v");

        // A merge operator has a real failure channel, so the throw becomes a
        // merge failure that RocksDb surfaces as a corruption error on read,
        // rather than a silently wrong merged value.
        Assert.Throws<RocksDbException>(() => db.Db.GetString("k"));

        Assert.Contains(recorder.Reported, r => r.CallbackName == "FullMerge" && r.Exception is InvalidOperationException);
        Assert.All(recorder.Reported, r => Assert.False(r.IsFatal));
    }

    private sealed class ThrowingLogger() : Logger(InfoLogLevel.Info)
    {
        public override void Log(InfoLogLevel level, string message)
            => throw new InvalidOperationException("log boom");
    }

    [Fact]
    public void Logger_Throwing_DoesNotBreakDatabaseAndReports()
    {
        using var recorder = new ExceptionRecorder();
        using var logger = new ThrowingLogger();
        using var db = new TempDb(o => o.SetInfoLog(logger));

        db.Db.Put("a", "1");
        db.Db.Flush();

        Assert.Equal("1", db.Db.GetString("a"));
        Assert.Contains(recorder.Reported, r => r.CallbackName == "Log" && r.Exception is InvalidOperationException);
    }

    private sealed class ThrowingEventListener : EventListener
    {
        public int Calls;

        public override void OnFlushCompleted(FlushJobInfo info)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("listener boom");
        }
    }

    [Fact]
    public void EventListener_Throwing_DoesNotBreakFlushAndReports()
    {
        using var recorder = new ExceptionRecorder();
        var listener = new ThrowingEventListener();
        using var db = new TempDb(o => o.EventListener = listener);

        db.Db.Put("a", "1");
        db.Db.Flush();

        Assert.Equal("1", db.Db.GetString("a"));
        Assert.True(listener.Calls > 0, "the listener should have been invoked");
        Assert.Contains(recorder.Reported, r => r.CallbackName == nameof(EventListener.OnFlushCompleted) && r.Exception is InvalidOperationException);
    }

    private sealed class ThrowingCompactionFilterFactory() : CompactionFilterFactory("throwing-factory")
    {
        protected override CompactionFilter CreateFilter(CompactionFilterContext context)
            => throw new InvalidOperationException("factory boom");
    }

    [Fact]
    public void CompactionFilterFactory_Throwing_KeepsDataAndReports()
    {
        using var recorder = new ExceptionRecorder();
        using var factory = new ThrowingCompactionFilterFactory();
        using var db = new TempDb(o => o.SetCompactionFilterFactory(factory));

        db.Db.Put("a", "1");
        db.Db.Flush();
        db.Db.CompactRange();

        // A null filter means "no filtering for this job", so the data is intact.
        Assert.Equal("1", db.Db.GetString("a"));
        Assert.Contains(recorder.Reported, r => r.CallbackName == "CreateFilter" && r.Exception is InvalidOperationException);
    }

    [Fact]
    public void Reporter_Throwing_IsIgnored()
    {
        // A failing reporter must not mask the exception it is reporting, nor
        // propagate into native code itself.
        void Faulty(object? sender, CallbackExceptionEventArgs e) => throw new InvalidOperationException("reporter boom");

        RocksDbCallbacks.UnhandledException += Faulty;
        try
        {
            using var recorder = new ExceptionRecorder();
            using var logger = new ThrowingLogger();
            using var db = new TempDb(o => o.SetInfoLog(logger));

            db.Db.Put("a", "1");
            db.Db.Flush();

            Assert.Equal("1", db.Db.GetString("a"));
            Assert.Contains(recorder.Reported, r => r.CallbackName == "Log");
        }
        finally
        {
            RocksDbCallbacks.UnhandledException -= Faulty;
        }
    }

    [Fact]
    public void NoSubscriber_StillSurvives()
    {
        // The guard must not depend on anyone listening.
        using var logger = new ThrowingLogger();
        using var db = new TempDb(o => o.SetInfoLog(logger));

        db.Db.Put("a", "1");
        db.Db.Flush();

        Assert.Equal("1", db.Db.GetString("a"));
    }
}
