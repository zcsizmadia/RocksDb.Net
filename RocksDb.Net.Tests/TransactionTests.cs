namespace RocksDbNet.Tests;

/// <summary>
/// Transactions with per-key locking and conflict detection. See issue #69.
/// </summary>
public class TransactionTests
{
    /// <summary>
    /// Opens a transaction database in a temporary directory. Lock timeouts are
    /// zero by default here so that contention fails immediately rather than
    /// making the tests wait.
    /// </summary>
    private sealed class TempTransactionDb : IDisposable
    {
        public TempTransactionDb(long lockTimeoutMs = 0)
        {
            Dir = new TempDir();

            var options = new DbOptions { CreateIfMissing = true };
            using var txnOptions = new TransactionDbOptions { TransactionLockTimeout = lockTimeoutMs };

            Db = TransactionDb.Open(options, txnOptions, Dir.Path);
        }

        public TempDir Dir { get; }

        public TransactionDb Db { get; }

        public void Dispose()
        {
            // The database owns and disposes the DbOptions.
            Db.Dispose();
            Dir.Dispose();
        }
    }

    // ── Basics ───────────────────────────────────────────────────────────────

    [Fact]
    public void Commit_MakesWritesVisible()
    {
        using var db = new TempTransactionDb();

        using (Transaction txn = db.Db.BeginTransaction())
        {
            txn.Put("key", "value");

            // Not visible outside the transaction yet.
            Assert.Null(db.Db.GetString("key"));

            // Visible inside it.
            Assert.Equal("value", txn.GetString("key"));

            txn.Commit();
        }

        Assert.Equal("value", db.Db.GetString("key"));
    }

    [Fact]
    public void Rollback_DiscardsWrites()
    {
        using var db = new TempTransactionDb();
        db.Db.Put("key", "original");

        using (Transaction txn = db.Db.BeginTransaction())
        {
            txn.Put("key", "replaced");
            Assert.Equal("replaced", txn.GetString("key"));

            txn.Rollback();
        }

        Assert.Equal("original", db.Db.GetString("key"));
    }

    /// <summary>
    /// Abandoning a transaction without committing must not apply its writes.
    /// </summary>
    [Fact]
    public void Dispose_WithoutCommit_DiscardsWrites()
    {
        using var db = new TempTransactionDb();

        using (Transaction txn = db.Db.BeginTransaction())
        {
            txn.Put("key", "never-committed");
        }

        Assert.Null(db.Db.GetString("key"));
    }

    [Fact]
    public void Delete_InTransaction_IsVisibleOnCommit()
    {
        using var db = new TempTransactionDb();
        db.Db.Put("key", "value");

        using Transaction txn = db.Db.BeginTransaction();
        txn.Delete("key");

        Assert.Null(txn.GetString("key"));
        Assert.Equal("value", db.Db.GetString("key"));

        txn.Commit();

        Assert.Null(db.Db.GetString("key"));
    }

    // ── Locking and conflicts ────────────────────────────────────────────────

    /// <summary>
    /// The whole point of a pessimistic transaction: a second writer touching a
    /// locked key fails rather than silently overwriting.
    /// </summary>
    [Fact]
    public void ConcurrentWriteToALockedKey_Fails()
    {
        using var db = new TempTransactionDb();

        using Transaction first = db.Db.BeginTransaction();
        first.Put("contested", "from-first");

        using Transaction second = db.Db.BeginTransaction();

        RocksDbException ex = Assert.Throws<RocksDbException>(() => second.Put("contested", "from-second"));
        Assert.Contains("imeout", ex.Message, StringComparison.Ordinal);

        // The first transaction is unaffected and can still commit.
        first.Commit();
        Assert.Equal("from-first", db.Db.GetString("contested"));
    }

    /// <summary>
    /// Two transactions touching different keys must not interfere.
    /// </summary>
    [Fact]
    public void ConcurrentWritesToDifferentKeys_BothSucceed()
    {
        using var db = new TempTransactionDb();

        using Transaction first = db.Db.BeginTransaction();
        using Transaction second = db.Db.BeginTransaction();

        first.Put("a", "1");
        second.Put("b", "2");

        first.Commit();
        second.Commit();

        Assert.Equal("1", db.Db.GetString("a"));
        Assert.Equal("2", db.Db.GetString("b"));
    }

