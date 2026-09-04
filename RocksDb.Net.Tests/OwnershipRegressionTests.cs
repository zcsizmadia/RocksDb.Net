using System.Runtime.CompilerServices;

namespace RocksDbNet.Tests;

/// <summary>
/// Regressions for the ownership and lifetime defects found by the pre-release
/// independent review.
/// </summary>
/// <remarks>
/// Grouped rather than filed under each subject because they share a cause: a
/// native object whose lifetime the wrapper tracked with the wrong mechanism,
/// or with none. Each test fails if its fix is reverted, which is the point —
/// the review's own finding was that several past fixes had no such test.
/// </remarks>
public class OwnershipRegressionTests
{
    private sealed class ReverseComparator : Comparator
    {
        public ReverseComparator()
            : base("regression.reverse")
        {
        }

        public override int Compare(ReadOnlySpan<byte> keyA, ReadOnlySpan<byte> keyB)
            => keyB.SequenceCompareTo(keyA);
    }

    /// <summary>
    /// Disposing the options while the database is open is deferred, so the
    /// comparator they own survives until the database closes.
    /// </summary>
    /// <remarks>
    /// The database took a hold on the options at open. Without it the options
    /// disposed for real here, which released the comparator, and the next read
    /// went through freed memory — the guides forbid this shape, but forbidding
    /// it was the only thing protecting it.
    /// </remarks>
    [Fact]
    public void DisposingTheOptionsUnderALiveDatabaseIsDeferred()
    {
        using var comparator = new ReverseComparator();

        var options = new DbOptions { CreateIfMissing = true, Comparator = comparator };
        options.Env = Env.CreateInMemory();

        using RocksDb db = RocksDb.Open(options, TestDb.InMemoryPath);

        // The caller lets go while the database is open.
        options.Dispose();
        Assert.False(options.IsDisposed);

        // Reads and writes still go through the comparator.
        db.Put("b", "2");
        db.Put("a", "1");
        db.Put("c", "3");

        using Iterator it = db.NewIterator();
        it.SeekToFirst();

        // Reverse order, so the comparator is demonstrably still the one in use
        // rather than a default that happened to survive.
        Assert.Equal("c", it.KeyAsString());
    }

    /// <summary>
    /// A rate limiter can be reused by a database opened after the first one
    /// using it has closed.
    /// </summary>
    /// <remarks>
    /// RocksDb copies the shared pointer, so assigning registers no hold. It
    /// used to register one, which meant closing the first database released it
    /// and disposed the limiter the caller still held — for an object whose
    /// whole purpose is to be a process-wide I/O budget shared across
    /// databases. Same defect as the blob cache had, and the same fix.
    /// </remarks>
    [Fact]
    public void ARateLimiterCanBeReusedByALaterDatabase()
    {
        using var limiter = new RateLimiter(64 * 1024 * 1024);

        for (int i = 0; i < 3; i++)
        {
            var options = new DbOptions { CreateIfMissing = true, RateLimiter = limiter };
            options.Env = Env.CreateInMemory();

            using RocksDb db = RocksDb.Open(options, TestDb.InMemoryPath);

            db.Put("k", new string('v', 4096));
            db.Flush();

            Assert.Equal(new string('v', 4096), db.GetString("k"));
        }

        Assert.False(limiter.IsDisposed);
    }

