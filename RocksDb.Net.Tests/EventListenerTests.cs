using System.Text;

using RocksDbNet.Extensions;

namespace RocksDbNet.Tests;

public class EventListenerTests
{
    // The recorder these tests used to declare here appended to plain Lists
    // from RocksDb background threads, with no lock on either side. Shared
    // RecordingListener locks both, which is what it was written for.

    private sealed class PassiveEventListener : EventListener
    {
    }

    private sealed class CompletedEventListener : EventListener
    {
        public override void OnFlushCompleted(FlushJobInfo info)
        {
        }

        public override void OnSubCompactionCompleted(SubCompactionJobInfo info)
        {
        }

        public override void OnCompactionCompleted(CompactionJobInfo info)
        {
        }
    }

    private sealed class AllEventListener : EventListener
    {
        public override void OnFlushBegin(FlushJobInfo info)
        {
        }

        public override void OnFlushCompleted(FlushJobInfo info)
        {
        }

        public override void OnCompactionBegin(CompactionJobInfo info)
        {
        }

        public override void OnCompactionCompleted(CompactionJobInfo info)
        {
        }

        public override void OnSubCompactionBegin(SubCompactionJobInfo info)
        {
        }

        public override void OnSubCompactionCompleted(SubCompactionJobInfo info)
        {
        }

        public override void OnExternalFileIngested(ExternalFileIngestionInfo info)
        {
        }

        public override void OnBackgroundError(BackgroundErrorInfo info)
        {
        }

        public override void OnStallConditionsChanged(WriteStallInfo info)
        {
        }

        public override void OnMemTableSealed(MemTableInfo info)
        {
        }
    }

    /// <summary>Overrides exactly one event, leaving the other nine to the base class.</summary>
    private sealed class SingleOverrideListener : EventListener
    {
        public int FlushCompletedCount;

        public override void OnFlushCompleted(FlushJobInfo info)
            => Interlocked.Increment(ref FlushCompletedCount);
    }

    /// <summary>
    /// A listener only has to override the events it cares about. RocksDb invokes
    /// all ten callbacks with no null check, so the unoverridden ones must still
    /// be installed and filtered on the managed side. See issue #35.
    /// </summary>
    [Fact]
    public void EventListener_PartialOverride_DoesNotCrash()
    {
        var listener = new SingleOverrideListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.AddEventListener(listener);

        using var db = TestDb.OpenInMemory(opts);

        // Drive flush, compaction and memtable-sealed events, none of which this
        // listener overrides except OnFlushCompleted.
        db.Put("a", "1");
        db.Flush();
        db.Put("b", "2");
        db.Flush();
        db.CompactRange();

        Assert.True(listener.FlushCompletedCount > 0, "the single override should still fire");
    }

    [Fact]
    public void EventListener_ReceivesFlushEvents()
    {
        var listener = new RecordingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.AddEventListener(listener);

        using var db = TestDb.OpenInMemory(opts);

        db.Put("key1", "value1");
        db.Put("key2", "value2");
        db.Flush();

        Assert.True(
            Wait.Until(() =>
                listener.FlushBegin.Count > 0 && listener.FlushCompleted.Count > 0),
            "no flush callback arrived");

        Assert.NotEmpty(listener.FlushBegin);
        var beginInfo = listener.FlushBegin[0];
        Assert.NotNull(beginInfo.ColumnFamilyName);
        Assert.NotNull(beginInfo.FilePath);

        Assert.NotEmpty(listener.FlushCompleted);
        var completedInfo = listener.FlushCompleted[0];
        Assert.NotNull(completedInfo.ColumnFamilyName);
        Assert.NotNull(completedInfo.FilePath);
    }

    [Fact]
    public void EventListener_ReceivesCompactionEvents()
    {
        var listener = new RecordingListener();

        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            WriteBufferSize = 1024,
            Level0FileNumCompactionTrigger = 2,
        };
        opts.AddEventListener(listener);

        using var db = TestDb.OpenInMemory(opts);

        // Write enough data to trigger compaction
        for (int i = 0; i < 200; i++)
        {
            db.Put($"key_{i:D5}", new string('x', 100));
        }
        db.Flush();

        for (int i = 200; i < 400; i++)
        {
            db.Put($"key_{i:D5}", new string('y', 100));
        }
        db.Flush();

        db.CompactRange();

        Assert.True(
            Wait.Until(() => listener.CompactionCompleted.Count > 0),
            "no compaction-completed callback arrived");

        Assert.NotEmpty(listener.CompactionCompleted);