    /// <summary>
    /// A locking read is the read half of a read-modify-write, so it must block
    /// another transaction from changing the value underneath.
    /// </summary>
    [Fact]
    public void GetForUpdate_LocksTheKeyAgainstOtherWriters()
    {
        using var db = new TempTransactionDb();
        db.Db.Put("counter", "1");

        using Transaction reader = db.Db.BeginTransaction();
        Assert.Equal("1", reader.GetStringForUpdate("counter"));

        using Transaction other = db.Db.BeginTransaction();
        Assert.Throws<RocksDbException>(() => other.Put("counter", "99"));

        reader.Put("counter", "2");
        reader.Commit();

        Assert.Equal("2", db.Db.GetString("counter"));
    }

    [Fact]
    public void GetForUpdate_MissingKey_ReturnsNullAndStillLocks()
    {
        using var db = new TempTransactionDb();

        using Transaction txn = db.Db.BeginTransaction();
        Assert.Null(txn.GetForUpdate("absent"u8));

        using Transaction other = db.Db.BeginTransaction();
        Assert.Throws<RocksDbException>(() => other.Put("absent", "value"));
    }

    /// <summary>
    /// Writing outside a transaction still takes locks, so it contends with one.
    /// </summary>
    [Fact]
    public void WriteOutsideATransaction_ContendsWithALockedKey()
    {
        using var db = new TempTransactionDb();

        using Transaction txn = db.Db.BeginTransaction();
        txn.Put("key", "from-transaction");

        Assert.Throws<RocksDbException>(() => db.Db.Put("key", "from-outside"));

        txn.Commit();
        Assert.Equal("from-transaction", db.Db.GetString("key"));
    }

    /// <summary>
    /// With a snapshot, a commit must fail when a key the transaction read has
    /// changed since it began. Without one, last writer wins.
    /// </summary>
    [Fact]
    public void SetSnapshot_MakesACommitFailWhenAReadKeyChanged()
    {
        using var db = new TempTransactionDb();
        db.Db.Put("key", "original");

        using var txnOptions = new TransactionOptions { SetSnapshot = true };
        using Transaction txn = db.Db.BeginTransaction(transactionOptions: txnOptions);

        // Read under the snapshot but take no lock.
        Assert.Equal("original", txn.GetString("key"));

        // Someone else commits a change to that key.
        using (Transaction other = db.Db.BeginTransaction())
        {
            other.Put("key", "changed");
            other.Commit();
        }

        // The snapshot-validated read of the same key now conflicts.
        Assert.Throws<RocksDbException>(() => txn.GetStringForUpdate("key"));
    }

    // ── Save points ──────────────────────────────────────────────────────────

    [Fact]
    public void RollbackToSavePoint_DiscardsOnlyWhatCameAfter()
    {
        using var db = new TempTransactionDb();

        using Transaction txn = db.Db.BeginTransaction();

        txn.Put("kept", "1");
        txn.SetSavePoint();
        txn.Put("discarded", "2");

        Assert.Equal("2", txn.GetString("discarded"));

        txn.RollbackToSavePoint();

        Assert.Equal("1", txn.GetString("kept"));
        Assert.Null(txn.GetString("discarded"));

        txn.Commit();

        Assert.Equal("1", db.Db.GetString("kept"));
        Assert.Null(db.Db.GetString("discarded"));
    }

    // ── Iteration ────────────────────────────────────────────────────────────

    /// <summary>
    /// An iterator over a transaction sees the database plus the transaction's
    /// own uncommitted writes, which is what distinguishes it from a database
    /// iterator.
    /// </summary>
    [Fact]
    public void NewIterator_SeesPendingWritesOverlaidOnTheDatabase()
    {
        using var db = new TempTransactionDb();
        db.Db.Put("a", "committed");
        db.Db.Put("c", "committed");

        using Transaction txn = db.Db.BeginTransaction();
        txn.Put("b", "pending");
        txn.Put("a", "overwritten");

        var seen = new List<(string Key, string Value)>();
        using (Iterator iter = txn.NewIterator())
        {
            for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
            {
                seen.Add((iter.KeyAsString()!, iter.ValueAsString()!));
            }
        }

        Assert.Equal(
            [("a", "overwritten"), ("b", "pending"), ("c", "committed")],
            seen);
    }

