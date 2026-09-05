using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// <see cref="OptimisticTransactionDb"/>. See issue #164.
/// </summary>
/// <remarks>
/// The cases worth writing are the ones that separate optimistic from
/// pessimistic concurrency: a write that would block on a
/// <see cref="TransactionDb"/> goes straight through here, and the conflict
/// surfaces at commit instead. Anything that merely repeats
/// <see cref="Transaction"/>'s own behaviour is already covered there.
/// </remarks>
public class OptimisticTransactionDbTests
{
    private static DbOptions NewDbOptions() => new() { CreateIfMissing = true };

    private sealed class Db : IDisposable
    {
        private readonly TempDir _dir = new();

        public Db() => Value = OptimisticTransactionDb.Open(NewDbOptions(), _dir.Path);

        public OptimisticTransactionDb Value { get; }

        public void Dispose()
        {
            Value.Dispose();
            _dir.Dispose();
        }
    }

    // ── The behaviour that makes it worth having ────────────────────────────

    /// <summary>
    /// Two transactions writing the same key both proceed — no lock, no wait —
    /// and the second to commit is the one that fails.
    /// </summary>
    /// <remarks>
    /// On a <see cref="TransactionDb"/> the second <c>Put</c> would block and
    /// then throw. That difference is the entire reason this type exists.
    /// </remarks>
    [Fact]
    public void TwoTransactionsWritingTheSameKey_BothProceed_AndTheSecondCommitFails()
    {
        using var db = new Db();
        Write(db.Value, ("k", "initial"));

        using Transaction first = db.Value.BeginTransaction();
        using Transaction second = db.Value.BeginTransaction();

        // Neither of these blocks, which is the point.
        first.Put("k", "from-first");
        second.Put("k", "from-second");

        first.Commit();

        Assert.Throws<RocksDbException>(second.Commit);

        Assert.Equal("from-first", Read(db.Value, "k"));
    }

    /// <summary>
    /// Without contention both commit, which is the case the type is tuned for.
    /// </summary>
    [Fact]
    public void TwoTransactionsOnDifferentKeys_BothCommit()
    {
        using var db = new Db();

        using Transaction first = db.Value.BeginTransaction();
        using Transaction second = db.Value.BeginTransaction();

        first.Put("a", "1");
        second.Put("b", "2");

        first.Commit();
        second.Commit();

        Assert.Equal("1", Read(db.Value, "a"));
        Assert.Equal("2", Read(db.Value, "b"));
    }

    /// <summary>
    /// A failed commit leaves the database untouched and the winner's write
    /// intact, so retrying is a sound response to it.
    /// </summary>
    [Fact]
    public void AConflictingTransaction_CanBeRetriedAfterItFails()
    {
        using var db = new Db();
        Write(db.Value, ("counter", "1"));

        using Transaction loser = db.Value.BeginTransaction();
        loser.GetForUpdate("counter"u8.ToArray());
        loser.Put("counter", "loser");

        using (Transaction winner = db.Value.BeginTransaction())
        {
            winner.Put("counter", "2");
            winner.Commit();
        }

        Assert.Throws<RocksDbException>(loser.Commit);

        // The retry sees the winner's value and succeeds.
        using Transaction retry = db.Value.BeginTransaction();
        Assert.Equal("2", retry.GetString("counter"));
        retry.Put("counter", "3");
        retry.Commit();

        Assert.Equal("3", Read(db.Value, "counter"));
    }

    /// <summary>
    /// With a snapshot pinned at begin, a read-modify-write conflicts even
    /// though the transaction never wrote the key it read. Without one, the
    /// same sequence commits.
    /// </summary>
    [Fact]
    public void SetSnapshot_MakesAReadOfAChangedKeyConflict()
    {
        using var db = new Db();
        Write(db.Value, ("read", "original"));

        using var snapshotOpts = new OptimisticTransactionOptions { SetSnapshot = true };
        using var writeOpts = new WriteOptions();

        using Transaction txn = db.Value.BeginTransaction(writeOpts, snapshotOpts);

        // Read for update, so the key is tracked and validated at commit.
        Assert.Equal("original", txn.GetStringForUpdate("read"));
        txn.Put("written", "value");

        using (Transaction other = db.Value.BeginTransaction())
        {
            other.Put("read", "changed");
            other.Commit();
        }

        Assert.Throws<RocksDbException>(txn.Commit);
    }