        var info = listener.CompactionCompleted[0];
        Assert.NotNull(info.ColumnFamilyName);
        Assert.NotNull(info.Status);
    }

    [Fact]
    public void EventListener_AddMultiple()
    {
        var listener1 = new RecordingListener();
        var listener2 = new RecordingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.AddEventListeners([listener1, listener2]);

        using var db = TestDb.OpenInMemory(opts);

        db.Put("key1", "value1");
        db.Flush();

        Assert.True(
            Wait.Until(() =>
                listener1.FlushCompleted.Count > 0 && listener2.FlushCompleted.Count > 0),
            "one of the two listeners saw no flush");

        Assert.NotEmpty(listener1.FlushCompleted);
        Assert.NotEmpty(listener2.FlushCompleted);
    }

    [Fact]
    public void EventListener_FlushJobInfo_Properties()
    {
        var listener = new RecordingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.AddEventListener(listener);

        using var db = TestDb.OpenInMemory(opts);

        db.Put("a", "1");
        db.Flush();

        Assert.True(
            Wait.Until(() => listener.FlushCompleted.Count > 0),
            "no flush-completed callback arrived");

        FlushJobInfo info = Assert.Single(listener.FlushCompleted);

        Assert.Equal("default", info.ColumnFamilyName);
        Assert.EndsWith(".sst", info.FilePath, StringComparison.Ordinal);

        // The reason this flush actually had. The assertion here used to read
        // "reason != 0 || reason == 0", which is true of every value the field
        // could ever hold, including one the marshalling invented.
        Assert.Equal(FlushReason.ManualFlush, info.FlushReason);

        // One key was written, so the sequence range is a single number and a
        // real one rather than the zeroes an unmarshalled struct would show.
        Assert.True(info.SmallestSeqno > 0);
        Assert.Equal(info.SmallestSeqno, info.LargestSeqno);
    }

    [Fact]
    public void EventListener_ReceivesExternalFileIngestedEvent()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string sstPath = Path.Combine(dir.Path, "ingest.sst");

        var listener = new RecordingListener();

        using var dbOpts = new DbOptions { CreateIfMissing = true };
        dbOpts.AddEventListener(listener);

        // Create an SST file
        using (var writer = SstFileWriter.Create(dbOpts))
        {
            writer.Open(sstPath);
            writer.Put(Encoding.UTF8.GetBytes("sst_k"), Encoding.UTF8.GetBytes("sst_v"));
            writer.Finish();
        }

        using var db = RocksDb.Open(dbOpts, dbPath);
        using var ingestOpts = new IngestExternalFileOptions();
        db.IngestExternalFile([sstPath], ingestOpts);

        Assert.NotEmpty(listener.Ingested);
        var info = listener.Ingested[0];
        Assert.NotNull(info.ColumnFamilyName);
    }

    [Fact]
    public void EventListener_ReceivesFlushBeginEvent()
    {
        var listener = new RecordingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.AddEventListener(listener);

        using var db = TestDb.OpenInMemory(opts);

        db.Put("key", "value");
        db.Flush();

        Assert.True(
            Wait.Until(() => listener.FlushBegin.Count > 0),
            "no flush-begin callback arrived");
    }

    [Fact]
    public void EventListener_CompactionJobInfo_HasInputAndOutputFiles()
    {
        var listener = new RecordingListener();

        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            WriteBufferSize = 1024,
            Level0FileNumCompactionTrigger = 2,
        };
        opts.AddEventListener(listener);

        using var db = TestDb.OpenInMemory(opts);

        for (int i = 0; i < 200; i++)
            db.Put($"key_{i:D5}", new string('x', 100));
        db.Flush();

        for (int i = 200; i < 400; i++)
            db.Put($"key_{i:D5}", new string('y', 100));
        db.Flush();

        db.CompactRange();

        // Waited for rather than hoped for. This used to be guarded by a count
        // check, so a run where no compaction fired reached the end having
        // asserted nothing at all.
        Assert.True(
            Wait.Until(() => listener.CompactionCompleted.Count > 0),
            "no compaction completed");

        CompactionJobInfo info = listener.CompactionCompleted[0];

        // The two flushes above are what it compacted, and it produced at least
        // one file from them.
        Assert.NotEmpty(info.InputFiles);
        Assert.NotEmpty(info.OutputFiles);
        Assert.All(info.InputFiles, f => Assert.EndsWith(".sst", f, StringComparison.Ordinal));
        Assert.All(info.OutputFiles, f => Assert.EndsWith(".sst", f, StringComparison.Ordinal));
    }

    [Fact]
    public void EventListener_BaseVirtualMethods_DoNotThrow()
    {
        using var listener = new PassiveEventListener();

        listener.OnFlushBegin(new FlushJobInfo(
            ColumnFamilyName: "default",
            FilePath: "file.sst",
            TriggeredWritesSlowdown: false,
            TriggeredWritesStop: false,
            LargestSeqno: 10,
            SmallestSeqno: 1,
            FlushReason: FlushReason.ManualFlush));

        listener.OnFlushCompleted(new FlushJobInfo(
            ColumnFamilyName: "default",
            FilePath: "file.sst",
            TriggeredWritesSlowdown: true,
            TriggeredWritesStop: false,
            LargestSeqno: 11,
            SmallestSeqno: 2,
            FlushReason: FlushReason.WriteBufferFull));

        listener.OnCompactionBegin(new CompactionJobInfo(
            ColumnFamilyName: "default",
            InputFiles: ["a.sst"],
            OutputFiles: ["b.sst"],
            TotalInputBytes: 100,
            TotalOutputBytes: 90,
            InputRecords: 10,
            OutputRecords: 9,
            Elapsed: TimeSpan.FromMicroseconds(250),
            NumOfCorruptKeys: 0,
            BaseInputLevel: 0,
            CompactionReason: CompactionReason.LevelL0FilesNum,
            Status: "OK"));

        listener.OnCompactionCompleted(new CompactionJobInfo(
            ColumnFamilyName: "default",
            InputFiles: ["c.sst"],
            OutputFiles: ["d.sst"],
            TotalInputBytes: 200,
            TotalOutputBytes: 180,
            InputRecords: 20,
            OutputRecords: 18,
            Elapsed: TimeSpan.FromMicroseconds(500),
            NumOfCorruptKeys: 0,
            BaseInputLevel: 0,
            CompactionReason: CompactionReason.ManualCompaction,
            Status: null));

        listener.OnSubCompactionBegin(new SubCompactionJobInfo(
            ColumnFamilyName: "default",
            Status: "OK"));

        listener.OnSubCompactionCompleted(new SubCompactionJobInfo(
            ColumnFamilyName: "default",
            Status: null));

        listener.OnExternalFileIngested(new ExternalFileIngestionInfo(
            ColumnFamilyName: "default",
            InternalFilePath: "ingest.sst"));

        listener.OnBackgroundError(new BackgroundErrorInfo(
            Reason: BackgroundErrorReason.Compaction,
            Message: "simulated"));

        listener.OnStallConditionsChanged(new WriteStallInfo(
            ColumnFamilyName: "default",
            Condition: WriteStallCondition.Delayed,
            PreviousCondition: WriteStallCondition.Normal));

        listener.OnMemTableSealed(new MemTableInfo(
            ColumnFamilyName: "default",
            FirstSeqno: 1,
            EarliestSeqno: 1,
            NumEntries: 2,
            NumDeletes: 0));
    }

    [Fact]
    public void EventListener_DetectOverrides_All()
    {
        var listener = new AllEventListener();

        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnFlushBegin)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnFlushCompleted)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnCompactionBegin)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnCompactionCompleted)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnSubCompactionBegin)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnSubCompactionCompleted)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnExternalFileIngested)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnBackgroundError)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnStallConditionsChanged)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnMemTableSealed)));
    }

    /// <summary>
    /// Overrides some callbacks and not others, which is the whole point of
    /// the detection this test covers.
    /// </summary>
    /// <remarks>
    /// Declared here rather than reusing the shared recorder, which overrides
    /// every callback and so could only ever prove the True half.
    /// </remarks>
    private sealed class PartiallyOverridingListener : EventListener
    {
        public override void OnFlushBegin(FlushJobInfo info)
        {
        }

        public override void OnFlushCompleted(FlushJobInfo info)
        {
        }

        public override void OnCompactionBegin(CompactionJobInfo info)
        {
        }

        public override void OnCompactionCompleted(CompactionJobInfo info)
        {
        }

        public override void OnExternalFileIngested(ExternalFileIngestionInfo info)
        {
        }

        public override void OnMemTableSealed(MemTableInfo info)
        {
        }
    }

    [Fact]
    public void EventListener_DetectOverrides_Some()
    {
        var listener = new PartiallyOverridingListener();

        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnFlushBegin)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnFlushCompleted)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnCompactionBegin)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnCompactionCompleted)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnSubCompactionBegin)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnSubCompactionCompleted)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnExternalFileIngested)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnBackgroundError)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnStallConditionsChanged)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnMemTableSealed)));
    }

    [Fact]
    public void EventListener_DetectOverrides_None()
    {
        var listener = new PassiveEventListener();

        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnFlushBegin)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnFlushCompleted)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnCompactionBegin)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnCompactionCompleted)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnSubCompactionBegin)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnSubCompactionCompleted)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnExternalFileIngested)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnBackgroundError)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnStallConditionsChanged)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnMemTableSealed)));
    }

    [Fact]
    public void EventListener_DetectOverrides_Completed()
    {
        var listener = new CompletedEventListener();
        
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnFlushBegin)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnFlushCompleted)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnCompactionBegin)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnCompactionCompleted)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnSubCompactionBegin)));
        Assert.True(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnSubCompactionCompleted)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnExternalFileIngested)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnBackgroundError)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnStallConditionsChanged)));
        Assert.False(listener.CheckIfMethodOverridden<EventListener>(nameof(EventListener.OnMemTableSealed)));
    }
}
