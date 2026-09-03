namespace RocksDbNet.Tests;

/// <summary>
/// Per-read deadlines and limits. See issue #77.
/// </summary>
public class ReadDeadlineTests
{
    [Fact]
    public void Deadline_DefaultsToNoDeadline()
    {
        using var opts = new ReadOptions();

        Assert.Null(opts.Deadline);
    }

    /// <summary>
    /// The deadline is an absolute time, so it must round-trip as one rather
    /// than being reinterpreted as a duration.
    /// </summary>
    [Fact]
    public void Deadline_RoundTripsAnAbsoluteTime()
    {
        using var opts = new ReadOptions();

        DateTimeOffset when = DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000).AddMilliseconds(250);
        opts.Deadline = when;

        Assert.Equal(when, opts.Deadline);
    }

    [Fact]
    public void Deadline_Null_ClearsIt()
    {
        using var opts = new ReadOptions();
        opts.Deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        Assert.NotNull(opts.Deadline);

        opts.Deadline = null;

        Assert.Null(opts.Deadline);
    }

    /// <summary>
    /// A time before the epoch cannot be represented, and zero is how RocksDb
    /// spells no deadline, so it clamps rather than wrapping into a deadline
    /// nobody asked for.
    /// </summary>
    [Fact]
    public void Deadline_BeforeTheEpoch_ClearsRatherThanWrapping()
    {
        using var opts = new ReadOptions();

        opts.Deadline = DateTimeOffset.UnixEpoch.AddDays(-1);

        Assert.Null(opts.Deadline);
    }

    [Fact]
    public void SetDeadlineAfter_ProducesATimeInTheFuture()
    {
        using var opts = new ReadOptions();

        DateTimeOffset before = DateTimeOffset.UtcNow;
        opts.SetDeadlineAfter(TimeSpan.FromMinutes(5));

        Assert.NotNull(opts.Deadline);
        Assert.True(opts.Deadline > before.AddMinutes(4));
        Assert.True(opts.Deadline < before.AddMinutes(6));
    }

    /// <summary>
    /// A non-positive timeout clears the deadline instead of setting one that
    /// has already passed, which would fail every read.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetDeadlineAfter_NonPositive_ClearsIt(int seconds)
    {
        using var opts = new ReadOptions();
        opts.SetDeadlineAfter(TimeSpan.FromMinutes(1));

        opts.SetDeadlineAfter(TimeSpan.FromSeconds(seconds));

        Assert.Null(opts.Deadline);
    }

    [Fact]
    public void SetDeadlineAfter_IsFluent()
    {
        using var opts = new ReadOptions();

        Assert.Same(opts, opts.SetDeadlineAfter(TimeSpan.FromSeconds(30)));
    }

    // ── IoTimeout ────────────────────────────────────────────────────────────

    [Fact]
    public void IoTimeout_DefaultsToZeroMeaningNoLimit()
    {
        using var opts = new ReadOptions();

        Assert.Equal(TimeSpan.Zero, opts.IoTimeout);
    }

    [Fact]
    public void IoTimeout_RoundTrips()
    {
        using var opts = new ReadOptions();

        opts.IoTimeout = TimeSpan.FromMilliseconds(1500);

        Assert.Equal(TimeSpan.FromMilliseconds(1500), opts.IoTimeout);
    }

    [Fact]
    public void IoTimeout_Negative_IsTreatedAsNoLimit()
    {
        using var opts = new ReadOptions();

        opts.IoTimeout = TimeSpan.FromSeconds(-5);

        Assert.Equal(TimeSpan.Zero, opts.IoTimeout);
    }

    // ── MaxSkippableInternalKeys ─────────────────────────────────────────────

    [Fact]
    public void MaxSkippableInternalKeys_DefaultsToZeroMeaningUnlimited()
    {
        using var opts = new ReadOptions();

        Assert.Equal(0UL, opts.MaxSkippableInternalKeys);
    }

    [Fact]
    public void MaxSkippableInternalKeys_RoundTrips()
    {
        using var opts = new ReadOptions();

        opts.MaxSkippableInternalKeys = 1000;

        Assert.Equal(1000UL, opts.MaxSkippableInternalKeys);
    }

    /// <summary>
    /// The behavioural test, and the reason the setting exists. A range full of
    /// tombstones makes a seek walk every one, so a low limit must make that
    /// seek fail rather than scan.
    /// </summary>
    [Fact]
    public void MaxSkippableInternalKeys_LowLimit_FailsASeekOverManyTombstones()
    {
        using var db = new TempDb(o => o.DisableAutoCompactions = true);

        for (int i = 0; i < 500; i++)
        {
            db.Db.Put($"key{i:D5}", "value");
        }

        db.Db.Flush();

        // Delete them all without compacting, so the tombstones remain.
        for (int i = 0; i < 500; i++)
        {
            db.Db.Delete($"key{i:D5}");
        }

        db.Db.Flush();
        db.Db.Put("zzz", "survivor");
        db.Db.Flush();

        // With no limit the seek walks every tombstone and finds the survivor.
        using (var unlimited = new ReadOptions())
        using (Iterator iter = db.Db.NewIterator(unlimited))
        {
            iter.SeekToFirst();
            Assert.True(iter.IsValid());
            Assert.Equal("zzz", iter.KeyAsString());
        }

        // With a low limit it gives up instead. RocksDb reports that through the
        // iterator's status rather than by returning nothing, so check both:
        // either the iterator is invalid or it reports an error.
        using var limited = new ReadOptions { MaxSkippableInternalKeys = 10 };
        using Iterator limitedIter = db.Db.NewIterator(limited);
        limitedIter.SeekToFirst();

        bool gaveUp = !limitedIter.IsValid();
        if (!gaveUp)
        {
            gaveUp = Record.Exception(limitedIter.CheckForError) is not null;
        }

        Assert.True(gaveUp, "a low skip limit should stop the seek rather than walking 500 tombstones");
    }

    // ── BackgroundPurgeOnIteratorCleanup ─────────────────────────────────────

    [Fact]
    public void BackgroundPurgeOnIteratorCleanup_RoundTrips()
    {
        using var opts = new ReadOptions();

        Assert.False(opts.BackgroundPurgeOnIteratorCleanup);

        opts.BackgroundPurgeOnIteratorCleanup = true;

        Assert.True(opts.BackgroundPurgeOnIteratorCleanup);
    }

    /// <summary>
    /// It must not break ordinary iteration, since it only moves file cleanup
    /// off the disposing thread.
    /// </summary>
    [Fact]
    public void BackgroundPurgeOnIteratorCleanup_StillIteratesCorrectly()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Flush();

        using var opts = new ReadOptions { BackgroundPurgeOnIteratorCleanup = true };

        var seen = new List<string>();
        using (Iterator iter = db.Db.NewIterator(opts))
        {
            for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
            {
                seen.Add(iter.KeyAsString()!);
            }
        }

        Assert.Equal(["a", "b"], seen);
    }

    /// <summary>
    /// A deadline set on options used for a real read must not break it. The
    /// deadline is best effort, so the assertion is that a generous one is
    /// honoured, not that a tight one fails.
    /// </summary>
    [Fact]
    public void Deadline_GenerousDeadline_DoesNotPreventTheRead()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");
        db.Db.Flush();

        using var opts = new ReadOptions();
        opts.SetDeadlineAfter(TimeSpan.FromMinutes(1));
        opts.IoTimeout = TimeSpan.FromSeconds(30);

        Assert.Equal("value", db.Db.GetString("key", opts));
    }
}
