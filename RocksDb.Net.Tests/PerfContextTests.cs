namespace RocksDbNet.Tests;

/// <summary>
/// Per-operation profiling counters. See issue #70.
/// </summary>
/// <remarks>
/// The perf level is thread-local, and xUnit runs each test body on one thread,
/// so each test sets the level it needs and restores counting afterwards.
/// </remarks>
public class PerfContextTests
{
    /// <summary>
    /// Sets a perf level for the duration of a test and puts it back to
    /// RocksDb's default afterwards.
    /// </summary>
    private sealed class LevelScope : IDisposable
    {
        public LevelScope(PerfLevel level) => PerfContext.SetLevel(level);

        public void Dispose() => PerfContext.SetLevel(PerfLevel.EnableCount);
    }

    [Fact]
    public void GetMetric_CountsKeyComparisonsForOneRead()
    {
        using var scope = new LevelScope(PerfLevel.EnableCount);
        using var db = new TempDb();

        for (int i = 0; i < 100; i++)
        {
            db.Db.Put($"key{i:D3}", "value");
        }

        db.Db.Flush();

        using PerfContext perf = PerfContext.CreateForCurrentThread();
        perf.Reset();

        Assert.Equal("value", db.Db.GetString("key050"));

        // A read has to compare keys, so this must have moved.
        Assert.True(
            perf.GetMetric(PerfMetric.UserKeyComparisonCount) > 0,
            "reading a key should compare keys");
    }

    [Fact]
    public void Reset_ZeroesTheCounters()
    {
        using var scope = new LevelScope(PerfLevel.EnableCount);
        using var db = new TempDb();

        db.Db.Put("key", "value");
        db.Db.Flush();

        using PerfContext perf = PerfContext.CreateForCurrentThread();

        _ = db.Db.GetString("key");
        Assert.True(perf.GetMetric(PerfMetric.UserKeyComparisonCount) > 0);

        perf.Reset();

        Assert.Equal(0UL, perf.GetMetric(PerfMetric.UserKeyComparisonCount));
    }

    /// <summary>
    /// Reading from an SST rather than the memtable must register a block read,
    /// which is the counter that makes this feature worth having.
    /// </summary>
    [Fact]
    public void GetMetric_CountsBlockReadsWhenServedFromDisk()
    {
        using var scope = new LevelScope(PerfLevel.EnableCount);

        using var dir = new TempDir();
        using var cache = Cache.CreateLru(1024);
        using var tableOptions = new BlockBasedTableOptions();
        tableOptions.SetBlockCache(cache);

        var opts = new DbOptions { CreateIfMissing = true };
        opts.BlockBasedTableFactory = tableOptions;

        using var db = RocksDb.Open(opts, dir.Path);

        for (int i = 0; i < 500; i++)
        {
            db.Put($"key{i:D4}", new string('v', 512));
        }

        db.Flush();

        using PerfContext perf = PerfContext.CreateForCurrentThread();
        perf.Reset();

        for (int i = 0; i < 100; i++)
        {
            _ = db.GetString($"key{i:D4}");
        }

        Assert.True(perf.GetMetric(PerfMetric.BlockReadCount) > 0, "reads from an SST should read blocks");
        Assert.True(perf.GetMetric(PerfMetric.BlockReadByte) > 0, "and should account the bytes");
    }

    /// <summary>
    /// Timing counters need a higher level, so at the counting level they stay
    /// at zero while counts do not. That difference is what the levels mean.
    /// </summary>
    [Fact]
    public void Levels_ControlWhetherTimingIsCollected()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");
        db.Db.Flush();

        ulong countAtCountLevel;
        using (new LevelScope(PerfLevel.EnableCount))
        using (PerfContext perf = PerfContext.CreateForCurrentThread())
        {
            perf.Reset();
            _ = db.Db.GetString("key");
            countAtCountLevel = perf.GetMetric(PerfMetric.UserKeyComparisonCount);
        }

        Assert.True(countAtCountLevel > 0);

