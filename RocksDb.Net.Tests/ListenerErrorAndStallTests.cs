namespace RocksDbNet.Tests;

/// <summary>
/// The two <see cref="EventListener"/> callbacks nothing in the suite had ever
/// caused RocksDb to fire.
/// </summary>
/// <remarks>
/// Every other callback was covered, so their marshalling was known to work.
/// These two were reachable only by making RocksDb fail or throttle, which no
/// test did on purpose, and unexercised marshalling of a native struct is
/// exactly where a wrong field offset sits unnoticed. Both tests therefore
/// assert on the contents rather than on the callback merely arriving.
/// </remarks>
public class ListenerErrorAndStallTests
{
    /// <summary>
    /// A space cap that a flush breaches is reported to the listener, not only
    /// to the caller who was writing at the time.
    /// </summary>
    [Fact]
    public void OnBackgroundError_FiresWhenAFlushCannotComplete()
    {
        using var dir = new TempDir();
        using SstFileManager manager = SstFileManager.Create();

        // Small enough that a few flushes exceed it.
        manager.SetMaxAllowedSpaceUsage(64 * 1024);

        var listener = new RecordingListener();

        var opts = new DbOptions { CreateIfMissing = true, WriteBufferSize = 16 * 1024 };
        opts.SstFileManager = manager;
        opts.EventListener = listener;

        using var db = RocksDb.Open(opts, dir.Path);

        Exception? failure = null;

        for (int i = 0; i < 400 && failure is null; i++)
        {
            failure = Record.Exception(() =>
            {
                db.Put($"key{i:D4}", new string('v', 1024));
                db.Flush();
            });
        }

        Assert.NotNull(failure);

        Assert.True(
            Wait.Until(() => listener.BackgroundErrors.Count > 0),
            "the write failed but the listener was never told");

        BackgroundErrorInfo error = listener.BackgroundErrors[0];

        // The reason has to be a real value rather than whatever an unmarshalled
        // field happened to hold, and the message has to say something.
        Assert.True(
            Enum.IsDefined(error.Reason),
            $"reason came back as {(int)error.Reason}, which is not a defined value");

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    /// <summary>
    /// Work piling up faster than compaction clears it throttles writes, and
    /// the listener is told each time the condition changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A one-byte soft limit on pending compaction bytes, so any real backlog
    /// crosses it. Two more obvious routes do not work. Disabling auto
    /// compaction and letting level-zero files pile up reports nothing at all:
    /// RocksDb skips stall accounting entirely when compaction is off. Setting
    /// the slowdown trigger below the compaction trigger reports nothing
    /// either, because sanitization raises the slowdown trigger back up to it.
    /// </para>
    /// <para>
    /// The soft limit rather than the hard one. Crossing the hard limit stops
    /// the writing thread until compaction drains the backlog, so a test that
    /// got the balance wrong would hang instead of failing.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnStallConditionsChanged_FiresWhenWritesAreThrottled()
    {
        using var dir = new TempDir();

        var listener = new RecordingListener();

        var opts = new DbOptions
        {
            CreateIfMissing = true,

            // Any backlog at all is over the line.
            SoftPendingCompactionBytesLimit = 1,

            // Zero disables the hard limit, so writes are only ever delayed.
            HardPendingCompactionBytesLimit = 0,
        };

        opts.EventListener = listener;

        using var db = RocksDb.Open(opts, dir.Path);

        for (int round = 0; round < 10; round++)
        {
            for (int i = 0; i < 200; i++)
            {
                db.Put($"key{round:D3}_{i:D5}", new string('v', 1024));
            }

            db.Flush();
        }

        Assert.True(
            Wait.Until(() => listener.Stalls.Count > 0),
            "compaction fell behind a one-byte limit and no stall was reported");

        IReadOnlyList<WriteStallInfo> stalls = listener.Stalls;

        // Every report is a change, which is what the callback is named for.
        Assert.All(stalls, s => Assert.NotEqual(s.PreviousCondition, s.Condition));
        Assert.All(stalls, s => Assert.Equal("default", s.ColumnFamilyName));

        // Delayed rather than stopped, since the hard limit is disabled. Which
        // direction comes first depends on when compaction gets scheduled, so
        // the assertion is that the transition happened, not that it was first.
        Assert.Contains(
            stalls,
            s => s.PreviousCondition == WriteStallCondition.Normal
                && s.Condition == WriteStallCondition.Delayed);

        Assert.DoesNotContain(stalls, s => s.Condition == WriteStallCondition.Stopped);
    }
}
