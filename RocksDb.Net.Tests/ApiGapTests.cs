using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Native functions that were bound but unreachable from the managed API. See
/// issues #190, #193, #194 and #195.
/// </summary>
public class ApiGapTests
{
    private static DbOptions Create() => new() { CreateIfMissing = true, CreateMissingColumnFamilies = true };

    private static ColumnFamilyDescriptor[] Families(params string[] names)
        => [.. names.Select(n => new ColumnFamilyDescriptor(n))];

    // ── #190: secondary and TTL databases with column families ──────────────

    [Fact]
    public void OpenAsSecondary_WithColumnFamilies_SeesThePrimarysFamilies()
    {
        using var dir = new TempDir();
        string primaryPath = dir.Sub("primary");

        using RocksDb primary = RocksDb.Open(Create(), primaryPath, Families("default", "orders"));
        primary.Put("k", "default-value");
        primary.Put("k", "orders-value", primary.GetColumnFamily("orders"));
        primary.Flush();

        using RocksDb secondary = RocksDb.OpenAsSecondary(
            new DbOptions(), primaryPath, dir.Sub("secondary"), Families("default", "orders"));

        Assert.Equal("default-value", secondary.GetString("k"));
        Assert.Equal("orders-value", secondary.GetString("k", secondary.GetColumnFamily("orders")));

        // Later writes are seen after catching up.
        primary.Put("later", "x", primary.GetColumnFamily("orders"));
        primary.Flush(primary.GetColumnFamily("orders"));
        secondary.TryCatchUpWithPrimary();

        Assert.Equal("x", secondary.GetString("later", secondary.GetColumnFamily("orders")));
    }

    [Fact]
    public void OpenWithTtl_WithColumnFamilies_OpensEachFamily()
    {
        using var dir = new TempDir();

        using (RocksDb db = RocksDb.OpenWithTtl(Create(), dir.Path, Families("default", "short", "forever"), [3600, 1, 0]))
        {
            db.Put("k", "v", db.GetColumnFamily("short"));
            db.Put("k", "v", db.GetColumnFamily("forever"));

            Assert.Equal("v", db.GetString("k", db.GetColumnFamily("short")));
            Assert.Equal("v", db.GetString("k", db.GetColumnFamily("forever")));
        }

        // Reopens with the same families, which a TTL database with column
        // families could not do at all before.
        using RocksDb reopened = RocksDb.OpenWithTtl(new DbOptions(), dir.Path, Families("default", "short", "forever"), [3600, 1, 0]);
        Assert.Equal("v", reopened.GetString("k", reopened.GetColumnFamily("forever")));
    }

    [Fact]
    public void OpenWithTtl_RequiresOneTtlPerFamily()
    {
        using var dir = new TempDir();

        Assert.Throws<ArgumentException>(
            () => RocksDb.OpenWithTtl(Create(), dir.Path, Families("default", "other"), [60]));
    }

    // ── #194: TransactionDb ──────────────────────────────────────────────────

    [Fact]
    public void TransactionDb_MultiGet_ReadsCommittedValues()
    {
        using var dir = new TempDir();
        using var txnOptions = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(Create(), txnOptions, dir.Path, Families("default", "other"));
        ColumnFamilyHandle other = db.GetColumnFamily("other");

        db.Put("a", "1");
        db.Put("b", "2", other);

        using (Transaction pending = db.BeginTransaction())
        {
            pending.Put("c", "uncommitted");

            byte[]?[] values = db.MultiGet(["a"u8.ToArray(), "c"u8.ToArray()]);
            Assert.Equal("1"u8.ToArray(), values[0]);
            Assert.Null(values[1]);
        }

        Assert.Equal("2"u8.ToArray(), db.MultiGet(["b"u8.ToArray()], other)[0]);

        byte[]?[] mixed = db.MultiGet(["a"u8.ToArray(), "b"u8.ToArray()], [db.GetDefaultColumnFamily(), other]);
        Assert.Equal("1"u8.ToArray(), mixed[0]);
        Assert.Equal("2"u8.ToArray(), mixed[1]);
    }

