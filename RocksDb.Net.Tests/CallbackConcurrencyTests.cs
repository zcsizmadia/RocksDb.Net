namespace RocksDbNet.Tests;

/// <summary>
/// Exercises the pinned-state pattern that every callback wrapper relies on,
/// under concurrent invocation and garbage collection pressure. See issue #31.
/// </summary>
/// <remarks>
/// A single wrapper instance is reachable from RocksDb background threads
/// through a <c>GCHandle</c> and a raw state pointer. Two things could go wrong
/// and neither shows up in a single-threaded test: the handle could fail to
/// resolve back to the right object when several threads call at once, and the
/// pin could fail to keep the object alive across a collection while native
/// code still holds the pointer.
/// </remarks>
public class CallbackConcurrencyTests
{
    /// <summary>
    /// Records how the callback was invoked, and verifies on every call that the
    /// pinned state resolved to this instance with its fields intact.
    /// </summary>
    private sealed class ConcurrencyProbe
    {
        // A value the callback checks on every invocation. If the pinned state
        // ever resolved to the wrong object, or to a collected one, this would
        // not read back correctly.
        private const int ExpectedSentinel = 0x5A5A5A5A;

        private readonly int _sentinel = ExpectedSentinel;
        private readonly object _gate = new();
        private readonly HashSet<int> _threadIds = [];

        private int _calls;
        private int _inFlight;
        private int _maxInFlight;
        private int _sentinelFailures;

        public int Calls => Volatile.Read(ref _calls);

        public int MaxConcurrentCalls => Volatile.Read(ref _maxInFlight);

        public int SentinelFailures => Volatile.Read(ref _sentinelFailures);

        public int DistinctThreads
        {
            get { lock (_gate) { return _threadIds.Count; } }
        }

        /// <summary>Call at the top of a callback, and dispose at the bottom.</summary>
        public Scope Enter(object resolvedInstance)
        {
            if (!ReferenceEquals(resolvedInstance, Owner) || _sentinel != ExpectedSentinel)
            {
                Interlocked.Increment(ref _sentinelFailures);
            }

            Interlocked.Increment(ref _calls);

            int current = Interlocked.Increment(ref _inFlight);

            // Track the high-water mark of simultaneous callbacks without a lock
            // on the hot path.
            int observed = Volatile.Read(ref _maxInFlight);
            while (current > observed)
            {
                int previous = Interlocked.CompareExchange(ref _maxInFlight, current, observed);
                if (previous == observed)
                {
                    break;
                }

                observed = previous;
            }

            lock (_gate)
            {
                _threadIds.Add(Environment.CurrentManagedThreadId);
            }

            return new Scope(this);
        }

        /// <summary>The object the pinned state is expected to resolve to.</summary>
        public object? Owner { get; set; }

        public readonly struct Scope(ConcurrencyProbe probe) : IDisposable
        {
            public void Dispose() => Interlocked.Decrement(ref probe._inFlight);
        }
    }

    private sealed class ProbedCompactionFilter : CompactionFilter
    {
        public ProbedCompactionFilter(ConcurrencyProbe probe)
            : base("probed-filter")
        {
            Probe = probe;
            probe.Owner = this;
        }

        public ConcurrencyProbe Probe { get; }

        protected override FilterDecision Filter(
            int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            using ConcurrencyProbe.Scope _ = Probe.Enter(this);

            newValue = null;

            // Hold the callback open briefly so overlapping invocations actually
            // overlap, rather than each finishing before the next begins.
            Thread.SpinWait(200);

            return FilterDecision.Keep;
        }
    }

    private sealed class ProbedListener : EventListener
    {
        public ProbedListener(ConcurrencyProbe probe)
        {
            Probe = probe;
            probe.Owner = this;
        }

        public ConcurrencyProbe Probe { get; }

        public override void OnFlushCompleted(FlushJobInfo info)
        {
            using ConcurrencyProbe.Scope _ = Probe.Enter(this);
            Thread.SpinWait(200);
        }
    }