    [Fact]
    public void SetSnapshot_RoundTrips()
    {
        using var opts = new OptimisticTransactionOptions();

        Assert.False(opts.SetSnapshot);

        opts.SetSnapshot = true;
        Assert.True(opts.SetSnapshot);

        opts.SetSnapshot = false;
        Assert.False(opts.SetSnapshot);
    }

    // ── Ordinary transaction behaviour still works ──────────────────────────

    [Fact]
    public void ATransaction_SeesItsOwnWritesAndHidesThemUntilCommit()
    {
        using var db = new Db();

        using Transaction txn = db.Value.BeginTransaction();
        txn.Put("k", "pending");

        Assert.Equal("pending", txn.GetString("k"));
        Assert.Null(Read(db.Value, "k"));

        txn.Commit();
        Assert.Equal("pending", Read(db.Value, "k"));
    }

    [Fact]
    public void Rollback_DiscardsTheWrites()
    {
        using var db = new Db();

        using Transaction txn = db.Value.BeginTransaction();
        txn.Put("k", "discarded");
        txn.Rollback();

        Assert.Null(Read(db.Value, "k"));
    }

    /// <summary>The batched reads added alongside this work here too.</summary>
    [Fact]
    public void MultiGet_WorksOnAnOptimisticTransaction()
    {
        using var db = new Db();
        Write(db.Value, ("a", "1"), ("c", "3"));

        using Transaction txn = db.Value.BeginTransaction();
        txn.Put("b", "2");

        byte[]?[] values = txn.MultiGet(["a"u8.ToArray(), "b"u8.ToArray(), "c"u8.ToArray()]);

        Assert.Equal(["1", "2", "3"], values.Select(v => Encoding.UTF8.GetString(v!)));
    }

    // ── Writes outside a transaction ────────────────────────────────────────

    [Fact]
    public void Write_AppliesABatchDirectly()
    {
        using var db = new Db();

        Write(db.Value, ("a", "1"), ("b", "2"));

        Assert.Equal("1", Read(db.Value, "a"));
        Assert.Equal("2", Read(db.Value, "b"));
    }

    [Fact]
    public void Write_WithNullBatch_Throws()
    {
        using var db = new Db();
        Assert.Throws<ArgumentNullException>(() => db.Value.Write(null!));
    }

    // ── Options ─────────────────────────────────────────────────────────────

    [Fact]
    public void DbOptions_RoundTrip()
    {
        using var opts = new OptimisticTransactionDbOptions();

        // The default is the parallel policy, which is why it is the default.
        Assert.Equal(OccValidationPolicy.ValidateParallel, opts.ValidatePolicy);

        opts.ValidatePolicy = OccValidationPolicy.ValidateSerial;
        Assert.Equal(OccValidationPolicy.ValidateSerial, opts.ValidatePolicy);

        opts.OccLockBucketCount = 64;
        Assert.Equal(64U, opts.OccLockBucketCount);
    }

    [Fact]
    public void OpeningWithOptions_Works()
    {
        using var dir = new TempDir();
        using var otxnOpts = new OptimisticTransactionDbOptions
        {
            ValidatePolicy = OccValidationPolicy.ValidateSerial,
            OccLockBucketCount = 16,
        };

        using OptimisticTransactionDb db = OptimisticTransactionDb.Open(NewDbOptions(), otxnOpts, dir.Path);

        using Transaction txn = db.BeginTransaction();
        txn.Put("k", "v");
        txn.Commit();

        Assert.Equal("v", Read(db, "k"));
    }

    /// <summary>
    /// Shared buckets are the reason they are an object rather than a number:
    /// two databases use the one set, and it outlives neither.
    /// </summary>
    [Fact]
    public void SharedLockBuckets_CanBeUsedBySeveralDatabases()
    {
        using var first = new TempDir();
        using var second = new TempDir();
        using var buckets = new OccLockBuckets(32, cacheAligned: true);

        Assert.True(buckets.ApproximateMemoryUsage > 0);

        using var otxnOpts = new OptimisticTransactionDbOptions();
        otxnOpts.SetSharedLockBuckets(buckets);

        using OptimisticTransactionDb a = OptimisticTransactionDb.Open(NewDbOptions(), otxnOpts, first.Path);
        using OptimisticTransactionDb b = OptimisticTransactionDb.Open(NewDbOptions(), otxnOpts, second.Path);

        using (Transaction txn = a.BeginTransaction())
        {
            txn.Put("k", "a");
            txn.Commit();
        }

        using (Transaction txn = b.BeginTransaction())
        {
            txn.Put("k", "b");
            txn.Commit();
        }

        Assert.Equal("a", Read(a, "k"));
        Assert.Equal("b", Read(b, "k"));
    }