    [Fact]
    public void TransactionDb_FlushesChosenFamilies_AndCheckpoints()
    {
        using var dir = new TempDir();
        using var txnOptions = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(Create(), txnOptions, dir.Sub("db"), Families("default", "other"));

        db.Put("k", "v", db.GetColumnFamily("other"));
        db.Flush([db.GetColumnFamily("other")]);
        db.Flush([]);

        string checkpointPath = Path.Combine(dir.Path, "checkpoint");
        using (Checkpoint checkpoint = db.CreateCheckpoint())
        {
            checkpoint.CreateCheckpoint(checkpointPath);
        }

        using RocksDb copy = RocksDb.Open(new DbOptions(), checkpointPath, Families("default", "other"));
        Assert.Equal("v", copy.GetString("k", copy.GetColumnFamily("other")));
    }

    [Fact]
    public void Transaction_PutLogData_ReachesTheWalButNotTheDatabase()
    {
        using var dir = new TempDir();
        using var txnOptions = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(Create(), txnOptions, dir.Path);

        using (Transaction txn = db.BeginTransaction())
        {
            txn.Put("k", "v");
            txn.PutLogData("marker"u8);
            txn.Commit();
        }

        Assert.Equal("v", db.GetString("k"));
        Assert.Null(db.GetString("marker"));
    }

    [Fact]
    public void Transaction_RebuildFromWriteBatch_AppliesTheBatchAsItsOwnWrites()
    {
        using var dir = new TempDir();
        using var txnOptions = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(Create(), txnOptions, dir.Path);

        db.Put("gone", "x");

        using var batch = new WriteBatch();
        batch.Put("a", "1");
        batch.Delete("gone");

        using var indexed = new WriteBatchWithIndex();
        indexed.Put("b", "2");

        using (Transaction txn = db.BeginTransaction())
        {
            txn.RebuildFromWriteBatch(batch);
            txn.RebuildFromWriteBatch(indexed);

            // Visible to the transaction, not yet to the database.
            Assert.Equal("1", txn.GetString("a"));
            Assert.Null(db.GetString("a"));

            txn.Commit();
        }

        Assert.Equal("1", db.GetString("a"));
        Assert.Equal("2", db.GetString("b"));
        Assert.Null(db.GetString("gone"));

        // The batch was copied from, not consumed.
        Assert.Equal(2, batch.Count);
    }

    // ── #193: batches ────────────────────────────────────────────────────────

    [Fact]
    public void ProtectedBatches_WriteNormally()
    {
        using var db = new TempDb();

        using var batch = new WriteBatch(reservedBytes: 1024, protectionBytesPerKey: 8);
        batch.Put("a", "1");

        // Counted straight away. A Put the batch refuses is dropped without an
        // error through the C API, which is how a misordered size argument once
        // capped the batch at eight bytes and lost every write.
        Assert.Equal(1, batch.Count);
        db.Db.Write(batch);

        using var indexed = new WriteBatchWithIndex(reservedBytes: 0, overwriteKeys: true, protectionBytesPerKey: 8);
        indexed.Put("b", "2");
        Assert.Equal(1, indexed.Count);
        db.Db.Write(indexed);

        Assert.Equal("1", db.Db.GetString("a"));
        Assert.Equal("2", db.Db.GetString("b"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    public void ProtectedBatches_RejectAnUnsupportedWidth(int width)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WriteBatch(0, width));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WriteBatchWithIndex(0, true, width));
    }

    [Fact]
    public void GetPinnedFromBatchAndDb_LayersTheBatchOverTheDatabase()
    {
        using var db = new TempDb();
        db.Db.Put("db-only", "from-db");
        db.Db.Put("both", "from-db");

        using var batch = new WriteBatchWithIndex();
        batch.Put("both", "from-batch");
        batch.Put("batch-only", "from-batch");

        using (PinnableSlice? fromDb = batch.GetPinnedFromBatchAndDb(db.Db, "db-only"u8))
        {
            Assert.Equal("from-db", fromDb!.ToUtf8String());
        }

        PinnableSlice? fromBatch = batch.GetPinnedFromBatchAndDb(db.Db, "both"u8, db.Db.GetDefaultColumnFamily());

        // Copied out of the batch, so changing the batch does not change it.
        batch.Put("both", "changed");
        Assert.Equal("from-batch", fromBatch!.ToUtf8String());
        fromBatch.Dispose();

        Assert.Null(batch.GetPinnedFromBatchAndDb(db.Db, "missing"u8));
    }

    // ── #195: tuning options ─────────────────────────────────────────────────