    /// <summary>
    /// Writes from several threads at once with a small write buffer, so RocksDb
    /// runs many flushes and compactions in parallel and the single filter
    /// instance is called from several background threads.
    /// </summary>
    private static void DriveParallelBackgroundWork(RocksDb db, int threads = 4, int writesPerThread = 400)
    {
        Parallel.For(0, threads, t =>
        {
            for (int i = 0; i < writesPerThread; i++)
            {
                db.Put($"t{t}-key{i:D5}", new string('v', 512));
            }
        });

        db.Flush();
        db.CompactRange();
    }

    [Fact]
    public void CompactionFilter_UnderParallelBackgroundWork_ResolvesTheSameInstance()
    {
        using var dir = new TempDir();

        var probe = new ConcurrencyProbe();
        using var filter = new ProbedCompactionFilter(probe);

        // Filtered to this instance. Other test classes run in parallel and
        // throw from callbacks of the same names on purpose, so an unfiltered
        // recorder would make the no-exception assertion below fail at random.
        using var reported = new CallbackExceptionRecorder(filter);

        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            // Small buffers and a low trigger produce many small files, so there
            // is real background work to parallelise.
            WriteBufferSize = 64 * 1024,
            MaxWriteBufferNumber = 2,
            Level0FileNumCompactionTrigger = 2,
            MaxBackgroundJobs = 8,
            MaxSubcompactions = 4,
        };
        opts.CompactionFilter = filter;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            DriveParallelBackgroundWork(db);
        }

        Assert.True(probe.Calls > 0, "the filter should have been invoked");

        // Every invocation resolved the pinned state back to this instance, with
        // its fields intact.
        Assert.Equal(0, probe.SentinelFailures);
        Assert.Empty(reported.Reported);

        // Deliberately no assertion on MaxConcurrentCalls. RocksDb decides
        // whether to run this work in parallel, and measured over four runs the
        // observed maximum was 2, 2, 1, 1 with three to four distinct threads.
        // Requiring overlap here would be flaky; PinnedState_ResolvesCorrectly-
        // UnderContention covers concurrent resolution deterministically instead.
    }

    [Fact]
    public void EventListener_UnderParallelBackgroundWork_ResolvesTheSameInstance()
    {
        using var dir = new TempDir();

        var probe = new ConcurrencyProbe();
        var listener = new ProbedListener(probe);

        // Filtered to this instance. Other test classes run in parallel and
        // throw from callbacks of the same names on purpose, so an unfiltered
        // recorder would make the no-exception assertion below fail at random.
        using var reported = new CallbackExceptionRecorder(listener);

        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            WriteBufferSize = 64 * 1024,
            MaxWriteBufferNumber = 2,
            Level0FileNumCompactionTrigger = 2,
            MaxBackgroundJobs = 8,
        };
        opts.AddEventListener(listener);

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            DriveParallelBackgroundWork(db);
        }

        Assert.True(probe.Calls > 0, "the listener should have been invoked");
        Assert.Equal(0, probe.SentinelFailures);
        Assert.Empty(reported.Reported);
    }

    /// <summary>
    /// The pin has to keep the wrapper alive while native code holds the state
    /// pointer. Collecting aggressively during compaction is what would expose a
    /// missing or premature unpin.
    /// </summary>
    [Fact]
    public void CompactionFilter_SurvivesCollectionDuringCompaction()
    {
        using var dir = new TempDir();

        var probe = new ConcurrencyProbe();
        using var filter = new ProbedCompactionFilter(probe);

        // Filtered to this instance. Other test classes run in parallel and
        // throw from callbacks of the same names on purpose, so an unfiltered
        // recorder would make the no-exception assertion below fail at random.
        using var reported = new CallbackExceptionRecorder(filter);

        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            WriteBufferSize = 64 * 1024,
            MaxWriteBufferNumber = 2,
            Level0FileNumCompactionTrigger = 2,
            MaxBackgroundJobs = 4,
        };
        opts.CompactionFilter = filter;

        // A plain thread rather than a Task, so the test can join it without the
        // blocking-task-in-a-test analyzer warning.
        using var collecting = new CancellationTokenSource();
        var collector = new Thread(() =>
        {
            while (!collecting.IsCancellationRequested)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
            }
        })
        {
            IsBackground = true,
            Name = "gc-pressure",
        };
        collector.Start();

        try
        {
            using var db = RocksDb.Open(opts, dir.Path);

            for (int i = 0; i < 1_500; i++)
            {
                db.Put($"key{i:D6}", new string('v', 512));
            }

            db.Flush();
            db.CompactRange();

            Assert.True(probe.Calls > 0, "the filter should have been invoked");
            Assert.Equal(0, probe.SentinelFailures);
        }
        finally
        {
            collecting.Cancel();
            collector.Join(TimeSpan.FromSeconds(30));
        }

        Assert.Empty(reported.Reported);
    }

    /// <summary>
    /// Resolves the pinned state from many managed threads at once and asserts
    /// it always comes back as the right instance.
    /// </summary>
    /// <remarks>
    /// This is the deterministic half of the concurrency coverage. The tests
    /// above drive real RocksDb background work, but RocksDb decides how much of
    /// it to parallelise, so overlap cannot be asserted there without
    /// flakiness. Here the contention is produced directly, so every run
    /// genuinely exercises concurrent resolution of the same
    /// <c>GCHandle</c>-backed state pointer that every callback uses.
    /// </remarks>
    [Fact]
    public void PinnedState_ResolvesCorrectlyUnderContention()
    {
        var handle = new ContendedHandle();
        handle.Pin();

        try
        {
            nint state = handle.State;

            const int threads = 8;
            const int resolutionsPerThread = 20_000;
            int failures = 0;

            Parallel.For(0, threads, _ =>
            {
                for (int i = 0; i < resolutionsPerThread; i++)
                {
                    if (!ReferenceEquals(ContendedHandle.Resolve(state), handle))
                    {
                        Interlocked.Increment(ref failures);
                    }
                }
            });

            Assert.Equal(0, failures);
        }
        finally
        {
            handle.UnpinGarbageCollector();
        }
    }

    /// <summary>
    /// Exposes the protected pinning helpers, which are reachable only from a
    /// derived type, exactly as a real callback wrapper does.
    /// </summary>
    private sealed class ContendedHandle : RocksDbHandle
    {
        protected override void DisposeHandle()
        {
        }

        public void Pin() => PinGarbageCollector("contended");

        public nint State => GetPinnedIntPtr();

        public static ContendedHandle Resolve(nint state) => GetSelfFromPinnedIntPtr<ContendedHandle>(state);
    }

    /// <summary>
    /// Data must survive all of the above. A filter that resolved the wrong
    /// instance could silently drop or rewrite entries, which the sentinel check
    /// alone would not prove.
    /// </summary>
    [Fact]
    public void ParallelBackgroundWork_PreservesEveryWrite()
    {
        using var dir = new TempDir();

        var probe = new ConcurrencyProbe();
        using var filter = new ProbedCompactionFilter(probe);

        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            WriteBufferSize = 64 * 1024,
            MaxWriteBufferNumber = 2,
            Level0FileNumCompactionTrigger = 2,
            MaxBackgroundJobs = 8,
            MaxSubcompactions = 4,
        };
        opts.CompactionFilter = filter;

        const int threads = 4;
        const int writesPerThread = 400;

        using var db = RocksDb.Open(opts, dir.Path);
        DriveParallelBackgroundWork(db, threads, writesPerThread);

        string expected = new('v', 512);
        for (int t = 0; t < threads; t++)
        {
            for (int i = 0; i < writesPerThread; i++)
            {
                Assert.Equal(expected, db.GetString($"t{t}-key{i:D5}"));
            }
        }
    }
}
