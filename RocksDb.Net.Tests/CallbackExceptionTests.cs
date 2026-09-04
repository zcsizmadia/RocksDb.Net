namespace RocksDbNet.Tests;

/// <summary>
/// A managed exception that reaches native code terminates the process, so every
/// callback this library installs must contain its own exceptions. See issue #29.
/// </summary>
/// <remarks>
/// These tests are the harness for that: each one throws from a callback and
/// asserts that the process survives, the exception is reported, and the
/// operation degrades in the documented way. The <see cref="Comparator"/> path is
/// not here because it fails fast by design and takes the process with it; it is
/// covered from a child process in <see cref="ComparatorFailFastTests"/>.
/// </remarks>
[Collection(nameof(CallbackExceptionTests))]
public class CallbackExceptionTests
{
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
        using var recorder = new CallbackExceptionRecorder();
        using var filter = new ThrowingCompactionFilter();
        using var db = new TempDb(o => o.CompactionFilter = filter);

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
        public override bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue, IReadOnlyList<byte[]> operands, out byte[] newValue)
            => throw new InvalidOperationException("merge boom");
    }

    [Fact]
    public void MergeOperator_Throwing_FailsMergeAndReports()
    {
        using var recorder = new CallbackExceptionRecorder();
        using var merge = new ThrowingMergeOperator();
        using var db = new TempDb(o => o.MergeOperator = merge);

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
        using var recorder = new CallbackExceptionRecorder();
        using var logger = new ThrowingLogger();
        using var db = new TempDb(o => o.InfoLog = logger);

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
        using var recorder = new CallbackExceptionRecorder();
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
        using var recorder = new CallbackExceptionRecorder();
        using var factory = new ThrowingCompactionFilterFactory();
        using var db = new TempDb(o => o.CompactionFilterFactory = factory);

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
            using var recorder = new CallbackExceptionRecorder();
            using var logger = new ThrowingLogger();
            using var db = new TempDb(o => o.InfoLog = logger);

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
        using var db = new TempDb(o => o.InfoLog = logger);

        db.Db.Put("a", "1");
        db.Db.Flush();

        Assert.Equal("1", db.Db.GetString("a"));
    }

    /// <summary>
    /// The event names the instance that threw, so an application running
    /// several wrappers can tell which one failed.
    /// </summary>
    /// <remarks>
    /// The callback name alone cannot do this. Two compaction filters both
    /// report under "Filter", which is exactly the case here.
    /// </remarks>
    [Fact]
    public void Report_NamesTheInstanceThatThrew()
    {
        using var first = new ThrowingCompactionFilter();
        using var second = new ThrowingCompactionFilter();

        using var forFirst = new CallbackExceptionRecorder(first);
        using var forSecond = new CallbackExceptionRecorder(second);
        using var all = new CallbackExceptionRecorder();

        Provoke(first);
        Provoke(second);

        // Each recorder saw its own instance and nothing else.
        Assert.NotEmpty(forFirst.Reported);
        Assert.NotEmpty(forSecond.Reported);

        // The unfiltered recorder saw at least both, confirming the filtering
        // above narrowed rather than dropped.
        Assert.True(
            all.Reported.Count >= forFirst.Reported.Count + forSecond.Reported.Count,
            "the unfiltered recorder should see at least what the filtered ones did");
    }

    private static void Provoke(CompactionFilter filter)
    {
        using var db = new TempDb(o => o.CompactionFilter = filter);

        db.Db.Put("a", "1");
        db.Db.Flush();
        db.Db.CompactRange();
    }

    // ── Callbacks whose fallback had never been exercised ───────────────────

    private sealed class ThrowingPartialMergeOperator() : MergeOperator("throwing-partial-merge")
    {
        public override bool FullMerge(
            ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue,
            IReadOnlyList<byte[]> operands, out byte[] newValue)
        {
            // Deliberately works, so the only thing that can fail is the partial
            // merge below.
            var merged = new List<byte>();

            if (hasExistingValue)
            {
                merged.AddRange(existingValue.ToArray());
            }

            foreach (byte[] operand in operands)
            {
                merged.AddRange(operand);
            }

            newValue = [.. merged];
            return true;
        }

        public override bool PartialMerge(
            ReadOnlySpan<byte> key, IReadOnlyList<byte[]> operands, out byte[] newValue)
            => throw new InvalidOperationException("partial merge boom");
    }

    /// <summary>
    /// A partial merge that throws costs nothing but the optimisation: RocksDb
    /// keeps the operands and the full merge still produces the right answer.
    /// </summary>
    /// <remarks>
    /// The fallback returns a null pointer, which is how the C API spells "this
    /// partial merge did not happen". Unlike a full merge that fails, that is not
    /// an error: combining operands early is an optimisation RocksDb is free to
    /// skip. Nothing had ever taken this path.
    /// </remarks>
    [Fact]
    public void PartialMerge_Throwing_LeavesTheOperandsAndReports()
    {
        using var recorder = new CallbackExceptionRecorder();
        using var merge = new ThrowingPartialMergeOperator();
        using var db = new TempDb(o => o.MergeOperator = merge);

        db.Db.Merge("k", "a");
        db.Db.Merge("k", "b");
        db.Db.Merge("k", "c");

        // A compaction is what asks for a partial merge.
        db.Db.Flush();
        db.Db.CompactRange();

        // The value is still right, because the full merge answered instead.
        Assert.Equal("abc", db.Db.GetString("k"));

        Assert.Contains(
            recorder.Reported,
            r => r.CallbackName == nameof(MergeOperator.PartialMerge) && r.Exception is InvalidOperationException);

        Assert.All(recorder.Reported, r => Assert.False(r.IsFatal));
    }

    private sealed class ThrowingLogNumberMapFilter() : WalFilter("throwing-log-number-map")
    {
        public int RecordsSeen;

        protected override void OnColumnFamilyLogNumberMap(
            IReadOnlyDictionary<uint, ulong> logNumbersByColumnFamilyId,
            IReadOnlyDictionary<string, uint> columnFamilyIdsByName)
            => throw new InvalidOperationException("log number map boom");

        protected override WalProcessingOption LogRecordFound(
            ulong logNumber,
            string logFileName,
            WriteBatch batch,
            WriteBatch replacementBatch,
            ref bool batchChanged)
        {
            Interlocked.Increment(ref RecordsSeen);
            return WalProcessingOption.ContinueProcessing;
        }
    }

    /// <summary>
    /// The log-number map is handed over before recovery starts and has no way
    /// to refuse, so a filter that throws there still gets its records and the
    /// database still opens.
    /// </summary>
    [Fact]
    public void WalFilterLogNumberMap_Throwing_StillRecoversAndReports()
    {
        using var recorder = new CallbackExceptionRecorder();
        using var dir = new TempDir();

        TestDb.WriteRecordsLeftInTheWal(dir.Path, ("a", "1"), ("b", "2"));

        var filter = new ThrowingLogNumberMapFilter();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetWalFilter(filter);

        using var db = RocksDb.Open(opts, dir.Path);

        // Recovery carried on regardless: the records arrived and were applied.
        Assert.Equal(2, filter.RecordsSeen);
        Assert.Equal("1", db.GetString("a"));
        Assert.Equal("2", db.GetString("b"));

        Assert.Contains(
            recorder.Reported,
            r => r.CallbackName == "OnColumnFamilyLogNumberMap" && r.Exception is InvalidOperationException);

        Assert.All(recorder.Reported, r => Assert.False(r.IsFatal));
    }

    // MergeOperator's DeleteValue callback has a catch of its own that nothing
    // here exercises, and deliberately so: all it guards is a FreeHGlobal of a
    // pointer the library allocated itself moments earlier. Reaching the catch
    // would mean handing it a pointer the library did not allocate, which is a
    // corrupt process rather than a test.
}