    [Fact]
    public void OccLockBuckets_RejectsAZeroCount()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new OccLockBuckets(0));

    // ── Column families ─────────────────────────────────────────────────────

    [Fact]
    public void OpeningWithColumnFamilies_ResolvesThem()
    {
        using var dir = new TempDir();
        using var dbOpts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        using OptimisticTransactionDb db = OptimisticTransactionDb.Open(
            dbOpts, dir.Path, [new("default"), new("other")]);

        Assert.Equal(["default", "other"], db.ColumnFamilyNames.Order());

        ColumnFamilyHandle other = db.GetColumnFamily("other");

        using Transaction txn = db.BeginTransaction();
        txn.Put("k", "in-other", other);
        txn.Commit();

        using Transaction reader = db.BeginTransaction();
        Assert.Equal("in-other", reader.GetString("k", other));
        Assert.Null(reader.GetString("k"));
    }

    [Fact]
    public void GetColumnFamily_ForAnUnknownName_Throws()
    {
        using var dir = new TempDir();
        using var dbOpts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using OptimisticTransactionDb db = OptimisticTransactionDb.Open(
            dbOpts, dir.Path, [new("default")]);

        Assert.Throws<KeyNotFoundException>(() => db.GetColumnFamily("nope"));
        Assert.False(db.TryGetColumnFamily("nope", out _));
    }

    [Fact]
    public void OpeningWithNoColumnFamilies_Throws()
    {
        using var dir = new TempDir();

        Assert.Throws<ArgumentException>(
            () => OptimisticTransactionDb.Open(NewDbOptions(), dir.Path, []));
    }

    // ── Properties and checkpoints ──────────────────────────────────────────

    [Fact]
    public void Properties_AreReadable()
    {
        using var db = new Db();
        Write(db.Value, ("k", "v"));

        Assert.NotNull(db.Value.GetProperty("rocksdb.stats"));
        Assert.NotNull(db.Value.GetPropertyInt("rocksdb.num-entries-active-mem-table"));

        Assert.Null(db.Value.GetProperty("rocksdb.no.such.property"));
        Assert.Null(db.Value.GetPropertyInt("rocksdb.no.such.property"));
    }

    [Fact]
    public void CreateCheckpoint_ProducesAReadableCopy()
    {
        using var dir = new TempDir();
        using var target = new TempDir();

        string checkpointPath = Path.Combine(target.Path, "checkpoint");

        using (OptimisticTransactionDb db = OptimisticTransactionDb.Open(NewDbOptions(), dir.Path))
        {
            Write(db, ("k", "checkpointed"));

            using Checkpoint checkpoint = db.CreateCheckpoint();
            checkpoint.CreateCheckpoint(checkpointPath);
        }

        using var readOpts = new DbOptions();
        using RocksDb copy = RocksDb.Open(readOpts, checkpointPath);

        Assert.Equal("checkpointed", copy.GetString("k"));
    }

    // ── Lifetime ────────────────────────────────────────────────────────────

    /// <summary>
    /// A transaction released after its database gives an
    /// <see cref="ObjectDisposedException"/> rather than reading freed memory,
    /// the same guarantee <see cref="TransactionDb"/> makes.
    /// </summary>
    [Fact]
    public void ATransaction_OutlivingItsDatabase_DoesNotUseFreedMemory()
    {
        using var dir = new TempDir();

        OptimisticTransactionDb db = OptimisticTransactionDb.Open(NewDbOptions(), dir.Path);
        Transaction txn = db.BeginTransaction();
        txn.Put("k", "v");

        db.Dispose();

        Assert.Throws<ObjectDisposedException>(txn.Commit);
        txn.Dispose();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Applies entries directly, disposing the batch it builds.</summary>
    private static void Write(OptimisticTransactionDb db, params (string Key, string Value)[] entries)
    {
        using var batch = new WriteBatch();
        foreach ((string key, string value) in entries)
        {
            batch.Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value));
        }

        db.Write(batch);
    }

    /// <summary>
    /// Reads through a fresh transaction, since the database itself exposes no
    /// direct read path.
    /// </summary>
    private static string? Read(OptimisticTransactionDb db, string key)
    {
        using Transaction txn = db.BeginTransaction();
        return txn.GetString(key);
    }
}
