using System.Runtime.CompilerServices;

namespace RocksDbNet.Tests;

/// <summary>
/// Covers the <see cref="ReadOptions"/> members,
/// including the table filter callback. See issue #25.
/// </summary>
public class ReadOptionsPropertyTests
{
    // ── Simple properties ────────────────────────────────────────────────────

    /// <summary>
    /// Each of these round-trips both ways on its own, without moving any of
    /// the others.
    /// </summary>
    [Fact]
    public void BoolProperties_RoundTrip()
    {
        using var opts = new ReadOptions();

        BoolProperty.AssertRoundTripsIndependently(
            opts,
            (nameof(opts.AdaptiveReadahead), (o, v) => o.AdaptiveReadahead = v, o => o.AdaptiveReadahead),
            (nameof(opts.AutoReadaheadSize), (o, v) => o.AutoReadaheadSize = v, o => o.AutoReadaheadSize),
            (nameof(opts.AutoPrefixMode), (o, v) => o.AutoPrefixMode = v, o => o.AutoPrefixMode),
            (nameof(opts.AutoRefreshIteratorWithSnapshot), (o, v) => o.AutoRefreshIteratorWithSnapshot = v, o => o.AutoRefreshIteratorWithSnapshot),
            (nameof(opts.AllowUnpreparedValue), (o, v) => o.AllowUnpreparedValue = v, o => o.AllowUnpreparedValue),
            (nameof(opts.OptimizeMultiGetForIo), (o, v) => o.OptimizeMultiGetForIo = v, o => o.OptimizeMultiGetForIo));
    }

    [Fact]
    public void ValueSizeSoftLimit_GetSet()
    {
        using var opts = new ReadOptions();

        opts.ValueSizeSoftLimit = 1048576;
        Assert.Equal(1048576UL, opts.ValueSizeSoftLimit);
    }

    [Theory]
    [InlineData(RateLimiterPriority.Low)]
    [InlineData(RateLimiterPriority.Mid)]
    [InlineData(RateLimiterPriority.High)]
    [InlineData(RateLimiterPriority.User)]
    public void RateLimiterPriority_GetSet(RateLimiterPriority priority)
    {
        using var opts = new ReadOptions();

        opts.RateLimiterPriority = priority;
        Assert.Equal(priority, opts.RateLimiterPriority);
    }

    [Theory]
    [InlineData(IoActivity.Get)]
    [InlineData(IoActivity.MultiGet)]
    [InlineData(IoActivity.DbIterator)]
    [InlineData(IoActivity.GetEntity)]
    public void IoActivity_GetSet(IoActivity activity)
    {
        using var opts = new ReadOptions();

        opts.IoActivity = activity;
        Assert.Equal(activity, opts.IoActivity);
    }

    // ── Merge operand count threshold ────────────────────────────────────────

    [Fact]
    public void MergeOperandCountThreshold_SetHasClear()
    {
        using var opts = new ReadOptions();

        Assert.False(opts.HasMergeOperandCountThreshold);

        opts.MergeOperandCountThreshold = 32;

        Assert.True(opts.HasMergeOperandCountThreshold);
        Assert.Equal(32UL, opts.MergeOperandCountThreshold);

        opts.ClearMergeOperandCountThreshold();

        Assert.False(opts.HasMergeOperandCountThreshold);
    }

    // ── Request id ───────────────────────────────────────────────────────────

    [Fact]
    public void RequestId_RoundTrips()
    {
        using var opts = new ReadOptions();

        Assert.Null(opts.RequestId);

        opts.RequestId = "trace-1234";
        Assert.Equal("trace-1234", opts.RequestId);

        opts.RequestId = "trace-5678";
        Assert.Equal("trace-5678", opts.RequestId);
    }

    [Fact]
    public void RequestId_SetToNull_Clears()
    {
        using var opts = new ReadOptions();
        opts.RequestId = "trace-1234";

        opts.RequestId = null;

        Assert.Null(opts.RequestId);
    }

    [Fact]
    public void RequestId_Clear_Clears()
    {
        using var opts = new ReadOptions();
        opts.RequestId = "trace-1234";

        opts.ClearRequestId();

        Assert.Null(opts.RequestId);
    }

