using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Reading a write batch back, which completes the change-data-capture story.
/// See issue #75.
/// </summary>
public class WriteBatchEntriesTests
{
    private static string Str(byte[]? bytes) => bytes is null ? "<null>" : Encoding.UTF8.GetString(bytes);

    [Fact]
    public void Entries_OnAnEmptyBatch_IsEmpty()
    {
        using var batch = new WriteBatch();

        Assert.Empty(batch.Entries());
    }

    [Fact]
    public void Entries_ReportsPutsInOrder()
    {
        using var batch = new WriteBatch();
        batch.Put("a"u8, "1"u8);
        batch.Put("b"u8, "2"u8);
        batch.Put("c"u8, "3"u8);

        IReadOnlyList<WriteBatchEntry> entries = batch.Entries();

        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal(WriteBatchEntryKind.Put, e.Kind));
        Assert.Equal(["a", "b", "c"], entries.Select(e => Str(e.Key)));
        Assert.Equal(["1", "2", "3"], entries.Select(e => Str(e.Value)));
    }

    /// <summary>
    /// A delete carries no value, and the distinction from a put of an empty
    /// value has to survive.
    /// </summary>
    [Fact]
    public void Entries_DistinguishesADeleteFromAnEmptyPut()
    {
        using var batch = new WriteBatch();
        batch.Put("empty"u8, []);
        batch.Delete("gone"u8);

        IReadOnlyList<WriteBatchEntry> entries = batch.Entries();

        Assert.Equal(2, entries.Count);

        Assert.Equal(WriteBatchEntryKind.Put, entries[0].Kind);
        Assert.NotNull(entries[0].Value);
        Assert.Empty(entries[0].Value!);

        Assert.Equal(WriteBatchEntryKind.Delete, entries[1].Kind);
        Assert.Null(entries[1].Value);
    }

    [Fact]
    public void Entries_ReportsMerges()
    {
        using var batch = new WriteBatch();
        batch.Merge("counter"u8, "5"u8);

        WriteBatchEntry entry = Assert.Single(batch.Entries());

        Assert.Equal(WriteBatchEntryKind.Merge, entry.Kind);
        Assert.Equal("counter", Str(entry.Key));
        Assert.Equal("5", Str(entry.Value));
    }

    /// <summary>
    /// Log data is carried in the write-ahead log but never stored against a
    /// key, so it has no key and its blob arrives as the value.
    /// </summary>
    [Fact]
    public void Entries_ReportsLogData()
    {
        using var batch = new WriteBatch();
        batch.Put("key"u8, "value"u8);
        batch.PutLogData("audit trail"u8);

        IReadOnlyList<WriteBatchEntry> entries = batch.Entries();

        WriteBatchEntry logData = Assert.Single(entries, e => e.Kind == WriteBatchEntryKind.LogData);
        Assert.Empty(logData.Key);
        Assert.Equal("audit trail", Str(logData.Value));
    }

    /// <summary>
    /// Column families appear as numeric ids, because that is what the batch
    /// records. They must match the handle's id.
    /// </summary>
    [Fact]
    public void Entries_ReportsColumnFamilyIds()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using var db = RocksDb.Open(opts, dir.Path, [new("default"), new("cf1")]);

        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");
        ColumnFamilyHandle defaultCf = db.GetDefaultColumnFamily();

        using var batch = new WriteBatch();
        batch.Put("in-default"u8, "1"u8);
        batch.Put("in-cf1"u8, "2"u8, cf1);

        IReadOnlyList<WriteBatchEntry> entries = batch.Entries();

        Assert.Equal(2, entries.Count);
        Assert.Equal(defaultCf.Id, entries[0].ColumnFamilyId);
        Assert.Equal(cf1.Id, entries[1].ColumnFamilyId);
        Assert.NotEqual(entries[0].ColumnFamilyId, entries[1].ColumnFamilyId);
    }

    [Fact]
    public void Entries_MixedOperations_AllAppear()
    {
        using var batch = new WriteBatch();
        batch.Put("a"u8, "1"u8);
        batch.Delete("b"u8);
        batch.Merge("c"u8, "op"u8);
        batch.PutLogData("blob"u8);

        IReadOnlyList<WriteBatchEntry> entries = batch.Entries();

        Assert.Equal(
            [WriteBatchEntryKind.Put, WriteBatchEntryKind.Delete, WriteBatchEntryKind.Merge, WriteBatchEntryKind.LogData],
            entries.Select(e => e.Kind));
    }

    /// <summary>
    /// The entries are copied during the call, so they outlive the batch.
    /// </summary>
    [Fact]
    public void Entries_SurviveTheBatchBeingDisposed()
    {
        IReadOnlyList<WriteBatchEntry> entries;

        using (var batch = new WriteBatch())
        {
            batch.Put("key"u8, "value"u8);
            entries = batch.Entries();
        }

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        WriteBatchEntry entry = Assert.Single(entries);
        Assert.Equal("key", Str(entry.Key));
        Assert.Equal("value", Str(entry.Value));
    }

    [Fact]
    public void Entries_AfterDispose_Throws()
    {
        var batch = new WriteBatch();
        batch.Put("key"u8, "value"u8);
        batch.Dispose();

        Assert.Throws<ObjectDisposedException>(() => batch.Entries());
    }

    [Fact]
    public void Entries_CanBeCalledRepeatedly()
    {
        using var batch = new WriteBatch();
        batch.Put("key"u8, "value"u8);

        Assert.Single(batch.Entries());
        Assert.Single(batch.Entries());

        batch.Put("second"u8, "value"u8);
        Assert.Equal(2, batch.Entries().Count);
    }

    /// <summary>
    /// A batch large enough to exercise the callback path repeatedly, since
    /// each record is a separate reverse call into managed code.
    /// </summary>
    [Fact]
    public void Entries_HandlesALargeBatch()
    {
        using var batch = new WriteBatch();

        for (int i = 0; i < 2000; i++)
        {
            batch.Put(Encoding.UTF8.GetBytes($"key{i:D5}"), Encoding.UTF8.GetBytes($"value{i}"));
        }

        IReadOnlyList<WriteBatchEntry> entries = batch.Entries();

        Assert.Equal(2000, entries.Count);
        Assert.Equal("key00000", Str(entries[0].Key));
        Assert.Equal("value1999", Str(entries[1999].Value));
    }

    // ── The change-data-capture path, end to end ─────────────────────────────

    /// <summary>
    /// What the feature is for: write to a database, read the batch back out of
    /// the write-ahead log, and inspect what it contains.
    /// </summary>
    /// <remarks>
    /// This is the assertion nothing previously made. The wrapper could reach
    /// the log and hand back a batch, but there was no way to see inside it, so
    /// the change-data-capture support the changelog advertised stopped one step
    /// short of being usable.
    /// </remarks>
    [Fact]
    public void GetUpdatesSince_ProducesBatchesWhoseContentsCanBeRead()
    {
        using var db = new TempDb();

        db.Db.Put("before", "ignored");

        // Plus one, because GetUpdatesSince is inclusive of the sequence number
        // given. Passing LatestSequenceNumber alone replays the write that
        // produced it, which is an easy off-by-one for a consumer resuming from
        // a checkpoint.
        ulong from = db.Db.LatestSequenceNumber + 1;

        db.Db.Put("added", "1");
        db.Db.Delete("removed");
        db.Db.Put("also-added", "2");

        var captured = new List<WriteBatchEntry>();

        using (WalIterator iterator = db.Db.GetUpdatesSince(from))
        {
            // AsEnumerable disposes each batch once the consumer moves past it,
            // and the entries are copies, so collecting them is safe.
            foreach ((WriteBatch batch, ulong _) in iterator.AsEnumerable())
            {
                captured.AddRange(batch.Entries());
            }
        }

        Assert.Contains(captured, e => e.Kind == WriteBatchEntryKind.Put && Str(e.Key) == "added");
        Assert.Contains(captured, e => e.Kind == WriteBatchEntryKind.Put && Str(e.Key) == "also-added");
        Assert.Contains(captured, e => e.Kind == WriteBatchEntryKind.Delete && Str(e.Key) == "removed");

        // The write before the starting point is not replayed.
        Assert.DoesNotContain(captured, e => Str(e.Key) == "before");
    }

    /// <summary>
    /// The inclusive boundary, pinned. Passing the latest sequence number
    /// replays the write that produced it.
    /// </summary>
    [Fact]
    public void GetUpdatesSince_IsInclusiveOfTheSequenceNumberGiven()
    {
        using var db = new TempDb();

        db.Db.Put("boundary", "value");
        ulong latest = db.Db.LatestSequenceNumber;

        var captured = new List<WriteBatchEntry>();
        using (WalIterator iterator = db.Db.GetUpdatesSince(latest))
        {
            foreach ((WriteBatch batch, ulong _) in iterator.AsEnumerable())
            {
                captured.AddRange(batch.Entries());
            }
        }

        Assert.Contains(captured, e => Str(e.Key) == "boundary");
    }

    /// <summary>
    /// A captured batch can be replayed into another database, which is what a
    /// replication consumer would do with it.
    /// </summary>
    [Fact]
    public void CapturedEntries_CanBeReplayedIntoAnotherDatabase()
    {
        using var source = new TempDb();
        using var target = new TempDb();

        ulong from = source.Db.LatestSequenceNumber;

        source.Db.Put("a", "1");
        source.Db.Put("b", "2");
        source.Db.Delete("a");

        using (WalIterator iterator = source.Db.GetUpdatesSince(from))
        {
            foreach ((WriteBatch batch, ulong _) in iterator.AsEnumerable())
            {
                // Translate rather than apply wholesale, which is the point of
                // being able to read the entries at all.
                using var replay = new WriteBatch();
                foreach (WriteBatchEntry entry in batch.Entries())
                {
                    switch (entry.Kind)
                    {
                        case WriteBatchEntryKind.Put:
                            replay.Put(entry.Key, entry.Value!);
                            break;
                        case WriteBatchEntryKind.Delete:
                            replay.Delete(entry.Key);
                            break;
                        default:
                            break;
                    }
                }

                target.Db.Write(replay);
            }
        }

        Assert.Null(target.Db.GetString("a"));
        Assert.Equal("2", target.Db.GetString("b"));
    }
}