        using (new LevelScope(PerfLevel.EnableTime))
        using (PerfContext perf = PerfContext.CreateForCurrentThread())
        {
            perf.Reset();
            for (int i = 0; i < 50; i++)
            {
                _ = db.Db.GetString("key");
            }

            // Counts still collected at the higher level.
            Assert.True(perf.GetMetric(PerfMetric.UserKeyComparisonCount) > 0);
        }
    }

    [Fact]
    public void Disable_StopsCollecting()
    {
        using var scope = new LevelScope(PerfLevel.EnableCount);
        using var db = new TempDb();
        db.Db.Put("key", "value");
        db.Db.Flush();

        using PerfContext perf = PerfContext.CreateForCurrentThread();

        PerfContext.SetLevel(PerfLevel.Disable);
        perf.Reset();

        for (int i = 0; i < 20; i++)
        {
            _ = db.Db.GetString("key");
        }

        Assert.Equal(0UL, perf.GetMetric(PerfMetric.UserKeyComparisonCount));
    }

    [Fact]
    public void Report_NamesTheCountersThatMoved()
    {
        using var scope = new LevelScope(PerfLevel.EnableCount);
        using var db = new TempDb();
        db.Db.Put("key", "value");
        db.Db.Flush();

        using PerfContext perf = PerfContext.CreateForCurrentThread();
        perf.Reset();
        _ = db.Db.GetString("key");

        string trimmed = perf.Report();
        string everything = perf.Report(excludeZeroCounters: false);

        Assert.Contains("user_key_comparison_count", trimmed, StringComparison.Ordinal);
        Assert.True(everything.Length > trimmed.Length, "including zero counters should produce more output");
    }

    // ── The corrected level values ───────────────────────────────────────────

    /// <summary>
    /// The levels must match RocksDb's C++ enum, not the stale constants in its
    /// C header. Following the C header, 3 means time-except-for-mutex; in the
    /// C++ enum 3 is the wait level, and the native setter casts without
    /// checking, so the wrong level would be selected silently.
    /// </summary>
    [Fact]
    public void PerfLevel_MatchesTheCppHeaderNotTheCHeader()
    {
        Assert.Equal(1, (int)PerfLevel.Disable);
        Assert.Equal(2, (int)PerfLevel.EnableCount);
        Assert.Equal(3, (int)PerfLevel.EnableWait);
        Assert.Equal(4, (int)PerfLevel.EnableTimeExceptForMutex);
        Assert.Equal(5, (int)PerfLevel.EnableTimeAndCpuTimeExceptForMutex);
        Assert.Equal(6, (int)PerfLevel.EnableTime);

        // No Uninitialized (0) or OutOfBounds member: neither is a level a
        // caller should be able to select.
        Assert.Equal(6, Enum.GetValues<PerfLevel>().Length);
    }

    [Fact]
    public void SetLevel_RejectsAnUndefinedValue()
    {
        // The native setter casts without validating, so the guard is here.
        Assert.Throws<ArgumentOutOfRangeException>(() => PerfContext.SetLevel((PerfLevel)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PerfContext.SetLevel((PerfLevel)7));
        Assert.Throws<ArgumentOutOfRangeException>(() => PerfContext.SetLevel((PerfLevel)999));
    }

    [Fact]
    public void GetMetric_RejectsAnUndefinedValue()
    {
        using PerfContext perf = PerfContext.CreateForCurrentThread();

        Assert.Throws<ArgumentOutOfRangeException>(() => perf.GetMetric((PerfMetric)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => perf.GetMetric((PerfMetric)84));
    }

    /// <summary>
    /// The metric numbering is positional in the header, so the ends must be
    /// pinned. There are 84 metrics, whatever the header's own total says.
    /// </summary>
    [Fact]
    public void PerfMetric_NumberingMatchesTheHeader()
    {
        Assert.Equal(0, (int)PerfMetric.UserKeyComparisonCount);
        Assert.Equal(1, (int)PerfMetric.BlockCacheHitCount);
        Assert.Equal(82, (int)PerfMetric.MetadataBlockReadByte);

        // 83 is absent: RocksDb names rocksdb_blob_cache_read_byte but its C
        // accessor has no case for it, so it could only ever read back as zero.
        Assert.Equal(83, Enum.GetValues<PerfMetric>().Length);
        Assert.DoesNotContain(83, Enum.GetValues<PerfMetric>().Select(v => (int)v));
    }

    // ── Thread affinity ──────────────────────────────────────────────────────

    /// <summary>
    /// The counters live in thread-local storage, so an instance used from
    /// another thread would report the wrong thread's work. It throws instead.
    /// </summary>
    [Fact]
    public void UsingFromAnotherThread_Throws()
    {
        using PerfContext perf = PerfContext.CreateForCurrentThread();

        Exception? captured = null;

        // A plain thread rather than a task, so the test can join it without
        // tripping the blocking-task analyzer.
        var other = new Thread(() =>
        {
            captured = Record.Exception(() => perf.GetMetric(PerfMetric.UserKeyComparisonCount));
        })
        {
            IsBackground = true,
        };

        other.Start();
        other.Join(TimeSpan.FromSeconds(30));

        InvalidOperationException ex = Assert.IsType<InvalidOperationException>(captured);
        Assert.Contains("thread-local", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryMemberChecksTheThread()
    {
        using PerfContext perf = PerfContext.CreateForCurrentThread();

        var failures = new List<Exception?>();
        var other = new Thread(() =>
        {
            failures.Add(Record.Exception(perf.Reset));
            failures.Add(Record.Exception(() => perf.Report()));
            failures.Add(Record.Exception(() => perf.GetMetric(PerfMetric.BlockReadCount)));
        })
        {
            IsBackground = true,
        };

        other.Start();
        other.Join(TimeSpan.FromSeconds(30));

        Assert.Equal(3, failures.Count);
        Assert.All(failures, f => Assert.IsType<InvalidOperationException>(f));
    }

    [Fact]
    public void AfterDispose_Throws()
    {
        PerfContext perf = PerfContext.CreateForCurrentThread();
        perf.Dispose();

        Assert.Throws<ObjectDisposedException>(perf.Reset);
        Assert.Throws<ObjectDisposedException>(() => perf.GetMetric(PerfMetric.BlockReadCount));
        Assert.Throws<ObjectDisposedException>(() => perf.Report());
    }

    /// <summary>
    /// Two instances on the same thread see the same underlying counters,
    /// because the context belongs to the thread rather than to the object.
    /// </summary>
    [Fact]
    public void TwoInstancesOnOneThread_ShareTheCounters()
    {
        using var scope = new LevelScope(PerfLevel.EnableCount);
        using var db = new TempDb();
        db.Db.Put("key", "value");
        db.Db.Flush();

        using PerfContext first = PerfContext.CreateForCurrentThread();
        using PerfContext second = PerfContext.CreateForCurrentThread();

        first.Reset();
        _ = db.Db.GetString("key");

        Assert.Equal(
            first.GetMetric(PerfMetric.UserKeyComparisonCount),
            second.GetMetric(PerfMetric.UserKeyComparisonCount));

        // And resetting through one is visible through the other.
        second.Reset();
        Assert.Equal(0UL, first.GetMetric(PerfMetric.UserKeyComparisonCount));
    }
}
