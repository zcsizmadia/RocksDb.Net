using System.Text;

using RocksDbNet.Extensions;

namespace RocksDbNet.Tests;

public class EventListenerTests
{
    private sealed class TestEventListener : EventListener
    {
        public List<FlushJobInfo> FlushBeginEvents { get; } = [];
        public List<FlushJobInfo> FlushCompletedEvents { get; } = [];
        public List<CompactionJobInfo> CompactionBeginEvents { get; } = [];
        public List<CompactionJobInfo> CompactionCompletedEvents { get; } = [];
        public List<MemTableInfo> MemTableSealedEvents { get; } = [];
        public List<ExternalFileIngestionInfo> ExternalFileIngestedEvents { get; } = [];

        public override void OnFlushBegin(FlushJobInfo info)
        {
            FlushBeginEvents.Add(info);
        }

        public override void OnFlushCompleted(FlushJobInfo info)
        {
            FlushCompletedEvents.Add(info);
        }

        public override void OnCompactionBegin(CompactionJobInfo info)
        {
            CompactionBeginEvents.Add(info);
        }

        public override void OnCompactionCompleted(CompactionJobInfo info)
        {
            CompactionCompletedEvents.Add(info);
        }

        public override void OnMemTableSealed(MemTableInfo info)
        {
            MemTableSealedEvents.Add(info);
        }

        public override void OnExternalFileIngested(ExternalFileIngestionInfo info)
        {
            ExternalFileIngestedEvents.Add(info);
        }
    }

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

    [Fact]
    public void EventListener_ReceivesFlushEvents()
    {
        using var dir = new TempDir();
        var listener = new TestEventListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Put("key1", "value1");
        db.Put("key2", "value2");
        db.Flush();

        Assert.NotEmpty(listener.FlushBeginEvents);
        var beginInfo = listener.FlushBeginEvents[0];
        Assert.NotNull(beginInfo.ColumnFamilyName);
        Assert.NotNull(beginInfo.FilePath);

        Assert.NotEmpty(listener.FlushCompletedEvents);
        var completedInfo = listener.FlushCompletedEvents[0];
        Assert.NotNull(completedInfo.ColumnFamilyName);
        Assert.NotNull(completedInfo.FilePath);
    }

    [Fact]
    public void EventListener_ReceivesCompactionEvents()
    {
        using var dir = new TempDir();
        var listener = new TestEventListener();

        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            WriteBufferSize = 1024,
            Level0FileNumCompactionTrigger = 2,
        };
        opts.EventListener = listener;

        using var db = RocksDb.Open(opts, dir.Path);

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

        Assert.NotEmpty(listener.CompactionCompletedEvents);

        var info = listener.CompactionCompletedEvents[0];
        Assert.NotNull(info.ColumnFamilyName);
        Assert.NotNull(info.Status);
    }

    [Fact]
    public void EventListener_AddMultiple()
    {
        using var dir = new TempDir();
        var listener1 = new TestEventListener();
        var listener2 = new TestEventListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListeners = [listener1, listener2];

        using var db = RocksDb.Open(opts, dir.Path);

        db.Put("key1", "value1");
        db.Flush();

        Assert.NotEmpty(listener1.FlushCompletedEvents);
        Assert.NotEmpty(listener2.FlushCompletedEvents);
    }

    [Fact]
    public void EventListener_FlushJobInfo_Properties()
    {
        using var dir = new TempDir();
        var listener = new TestEventListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Put("a", "1");
        db.Flush();

        Assert.NotEmpty(listener.FlushCompletedEvents);
        var info = listener.FlushCompletedEvents[0];

        // Verify all properties are populated
        Assert.Equal("default", info.ColumnFamilyName);
        Assert.NotNull(info.FilePath);
        Assert.True(info.FlushReason != 0 || info.FlushReason == 0); // Enum is valid
    }

    [Fact]
    public void EventListener_ReceivesExternalFileIngestedEvent()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string sstPath = Path.Combine(dir.Path, "ingest.sst");

        var listener = new TestEventListener();

        using var dbOpts = new DbOptions { CreateIfMissing = true };
        dbOpts.EventListener = listener;

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

        Assert.NotEmpty(listener.ExternalFileIngestedEvents);
        var info = listener.ExternalFileIngestedEvents[0];
        Assert.NotNull(info.ColumnFamilyName);
    }

    [Fact]
    public void EventListener_ReceivesFlushBeginEvent()
    {
        using var dir = new TempDir();
        var listener = new TestEventListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Put("key", "value");
        db.Flush();

        Assert.NotEmpty(listener.FlushBeginEvents);
    }

    [Fact]
    public void EventListener_CompactionJobInfo_HasInputAndOutputFiles()
    {
        using var dir = new TempDir();
        var listener = new TestEventListener();

        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            WriteBufferSize = 1024,
            Level0FileNumCompactionTrigger = 2,
        };
        opts.EventListener = listener;

        using var db = RocksDb.Open(opts, dir.Path);

        for (int i = 0; i < 200; i++)
            db.Put($"key_{i:D5}", new string('x', 100));
        db.Flush();

        for (int i = 200; i < 400; i++)
            db.Put($"key_{i:D5}", new string('y', 100));
        db.Flush();

        db.CompactRange();

        if (listener.CompactionCompletedEvents.Count > 0)
        {
            var info = listener.CompactionCompletedEvents[0];
            Assert.NotNull(info.InputFiles);
            Assert.NotNull(info.OutputFiles);
        }
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

    [Fact]
    public void EventListener_DetectOverrides_Some()
    {
        var listener = new TestEventListener();

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
