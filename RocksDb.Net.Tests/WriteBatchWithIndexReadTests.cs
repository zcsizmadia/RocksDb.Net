using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Applying an indexed write batch and reading back through it. See issue #71.
/// </summary>
/// <remarks>
/// Before this the type could be built and counted but never applied to a
/// database or read from, so none of the behaviour below was reachable.
/// </remarks>
public class WriteBatchWithIndexReadTests
{
    private static string? S(byte[]? value) => value is null ? null : Encoding.UTF8.GetString(value);

    [Fact]
    public void Write_AppliesTheBatch()
    {
        using var db = new TempDb();

        using var batch = new WriteBatchWithIndex();
        batch.Put("a"u8, "1"u8);
        batch.Put("b"u8, "2"u8);

        db.Db.Write(batch);

        Assert.Equal("1", db.Db.GetString("a"));
        Assert.Equal("2", db.Db.GetString("b"));
    }

    [Fact]
    public void Write_AppliesDeletes()
    {
        using var db = new TempDb();
        db.Db.Put("gone", "value");

        using var batch = new WriteBatchWithIndex();
        batch.Delete("gone"u8);

        db.Db.Write(batch);

        Assert.Null(db.Db.GetString("gone"));
    }

    /// <summary>
    /// Applying does not clear the batch, so it can be applied to a second
    /// database.
    /// </summary>
    [Fact]
    public void Write_LeavesTheBatchReusable()
    {
        using var first = new TempDb();
        using var second = new TempDb();

        using var batch = new WriteBatchWithIndex();
        batch.Put("key"u8, "value"u8);

        first.Db.Write(batch);
        second.Db.Write(batch);

        Assert.Equal("value", first.Db.GetString("key"));
        Assert.Equal("value", second.Db.GetString("key"));
        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void Write_RejectsNull()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.Write((WriteBatchWithIndex)null!));
    }

    // ── GetFromBatch ─────────────────────────────────────────────────────────

    [Fact]
    public void GetFromBatch_SeesOnlyTheBatch()
    {
        using var db = new TempDb();
        db.Db.Put("in-db", "stored");

        using var options = new DbOptions();
        using var batch = new WriteBatchWithIndex();
        batch.Put("in-batch"u8, "queued"u8);

        Assert.Equal("queued", S(batch.GetFromBatch(options, "in-batch"u8)));

        // The database is not consulted.
        Assert.Null(batch.GetFromBatch(options, "in-db"u8));
    }

    [Fact]
    public void GetFromBatch_RejectsNullArguments()
    {
        using var batch = new WriteBatchWithIndex();

        Assert.Throws<ArgumentNullException>(() => batch.GetFromBatch(null!, "k"u8));
    }

    // ── GetFromBatchAndDb: read your own writes ──────────────────────────────

    /// <summary>
    /// The reason to choose an indexed batch: a queued write is visible before
    /// the batch is applied.
    /// </summary>
    [Fact]
    public void GetFromBatchAndDb_QueuedPutShadowsTheStoredValue()
    {
        using var db = new TempDb();
        db.Db.Put("key", "stored");

        using var batch = new WriteBatchWithIndex();
        batch.Put("key"u8, "queued"u8);

        Assert.Equal("queued", S(batch.GetFromBatchAndDb(db.Db, "key"u8)));

        // Still stored until the batch is applied.
        Assert.Equal("stored", db.Db.GetString("key"));
    }

    [Fact]
    public void GetFromBatchAndDb_QueuedDeleteHidesTheStoredValue()
    {
        using var db = new TempDb();
        db.Db.Put("key", "stored");

        using var batch = new WriteBatchWithIndex();
        batch.Delete("key"u8);

        Assert.Null(batch.GetFromBatchAndDb(db.Db, "key"u8));
        Assert.Equal("stored", db.Db.GetString("key"));
    }

    [Fact]
    public void GetFromBatchAndDb_FallsThroughToTheDatabase()
    {
        using var db = new TempDb();
        db.Db.Put("key", "stored");

        using var batch = new WriteBatchWithIndex();
        batch.Put("other"u8, "queued"u8);

        Assert.Equal("stored", S(batch.GetFromBatchAndDb(db.Db, "key"u8)));
        Assert.Null(batch.GetFromBatchAndDb(db.Db, "absent"u8));
    }

    /// <summary>
    /// A queued merge must be resolved against the stored value using the
    /// database's merge operator.
    /// </summary>
    [Fact]
    public void GetFromBatchAndDb_ResolvesAQueuedMergeAgainstTheStoredValue()
    {
        var options = new DbOptions { CreateIfMissing = true };
        options.SetUInt64AddMergeOperator();

        using var db = TestDb.OpenInMemory(options);
        db.Put("counter"u8, BitConverter.GetBytes(10UL));

        using var batch = new WriteBatchWithIndex();
        batch.Merge("counter"u8, BitConverter.GetBytes(5UL));

        byte[]? merged = batch.GetFromBatchAndDb(db, "counter"u8);
        Assert.NotNull(merged);
        Assert.Equal(15UL, BitConverter.ToUInt64(merged));

        // Unchanged in the database until applied.
        Assert.Equal(10UL, BitConverter.ToUInt64(db.Get("counter"u8)!));
    }

    /// <summary>
    /// A snapshot bounds the database side only. The batch's own writes are not
    /// in the database, so they stay visible.
    /// </summary>
    [Fact]
    public void GetFromBatchAndDb_SnapshotAppliesToTheDatabaseSideOnly()
    {
        using var db = new TempDb();
        db.Db.Put("stored", "before");

        using Snapshot snapshot = db.Db.NewSnapshot();
        using var readOptions = new ReadOptions();
        readOptions.SetSnapshot(snapshot);

        db.Db.Put("stored", "after");

        using var batch = new WriteBatchWithIndex();
        batch.Put("queued"u8, "value"u8);

        Assert.Equal("before", S(batch.GetFromBatchAndDb(db.Db, "stored"u8, readOptions)));
        Assert.Equal("value", S(batch.GetFromBatchAndDb(db.Db, "queued"u8, readOptions)));
    }

    [Fact]
    public void GetStringFromBatchAndDb_Works()
    {
        using var db = new TempDb();
        db.Db.Put("key", "stored");

        using var batch = new WriteBatchWithIndex();
        batch.Put("other"u8, "queued"u8);

        Assert.Equal("stored", batch.GetStringFromBatchAndDb(db.Db, "key"));
        Assert.Equal("queued", batch.GetStringFromBatchAndDb(db.Db, "other"));
        Assert.Null(batch.GetStringFromBatchAndDb(db.Db, "absent"));
    }

    [Fact]
    public void GetFromBatchAndDb_RejectsNullArguments()
    {
        using var batch = new WriteBatchWithIndex();

        Assert.Throws<ArgumentNullException>(() => batch.GetFromBatchAndDb(null!, "k"u8));
    }

    // ── Column families ─────────────────────────────────────────────────────

    [Fact]
    public void ColumnFamilyOverloads_ReadFromTheRightFamily()
    {
        using var dir = new TempDir();
        using var options = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using var db = RocksDb.Open(options, dir.Path, [new("default"), new("cf1")]);
        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");

        db.Put("key", "in-cf1", cf1);

        using var batchOptions = new DbOptions();
        using var batch = new WriteBatchWithIndex();
        batch.Put("key"u8, "queued-in-cf1"u8, cf1);

        Assert.Equal("queued-in-cf1", S(batch.GetFromBatch(batchOptions, "key"u8, cf1)));
        Assert.Equal("queued-in-cf1", S(batch.GetFromBatchAndDb(db, "key"u8, cf1)));

        // The default family is untouched by that queued write.
        Assert.Null(batch.GetFromBatchAndDb(db, "key"u8));

        db.Write(batch);
        Assert.Equal("queued-in-cf1", db.GetString("key", cf1));

        Assert.Throws<ArgumentNullException>(() => batch.GetFromBatch(batchOptions, "key"u8, (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => batch.GetFromBatchAndDb(db, "key"u8, (ColumnFamilyHandle)null!));
    }

    // ── Overlay iteration ────────────────────────────────────────────────────

    /// <summary>
    /// The overlay iterator merges the database with the batch, so it shows
    /// what the data would look like once applied.
    /// </summary>
    [Fact]
    public void NewIteratorWithBase_MergesBothSourcesInOrder()
    {
        using var db = new TempDb();
        db.Db.Put("a", "stored");
        db.Db.Put("c", "stored");
        db.Db.Flush();

        using var batch = new WriteBatchWithIndex();
        batch.Put("b"u8, "queued"u8);
        batch.Put("a"u8, "overwritten"u8);

        var seen = new List<(string Key, string Value)>();
        using (Iterator iter = batch.NewIteratorWithBase(db.Db))
        {
            for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
            {
                seen.Add((iter.KeyAsString(), iter.ValueAsString()));
            }
        }

        Assert.Equal(
            [("a", "overwritten"), ("b", "queued"), ("c", "stored")],
            seen);
    }

    [Fact]
    public void NewIteratorWithBase_HidesDeletedKeys()
    {
        using var db = new TempDb();
        db.Db.Put("a", "stored");
        db.Db.Put("b", "stored");
        db.Db.Flush();

        using var batch = new WriteBatchWithIndex();
        batch.Delete("a"u8);

        var keys = new List<string>();
        using (Iterator iter = batch.NewIteratorWithBase(db.Db))
        {
            for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
            {
                keys.Add(iter.KeyAsString());
            }
        }

        Assert.Equal(["b"], keys);
    }

    [Fact]
    public void NewIteratorWithBase_ColumnFamily_MergesThatFamilyOnly()
    {
        using var dir = new TempDir();
        using var options = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using var db = RocksDb.Open(options, dir.Path, [new("default"), new("cf1")]);
        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");

        db.Put("a", "stored", cf1);
        db.Put("zzz", "in-default");
        db.Flush(cf1);

        using var batch = new WriteBatchWithIndex();
        batch.Put("b"u8, "queued"u8, cf1);

        var keys = new List<string>();
        using (Iterator iter = batch.NewIteratorWithBase(db, cf1))
        {
            for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
            {
                keys.Add(iter.KeyAsString());
            }
        }

        Assert.Equal(["a", "b"], keys);
    }

    /// <summary>
    /// The overlay iterator reads through two objects, so it holds both. Forcing
    /// collections while it is open must not disturb it.
    /// </summary>
    [Fact]
    public void NewIteratorWithBase_HoldsBothSourcesAlive()
    {
        using var db = new TempDb();
        db.Db.Put("a", "stored");
        db.Db.Flush();

        using var batch = new WriteBatchWithIndex();
        batch.Put("b"u8, "queued"u8);

        using Iterator iter = batch.NewIteratorWithBase(db.Db);
        Assert.True(iter.HasSecondarySource);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        var keys = new List<string>();
        for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
        {
            keys.Add(iter.KeyAsString());
        }

        Assert.Equal(["a", "b"], keys);
    }

    /// <summary>
    /// The base iterator is created inside the call because RocksDb deletes the
    /// one it is handed. Creating two overlays must therefore both work, rather
    /// than the second tripping over the first.
    /// </summary>
    [Fact]
    public void NewIteratorWithBase_CanBeCalledRepeatedly()
    {
        using var db = new TempDb();
        db.Db.Put("a", "stored");
        db.Db.Flush();

        using var batch = new WriteBatchWithIndex();
        batch.Put("b"u8, "queued"u8);

        for (int i = 0; i < 5; i++)
        {
            using Iterator iter = batch.NewIteratorWithBase(db.Db);
            iter.SeekToFirst();
            Assert.True(iter.IsValid());
            Assert.Equal("a", iter.KeyAsString());
        }
    }

    [Fact]
    public void NewIteratorWithBase_RejectsNullArguments()
    {
        using var db = new TempDb();
        using var batch = new WriteBatchWithIndex();

        Assert.Throws<ArgumentNullException>(() => batch.NewIteratorWithBase(null!));
        Assert.Throws<ArgumentNullException>(() => batch.NewIteratorWithBase(db.Db, (ColumnFamilyHandle)null!));
    }

    /// <summary>
    /// End to end: build a batch, read through it, then apply it and confirm
    /// the database now matches what the overlay showed.
    /// </summary>
    [Fact]
    public void OverlayMatchesTheDatabaseAfterApplying()
    {
        using var db = new TempDb();
        db.Db.Put("a", "stored");
        db.Db.Put("b", "stored");
        db.Db.Flush();

        using var batch = new WriteBatchWithIndex();
        batch.Put("a"u8, "changed"u8);
        batch.Delete("b"u8);
        batch.Put("c"u8, "added"u8);

        var overlay = new List<(string, string)>();
        using (Iterator iter = batch.NewIteratorWithBase(db.Db))
        {
            for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
            {
                overlay.Add((iter.KeyAsString(), iter.ValueAsString()));
            }
        }

        db.Db.Write(batch);

        var applied = new List<(string, string)>();
        using (Iterator iter = db.Db.NewIterator())
        {
            for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
            {
                applied.Add((iter.KeyAsString(), iter.ValueAsString()));
            }
        }

        Assert.Equal(overlay, applied);
    }
}