    /// <summary>
    /// Committing invalidates an open iterator. RocksDb does not stop a caller
    /// using it afterwards, so the wrapper disposes it and the caller gets an
    /// exception instead of reading freed memory.
    /// </summary>
    [Fact]
    public void Commit_DisposesOpenIterators()
    {
        using var db = new TempTransactionDb();

        using Transaction txn = db.Db.BeginTransaction();
        txn.Put("key", "value");

        Iterator iter = txn.NewIterator();
        iter.SeekToFirst();
        Assert.True(iter.IsValid());

        txn.Commit();

        Assert.True(iter.IsDisposed);
        Assert.Throws<ObjectDisposedException>(iter.ThrowIfDisposed);
    }

    [Fact]
    public void RollbackToSavePoint_DisposesOpenIterators()
    {
        using var db = new TempTransactionDb();

        using Transaction txn = db.Db.BeginTransaction();
        txn.Put("key", "value");
        txn.SetSavePoint();

        Iterator iter = txn.NewIterator();

        txn.RollbackToSavePoint();

        Assert.True(iter.IsDisposed);
    }

    // ── Column families ──────────────────────────────────────────────────────

    [Fact]
    public void Transaction_WorksAcrossColumnFamilies()
    {
        using var dir = new TempDir();
        var options = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using var txnOptions = new TransactionDbOptions();

        using var db = TransactionDb.Open(options, txnOptions, dir.Path, [new("default"), new("cf1")]);
        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");

        using (Transaction txn = db.BeginTransaction())
        {
            txn.Put("key", "in-default");
            txn.Put("key", "in-cf1", cf1);
            txn.Commit();
        }

        Assert.Equal("in-default", db.GetString("key"));
        Assert.Equal("in-cf1", db.GetString("key", cf1));
    }

    [Fact]
    public void CreateColumnFamily_IsRegisteredForLookup()
    {
        using var db = new TempTransactionDb();
        using var cfOptions = new DbOptions();

        ColumnFamilyHandle created = db.Db.CreateColumnFamily(cfOptions, "later");

        Assert.Same(created, db.Db.GetColumnFamily("later"));
        Assert.Contains("later", db.Db.ColumnFamilyNames);
        Assert.Throws<KeyNotFoundException>(() => db.Db.GetColumnFamily("nope"));
    }

    // ── Database-level operations ────────────────────────────────────────────

    [Fact]
    public void TransactionDb_SupportsPlainReadsWritesAndIteration()
    {
        using var db = new TempTransactionDb();

        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Delete("b");
        db.Db.Flush();

        Assert.Equal("1", db.Db.GetString("a"));
        Assert.Null(db.Db.GetString("b"));

        using (PinnableSlice? pinned = db.Db.GetPinned("a"u8))
        {
            Assert.NotNull(pinned);
            Assert.Equal("1", pinned.ToUtf8String());
        }

        var keys = new List<string>();
        using (Iterator iter = db.Db.NewIterator())
        {
            for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
            {
                keys.Add(iter.KeyAsString()!);
            }
        }

        Assert.Equal(["a"], keys);
    }

    [Fact]
    public void TransactionDb_Snapshot_GivesAConsistentView()
    {
        using var db = new TempTransactionDb();
        db.Db.Put("key", "before");

        using Snapshot snapshot = db.Db.NewSnapshot();
        using var readOptions = new ReadOptions();
        readOptions.SetSnapshot(snapshot);

        db.Db.Put("key", "after");

        Assert.Equal("before", db.Db.GetString("key", readOptions));
        Assert.Equal("after", db.Db.GetString("key"));
        Assert.True(snapshot.SequenceNumber > 0);
    }

    [Fact]
    public void TransactionDb_WriteBatch_IsAppliedAtomically()
    {
        using var db = new TempTransactionDb();

        using var batch = new WriteBatch();
        batch.Put("a"u8, "1"u8);
        batch.Put("b"u8, "2"u8);

        db.Db.Write(batch);

        Assert.Equal("1", db.Db.GetString("a"));
        Assert.Equal("2", db.Db.GetString("b"));
    }