    [Fact]
    public void RequestId_IsCopiedByRocksDb()
    {
        // RocksDb copies the string into a std::string, so a caller buffer that
        // goes away must not affect the stored value.
        using var opts = new ReadOptions();
        SetRequestIdFromTemporary(opts);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        Assert.Equal("temporary-request-id", opts.RequestId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetRequestIdFromTemporary(ReadOptions opts)
        => opts.RequestId = new string("temporary-request-id".ToCharArray());

    // ── User-defined index factory ───────────────────────────────────────────

    [Fact]
    public void TableIndexFactory_UnknownName_Throws()
    {
        using var opts = new ReadOptions();

        Assert.Throws<RocksDbException>(() => opts.SetTableIndexFactoryFromString("not-a-real-factory"));
    }

    [Fact]
    public void TableIndexFactoryName_IsNullByDefault()
    {
        using var opts = new ReadOptions();

        Assert.True(string.IsNullOrEmpty(opts.TableIndexFactoryName));
    }

    [Fact]
    public void ClearTableIndexFactory_OnFreshOptions_DoesNotThrow()
    {
        using var opts = new ReadOptions();

        opts.ClearTableIndexFactory();
    }

    // ── Table filter ─────────────────────────────────────────────────────────

    [Fact]
    public void TableFilter_HasAndClear()
    {
        using var opts = new ReadOptions();

        Assert.False(opts.HasTableFilter);

        opts.SetTableFilter(_ => true);
        Assert.True(opts.HasTableFilter);

        opts.ClearTableFilter();
        Assert.False(opts.HasTableFilter);
    }

    [Fact]
    public void TableFilter_Null_Throws()
    {
        using var opts = new ReadOptions();

        Assert.Throws<ArgumentNullException>(() => opts.SetTableFilter(null!));
    }

    [Fact]
    public void TableFilter_ReturningFalse_SkipsTheFile()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Flush(); // One SST file holding both keys.

        int calls = 0;
        using var opts = new ReadOptions();
        opts.SetTableFilter(_ =>
        {
            Interlocked.Increment(ref calls);
            return false;
        });

        using var iter = db.Db.NewIterator(opts);
        iter.SeekToFirst();

        Assert.False(iter.IsValid());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void TableFilter_ReturningTrue_IncludesTheFile()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Flush();

        using var opts = new ReadOptions();
        opts.SetTableFilter(_ => true);

        using var iter = db.Db.NewIterator(opts);
        iter.SeekToFirst();

        var keys = new List<string>();
        while (iter.IsValid())
        {
            keys.Add(iter.KeyAsString());
            iter.Next();
        }

        Assert.Equal(["a", "b"], keys);
    }

    [Fact]
    public void TableFilter_SeesTheFileProperties()
    {
        using var db = new TempDb();
        for (int i = 0; i < 5; i++)
        {
            db.Db.Put($"key{i}", $"value{i}");
        }

        db.Db.Flush();

        ulong observedEntries = 0;
        string? observedCf = null;

        using var opts = new ReadOptions();
        opts.SetTableFilter(props =>
        {
            observedEntries = props.NumEntries;
            observedCf = props.ColumnFamilyName;
            return true;
        });

        using var iter = db.Db.NewIterator(opts);
        iter.SeekToFirst();
        Assert.True(iter.IsValid());

        Assert.Equal(5UL, observedEntries);
        Assert.Equal("default", observedCf);
    }

    [Fact]
    public void TableFilter_ViewIsInvalidAfterTheCallback()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Flush();

        TablePropertiesView? escaped = null;
        bool? validInsideTheCallback = null;

        using var opts = new ReadOptions();
        opts.SetTableFilter(props =>
        {
            // Recorded, not asserted. The callback catches everything and
            // returns its fallback, and an xunit failure is only an exception,
            // so asserting here would pass whatever props said.
            validInsideTheCallback = props.IsValid;
            escaped = props;
            return true;
        });

        using (var iter = db.Db.NewIterator(opts))
        {
            iter.SeekToFirst();
        }

        Assert.True(validInsideTheCallback, "the view was not valid while the callback held it");

        // RocksDb owns the properties and frees them when the callback returns,
        // so holding on to the view must fail loudly rather than read freed
        // memory.
        Assert.NotNull(escaped);
        Assert.False(escaped!.IsValid);
        Assert.Throws<InvalidOperationException>(() => escaped.NumEntries);
    }