    [Fact]
    public void CompressionSettings_ApplyAndTheDatabaseStillWorks()
    {
        using var db = new TempDb(o =>
        {
            o.SetCompressionPerLevel([Compression.None, Compression.None, Compression.Lz4, Compression.Zstd]);
            o.SetCompressionOptions(level: 3, maxDictBytes: 16 * 1024);
            o.SetBottommostCompressionOptions(level: 19, maxDictBytes: 16 * 1024);
            o.SetBottommostCompressionOptionsZstdMaxTrainBytes(100 * 16 * 1024);
            o.SetBottommostCompressionOptionsMaxDictBufferBytes(1 << 20);
            o.SetMaxBytesForLevelMultiplierAdditional([1, 2, 1]);
        });

        for (int i = 0; i < 1000; i++)
        {
            db.Db.Put($"key{i:D5}", new string('v', 100));
        }

        db.Db.Flush();
        db.Db.CompactRange();

        Assert.Equal(new string('v', 100), db.Db.GetString("key00500"));
    }

    [Fact]
    public void ColumnFamilyPaths_PlaceTheFamilysFiles()
    {
        using var dir = new TempDir();
        string familyDir = dir.Sub("family-files");

        using var cfOptions = new DbOptions();
        using var path = new DbPath(familyDir, 1L << 30);
        cfOptions.SetColumnFamilyPaths([path]);

        using RocksDb db = RocksDb.Open(Create(), dir.Sub("db"), [new ColumnFamilyDescriptor("default"), new ColumnFamilyDescriptor("placed", cfOptions)]);
        ColumnFamilyHandle placed = db.GetColumnFamily("placed");

        db.Put("k", "v", placed);
        db.Flush(placed);

        Assert.NotEmpty(Directory.GetFiles(familyDir, "*.sst"));
        Assert.Equal("v", db.GetString("k", placed));
    }

    [Theory]
    [InlineData(RateLimiterMode.ReadsOnly, false)]
    [InlineData(RateLimiterMode.WritesOnly, true)]
    [InlineData(RateLimiterMode.AllIo, false)]
    public void RateLimiters_OfEveryKind_CanBeAttached(RateLimiterMode mode, bool autoTuned)
    {
        using RateLimiter limiter = RateLimiter.Create(64L << 20, mode, autoTuned);
        using RateLimiter tuned = RateLimiter.CreateAutoTuned(64L << 20);

        using var db = new TempDb(o => o.RateLimiter = limiter);
        db.Db.Put("k", "v");
        db.Db.Flush();

        Assert.Equal("v", db.Db.GetString("k"));
    }

    [Fact]
    public void RateLimiterMode_MatchesRocksDb()
        => NativeEnum.AssertExactly<RateLimiterMode>(("ReadsOnly", 0), ("WritesOnly", 1), ("AllIo", 2));

    [Fact]
    public void MemtableInsertHintPerBatch_RoundTrips()
    {
        using var options = new WriteOptions();
        Assert.False(options.MemtableInsertHintPerBatch);

        options.MemtableInsertHintPerBatch = true;
        Assert.True(options.MemtableInsertHintPerBatch);

        using var db = new TempDb();
        using var batch = new WriteBatch();
        for (int i = 0; i < 100; i++)
        {
            batch.Put($"k{i:D3}", "v");
        }

        db.Db.Write(batch, options);
        Assert.Equal("v", db.Db.GetString("k050"));
    }

    [Fact]
    public void SstFileWriter_DeleteRange_DeletesTheRangeOnIngest()
    {
        using var dir = new TempDir();
        using RocksDb db = RocksDb.Open(Create(), dir.Sub("db"));

        foreach (string key in new[] { "a", "b", "c", "d" })
        {
            db.Put(key, key);
        }

        string file = Path.Combine(dir.Path, "range.sst");
        using (var options = new DbOptions())
        using (SstFileWriter writer = SstFileWriter.Create(options))
        {
            writer.Open(file);
            writer.DeleteRange("b"u8, "d"u8);
            writer.Finish();
        }

        using var ingest = new IngestExternalFileOptions();
        db.IngestExternalFile([file], ingest);

        Assert.Equal("a", db.GetString("a"));
        Assert.Null(db.GetString("b"));
        Assert.Null(db.GetString("c"));
        Assert.Equal("d", db.GetString("d"));
    }
}