    [Fact]
    public void TransactionDb_ExposesProperties()
    {
        using var db = new TempTransactionDb();
        db.Db.Put("key", "value");

        Assert.NotNull(db.Db.GetProperty("rocksdb.stats"));
        Assert.NotNull(db.Db.GetPropertyInt("rocksdb.estimate-num-keys"));
    }

    [Fact]
    public void TransactionDb_SurvivesReopen()
    {
        using var dir = new TempDir();

        {
            var options = new DbOptions { CreateIfMissing = true };
            using var txnOptions = new TransactionDbOptions();
            using var db = TransactionDb.Open(options, txnOptions, dir.Path);

            using Transaction txn = db.BeginTransaction();
            txn.Put("key", "value");
            txn.Commit();
        }

        var reopenOptions = new DbOptions { CreateIfMissing = true };
        using var reopenTxnOptions = new TransactionDbOptions();
        using var reopened = TransactionDb.Open(reopenOptions, reopenTxnOptions, dir.Path);

        Assert.Equal("value", reopened.GetString("key"));
    }

    // ── Lifetime ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A leaked transaction must not crash when it is finalized after the
    /// database is closed. RocksDb's destructor unlocks keys and unregisters
    /// the transaction through the database pointer.
    /// </summary>
    [Fact]
    public void LeakedTransaction_FinalizedAfterTheDatabaseIsClosed_DoesNotCrash()
    {
        using var dir = new TempDir();

        BeginAndAbandon(dir.Path);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        var options = new DbOptions { CreateIfMissing = true };
        using var txnOptions = new TransactionDbOptions();
        using var reopened = TransactionDb.Open(options, txnOptions, dir.Path);
        Assert.Equal("committed", reopened.GetString("key"));
    }

    // Separated so the transaction cannot stay alive in a local of the test.
    private static void BeginAndAbandon(string path)
    {
        var options = new DbOptions { CreateIfMissing = true };
        using var txnOptions = new TransactionDbOptions();
        using var db = TransactionDb.Open(options, txnOptions, path);

        db.Put("key", "committed");

        // Deliberately neither committed nor disposed.
        _ = db.BeginTransaction();
    }

    [Fact]
    public void Transaction_DisposedAfterTheDatabaseIsClosed_DoesNotCrash()
    {
        using var dir = new TempDir();
        var options = new DbOptions { CreateIfMissing = true };
        using var txnOptions = new TransactionDbOptions();

        Transaction txn;
        using (var db = TransactionDb.Open(options, txnOptions, dir.Path))
        {
            txn = db.BeginTransaction();
            txn.Put("key", "value");
        }

        txn.Dispose();
        Assert.True(txn.IsDisposed);
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public void Open_RejectsNullArguments()
    {
        using var dir = new TempDir();
        using var txnOptions = new TransactionDbOptions();

        Assert.Throws<ArgumentNullException>(() => TransactionDb.Open(null!, txnOptions, dir.Path));
        Assert.Throws<ArgumentNullException>(
            () => TransactionDb.Open(new DbOptions(), null!, dir.Path));
        Assert.Throws<ArgumentNullException>(
            () => TransactionDb.Open(new DbOptions(), txnOptions, null!));
        Assert.Throws<ArgumentException>(
            () => TransactionDb.Open(new DbOptions(), txnOptions, dir.Path, []));
    }

    [Fact]
    public void ColumnFamilyOverloads_RejectNullHandles()
    {
        using var db = new TempTransactionDb();
        using Transaction txn = db.Db.BeginTransaction();

        // Casts are required, not decoration. A bare null on the read overloads
        // binds to the ReadOptions parameter rather than the column family one,
        // so the call succeeds against the default family and asserts nothing.
        Assert.Throws<ArgumentNullException>(() => txn.Put("k", "v", (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => txn.Delete("k"u8, (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => txn.Get("k"u8, (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => txn.GetForUpdate("k"u8, (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => txn.NewIterator((ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.Put("k", "v", (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.GetPinned("k"u8, (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.NewIterator((ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.Flush((ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.Write(null!));
    }
}
