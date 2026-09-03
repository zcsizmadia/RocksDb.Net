namespace RocksDbNet.Tests;

/// <summary>
/// Pins the enums whose values RocksDb defines, against the native numbers.
/// </summary>
/// <remarks>
/// <para>
/// These four enums are reported to callers by the event listener, and every one
/// of them was wrong before: values were shifted, names were invented, and
/// <see cref="WriteStallCondition"/> was inverted outright. Nothing caught it,
/// because the earlier tests spot-checked two or three of the wrapper's own
/// numbers instead of comparing against the header.
/// </para>
/// <para>
/// So each table below is exhaustive, and the expected numbers are transcribed
/// from RocksDb 11.8.1: <c>CompactionReason</c>, <c>FlushReason</c> and
/// <c>BackgroundErrorReason</c> from <c>include/rocksdb/listener.h</c>, and
/// <c>WriteStallCondition</c> from <c>include/rocksdb/types.h</c>. Three of the
/// four are positional in the header, so inserting a member shifts everything
/// after it; an exhaustive table is the only thing that catches that.
/// </para>
/// </remarks>
public class NativeEnumValueTests
{
    /// <summary>
    /// Asserts the enum has exactly the expected members, with the expected
    /// values and no extras.
    /// </summary>
    private static void AssertExactly<TEnum>(params (string Name, int Value)[] expected)
        where TEnum : struct, Enum
    {
        foreach ((string name, int value) in expected)
        {
            Assert.True(Enum.IsDefined(typeof(TEnum), name), $"{typeof(TEnum).Name}.{name} is missing");
            Assert.Equal(value, Convert.ToInt32(Enum.Parse<TEnum>(name)));
        }

        // No members beyond the native set. An extra one is a value some future
        // RocksDb release will claim for something else.
        Assert.Equal(
            expected.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal),
            Enum.GetNames<TEnum>().OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void CompactionReason_MatchesListenerHeader()
        => AssertExactly<CompactionReason>(
            ("Unknown", 0),
            ("LevelL0FilesNum", 1),
            ("LevelMaxLevelSize", 2),
            ("UniversalSizeAmplification", 3),
            ("UniversalSizeRatio", 4),
            ("UniversalSortedRunNum", 5),
            ("FifoMaxSize", 6),
            ("FifoReduceNumFiles", 7),
            ("FifoTtl", 8),
            ("ManualCompaction", 9),
            ("FilesMarkedForCompaction", 10),
            ("BottommostFiles", 11),
            ("Ttl", 12),
            ("Flush", 13),
            ("ExternalSstIngestion", 14),
            ("PeriodicCompaction", 15),
            ("ChangeTemperature", 16),
            ("ForcedBlobGC", 17),
            ("RoundRobinTtl", 18),
            ("RefitLevel", 19),
            ("ReadTriggered", 20));

    [Fact]
    public void FlushReason_MatchesListenerHeader()
        => AssertExactly<FlushReason>(
            ("Others", 0x00),
            ("GetLiveFiles", 0x01),
            ("ShutDown", 0x02),
            ("ExternalFileIngestion", 0x03),
            ("ManualCompaction", 0x04),
            ("WriteBufferManager", 0x05),
            ("WriteBufferFull", 0x06),
            ("Test", 0x07),
            ("DeleteFiles", 0x08),
            ("AutoCompaction", 0x09),
            ("ManualFlush", 0x0a),
            ("ErrorRecovery", 0x0b),
            ("ErrorRecoveryRetryFlush", 0x0c),
            ("WalFull", 0x0d),
            ("CatchUpAfterErrorRecovery", 0x0e),
            ("MemtableMaxRangeDeletions", 0x0f));

    [Fact]
    public void BackgroundErrorReason_MatchesListenerHeader()
        => AssertExactly<BackgroundErrorReason>(
            ("Flush", 0),
            ("Compaction", 1),
            ("WriteCallback", 2),
            ("MemTable", 3),
            ("ManifestWrite", 4),
            ("FlushNoWal", 5),
            ("ManifestWriteNoWal", 6),
            ("AsyncFileOpen", 7));

    /// <summary>
    /// The one to read twice. Native declares <c>kDelayed, kStopped, kNormal</c>,
    /// so normal is last, and the wrapper previously assumed the conventional
    /// normal-first order. That inverted the signal: a stall was reported as
    /// normal and recovery as a stop.
    /// </summary>
    [Fact]
    public void WriteStallCondition_MatchesTypesHeader()
        => AssertExactly<WriteStallCondition>(
            ("Delayed", 0),
            ("Stopped", 1),
            ("Normal", 2));

    /// <summary>
    /// An explicit flush must report itself as one. This is the assertion that
    /// would have caught the shifted <see cref="FlushReason"/> values without
    /// anyone reading the header, because the wrapper reported a manual flush as
    /// <c>WriteBufferManager</c>.
    /// </summary>
    [Fact]
    public void ManualFlush_IsReportedAsManualFlush()
    {
        var listener = new RecordingListener();

        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Put("key", "value");
        db.Flush();

        Assert.Contains(listener.FlushCompleted, f => f.FlushReason == FlushReason.ManualFlush);
    }

    /// <summary>
    /// A manual compaction must report itself as one, for the same reason.
    /// </summary>
    [Fact]
    public void ManualCompaction_IsReportedAsManualCompaction()
    {
        var listener = new RecordingListener();

        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using var db = RocksDb.Open(opts, dir.Path);

        db.WriteOverlappingSstFiles();
        db.CompactRange();

        Assert.Contains(
            listener.CompactionCompleted,
            c => c.CompactionReason == CompactionReason.ManualCompaction);
    }
}