    [Fact]
    public void TableFilter_ToSnapshot_OutlivesTheCallback()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Flush();

        TableProperties? snapshot = null;

        using var opts = new ReadOptions();
        opts.SetTableFilter(props =>
        {
            snapshot = props.ToSnapshot();
            return true;
        });

        using (var iter = db.Db.NewIterator(opts))
        {
            iter.SeekToFirst();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.NotNull(snapshot);
        Assert.Equal(2UL, snapshot!.NumEntries);
        Assert.Equal("default", snapshot.ColumnFamilyName);
    }

    [Fact]
    public void TableFilter_Throwing_IncludesTheFileAndReports()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Flush();

        using var reported = new CallbackExceptionRecorder();

        using var opts = new ReadOptions();
        opts.SetTableFilter(_ => throw new InvalidOperationException("filter boom"));

        using var iter = db.Db.NewIterator(opts);
        iter.SeekToFirst();

        // Excluding the file would silently hide data, so a throwing filter
        // includes it.
        Assert.True(iter.IsValid());
        Assert.Equal("a", iter.KeyAsString());

        Assert.True(reported.Contains("SetTableFilter"));
    }

    /// <summary>
    /// For a callback installed as a plain delegate, the reported sender is that
    /// delegate, not the <see cref="ReadOptions"/> that holds it.
    /// </summary>
    /// <remarks>
    /// Worth pinning because it differs from the subclass-based callbacks, where
    /// the sender is the wrapper. The pinned state behind a table filter is a
    /// handle to the delegate, so that is what there is to report.
    /// </remarks>
    [Fact]
    public void TableFilter_Throwing_ReportsTheDelegateAsTheSender()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Flush();

        Func<TablePropertiesView, bool> filter =
            _ => throw new InvalidOperationException("filter boom");

        object? sender = null;
        void Capture(object? s, CallbackExceptionEventArgs e)
        {
            if (e.CallbackName == "SetTableFilter")
            {
                sender = s;
            }
        }

        RocksDbCallbacks.UnhandledException += Capture;
        try
        {
            using var opts = new ReadOptions();
            opts.SetTableFilter(filter);

            using var iter = db.Db.NewIterator(opts);
            iter.SeekToFirst();

            Assert.Same(filter, sender);
        }
        finally
        {
            RocksDbCallbacks.UnhandledException -= Capture;
        }
    }

    /// <summary>
    /// Each <c>SetTableFilter</c> allocates a GCHandle and relies on RocksDb
    /// running the previous destructor to release it. A missed release pins the
    /// delegate forever.
    /// </summary>
    /// <remarks>
    /// The old version asserted only that a filter was still installed, which is
    /// true whether or not the previous few thousand were released. Giving each
    /// delegate something substantial to capture makes the leak measurable:
    /// a pinned delegate keeps its captures reachable, so a run that released
    /// none of them holds on to every ballast array.
    /// </remarks>
    [Fact]
    public void TableFilter_ReplacedRepeatedly_DoesNotLeakHandles()
    {
        const int Replacements = 2_000;
        const int BallastBytes = 64 * 1024;

        using var opts = new ReadOptions();

        long before = GC.GetTotalMemory(forceFullCollection: true);

        for (int i = 0; i < Replacements; i++)
        {
            byte[] ballast = new byte[BallastBytes];
            opts.SetTableFilter(_ => ballast.Length == BallastBytes);
        }

        long grew = GC.GetTotalMemory(forceFullCollection: true) - before;

        // 128 MB pinned if nothing was released, against one ballast for the
        // filter still installed if everything was.
        const long Budget = 32L * 1024 * 1024;

        Assert.True(
            grew < Budget,
            $"managed memory grew by {grew / (1024 * 1024)} MB over {Replacements} replacements, so the handles stayed pinned");

        Assert.True(opts.HasTableFilter);
    }

    [Fact]
    public void TableFilter_SurvivesTheDelegateGoingOutOfScope()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Flush();

        using var opts = new ReadOptions();
        SetFilterFromTemporaryScope(opts);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        using var iter = db.Db.NewIterator(opts);
        iter.SeekToFirst();

        Assert.False(iter.IsValid());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetFilterFromTemporaryScope(ReadOptions opts)
        => opts.SetTableFilter(_ => false);
}