    /// <summary>
    /// A checkpoint refuses to run once its database has closed, rather than
    /// reaching into freed memory.
    /// </summary>
    /// <remarks>
    /// <c>rocksdb_checkpoint_object_create</c> hands <c>db-&gt;rep</c> to
    /// <c>Checkpoint::Create</c>, which keeps that pointer for the checkpoint's
    /// whole life. Registering the database as the parent is what turns this
    /// into an exception; it was the only database-derived handle that did not.
    /// </remarks>
    [Fact]
    public void ACheckpointOutlivingItsDatabaseThrowsRatherThanFaulting()
    {
        using var dir = new TempDir();

        using var options = new DbOptions { CreateIfMissing = true };
        RocksDb db = RocksDb.Open(options, dir.Sub("db"));

        Checkpoint checkpoint = Checkpoint.Create(db);

        db.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => checkpoint.CreateCheckpoint(dir.Sub("cp")));
    }

    private sealed class CachingFilterFactory : CompactionFilterFactory
    {
        private readonly CompactionFilter _shared = new KeepEverything();

        public CachingFilterFactory()
            : base("regression.caching")
        {
        }

        // The natural mistake: filters are not free, so a caller caches one.
        // RocksDb wraps whatever this returns in its own unique_ptr per job, so
        // handing back the same pointer twice deleted it twice.
        protected override CompactionFilter CreateFilter(CompactionFilterContext context)
            => _shared;

        private sealed class KeepEverything : CompactionFilter
        {
            public KeepEverything()
                : base("regression.keep")
            {
            }

            protected override FilterDecision Filter(
                int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> existingValue, out byte[]? newValue)
            {
                newValue = null;
                return FilterDecision.Keep;
            }
        }
    }

    /// <summary>
    /// A factory that returns the same filter twice is reported, rather than
    /// corrupting the heap when RocksDb deletes it a second time.
    /// </summary>
    /// <remarks>
    /// The second attachment throws inside the callback, which the callback
    /// boundary catches and reports, and RocksDb treats the resulting null
    /// filter as "no filtering for this job" — so the data is untouched and the
    /// mistake is visible. Before, the second job deleted a pointer the first
    /// had already deleted.
    /// </remarks>
    [Fact]
    public void AFactoryReturningTheSameFilterTwiceIsReported()
    {
        using var recorder = new CallbackExceptionRecorder();
        using var factory = new CachingFilterFactory();

        using var db = new TempDb(o =>
        {
            o.CompactionFilterFactory = factory;
            o.WriteBufferSize = 4096;
            o.Level0FileNumCompactionTrigger = 2;
        });

        // Two flushes and a compaction, so the factory is asked for a filter
        // more than once.
        for (int i = 0; i < 200; i++)
        {
            db.Db.Put($"key{i:D5}", new string('x', 64));
        }

        db.Db.Flush();

        for (int i = 200; i < 400; i++)
        {
            db.Db.Put($"key{i:D5}", new string('y', 64));
        }

        db.Db.Flush();
        db.Db.CompactRange();

        Assert.True(
            Wait.Until(() => recorder.Contains("CreateFilter")),
            "reusing one filter across compaction jobs was not reported");

        // The data survived, which is what makes the degradation safe.
        Assert.Equal(new string('y', 64), db.Db.GetString("key00300"));
    }

    private sealed class LoggerWithAThrowingConstructor : Logger
    {
        public LoggerWithAThrowingConstructor()
            : base(FailBeforeBase())
        {
        }

        public override void Log(InfoLogLevel level, string message)
        {
        }

        // Throws while the derived constructor is evaluating the argument it
        // passes to base(...), so no base constructor ever runs.
        private static InfoLogLevel FailBeforeBase()
            => throw new InvalidOperationException("configuration could not be read");
    }

    /// <summary>
    /// A logger whose constructor threw does not take the process down when it
    /// is finalized.
    /// </summary>
    /// <remarks>
    /// The object is allocated and registered for finalization before any
    /// constructor runs, so a derived constructor throwing on the way to
    /// <c>base(...)</c> leaves a finalizable object that was never pinned.
    /// <c>Logger</c> unpinned unconditionally from its dispose path, which the
    /// finalizer reaches, and unpinning something never pinned throws — an
    /// unhandled exception on the finalizer thread, arbitrarily later than the
    /// catch below. If this regresses, the test host dies rather than this test
    /// failing, which is loud enough.
    /// </remarks>
    [Fact]
    public void ALoggerWhoseConstructorThrewIsFinalizedSafely()
    {
        AttemptConstruction();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        // Reached only if the finalizer did not throw.
        Assert.True(true);
    }

    // Separated so the half-constructed object cannot stay alive in a local of
    // the test method.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AttemptConstruction()
    {
        Assert.Throws<InvalidOperationException>(() => new LoggerWithAThrowingConstructor());
    }

    /// <summary>
    /// An SST file writer keeps working after the caller drops every reference
    /// to the options it was created from.
    /// </summary>
    /// <remarks>
    /// RocksDb keeps the comparator out of those options as the user comparator
    /// inside its internal key comparator, and the env inside the immutable
    /// options it copies, and reads both on every add and on finish. The writer
    /// held neither, so a collection between creating and finishing destroyed
    /// them underneath it — and the documentation said explicitly that this was
    /// safe to do.
    /// </remarks>
    [Fact]
    public void AnSstFileWriterSurvivesItsOptionsBecomingUnreachable()
    {
        using var dir = new TempDir();
        string sstPath = Path.Combine(dir.Path, "regression.sst");

        using var comparator = new ReverseComparator();
        SstFileWriter writer = CreateWriterAndDropTheOptions(comparator);

        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();

            writer.Open(sstPath);

            // Descending, because the comparator is reversed — so this only
            // succeeds if the comparator is still the live one.
            writer.Put("c"u8, "3"u8);
            writer.Put("b"u8, "2"u8);
            writer.Put("a"u8, "1"u8);
            writer.Finish();
        }
        finally
        {
            writer.Dispose();
        }

        Assert.True(File.Exists(sstPath));
        Assert.True(new FileInfo(sstPath).Length > 0);
    }

    /// <summary>
    /// Options that are collected rather than disposed release their attached
    /// handles without taking the process down.
    /// </summary>
    /// <remarks>
    /// The release loop used to be followed by clearing the bag that holds them.
    /// Clearing a <c>ConcurrentBag</c> reads a <c>ThreadLocal</c>, which is itself
    /// finalizable and may already be gone by the time a finalizer runs, so the
    /// <c>ObjectDisposedException</c> that came back was unhandled. The finalizer
    /// path — the one the holds exist to make safe — was therefore the path that
    /// crashed, for any options object abandoned rather than disposed. If this
    /// regresses the test host dies rather than this test failing.
    /// </remarks>
    [Fact]
    public void OptionsCollectedRatherThanDisposedReleaseTheirHandlesSafely()
    {
        for (int i = 0; i < 20; i++)
        {
            AbandonOptionsWithAttachedHandles();
        }

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        // Reached only if no finalizer threw.
        Assert.True(true);
    }

    // Nothing references these once this returns, so they are finalized rather
    // than disposed. An env, a rate limiter and a comparator, because all three
    // are attached handles the options release on the way out.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbandonOptionsWithAttachedHandles()
    {
        var options = new DbOptions { CreateIfMissing = true };
        options.Env = Env.CreateInMemory();
        options.RateLimiter = new RateLimiter(1024 * 1024);
        options.Comparator = new ReverseComparator();
    }

    // The options are reachable only from the writer once this returns.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SstFileWriter CreateWriterAndDropTheOptions(Comparator comparator)
    {
        var options = new DbOptions { Comparator = comparator };
        return SstFileWriter.Create(options);
    }
}
