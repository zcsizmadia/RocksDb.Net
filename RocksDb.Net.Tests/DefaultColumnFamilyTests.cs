namespace RocksDbNet.Tests;

/// <summary>
/// The default column family is resolvable on every database type, whether or
/// not it was named at open. See issue #165.
/// </summary>
/// <remarks>
/// The defect these cover is a listing that disagrees with a lookup:
/// <c>ColumnFamilyNames</c> reported <c>default</c> and asking for it threw
/// <see cref="KeyNotFoundException"/>, with a message that listed it among the
/// known families. <see cref="RocksDb"/> had been fixed; the two transaction
/// databases had not.
/// </remarks>
public class DefaultColumnFamilyTests
{
    private static DbOptions NewDbOptions() => new() { CreateIfMissing = true };

    // ── TransactionDb ───────────────────────────────────────────────────────

    [Fact]
    public void TransactionDb_ResolvesTheDefaultFamilyItLists()
    {
        using var dir = new TempDir();
        using var dbOpts = NewDbOptions();
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        Assert.Contains("default", db.ColumnFamilyNames);

        ColumnFamilyHandle cf = db.GetDefaultColumnFamily();
        Assert.Equal("default", cf.Name);

        // Everything the listing claims is now reachable, which is the property
        // that was broken.
        foreach (string name in db.ColumnFamilyNames)
        {
            Assert.True(db.TryGetColumnFamily(name, out _), $"{name} is listed but cannot be resolved");
        }
    }

    /// <summary>
    /// Still true once another family exists, which is the shape the defect was
    /// found in: the dictionary was non-empty, so the default fell out.
    /// </summary>
    [Fact]
    public void TransactionDb_ResolvesTheDefaultFamilyAlongsideOthers()
    {
        using var dir = new TempDir();
        using var dbOpts = NewDbOptions();
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        using var cfOpts = new DbOptions();
        using ColumnFamilyHandle other = db.CreateColumnFamily(cfOpts, "other");

        Assert.Equal(["default", "other"], db.ColumnFamilyNames.Order());

        ColumnFamilyHandle def = db.GetColumnFamily("default");
        Assert.Equal("default", def.Name);
    }

    /// <summary>
    /// The handle works, rather than merely being returned: a write through it
    /// lands in the default family and is visible without one.
    /// </summary>
    [Fact]
    public void TransactionDb_TheDefaultFamilyHandleReadsAndWrites()
    {
        using var dir = new TempDir();
        using var dbOpts = NewDbOptions();
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        ColumnFamilyHandle def = db.GetDefaultColumnFamily();

        db.Put("k", "through-handle", def);

        Assert.Equal("through-handle", db.GetString("k"));
        Assert.Equal("through-handle", db.GetString("k", def));
    }

    /// <summary>
    /// Cached, so repeated calls do not leak a wrapper struct each time.
    /// </summary>
    [Fact]
    public void TransactionDb_TheDefaultFamilyHandleIsCached()
    {
        using var dir = new TempDir();
        using var dbOpts = NewDbOptions();
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        ColumnFamilyHandle first = db.GetDefaultColumnFamily();

        for (int i = 0; i < 50; i++)
        {
            Assert.Same(first, db.GetDefaultColumnFamily());
        }

        Assert.Same(first, db.GetColumnFamily("default"));
    }

    /// <summary>
    /// Reaching the base database to find the handle must not close it. The
    /// wrapper taken there is released with <c>close_base_db</c>, which frees
    /// only that wrapper — if it closed the real database, everything after
    /// this would fail.
    /// </summary>
    [Fact]
    public void TransactionDb_StaysUsableAfterResolvingTheDefaultFamily()
    {
        using var dir = new TempDir();
        using var dbOpts = NewDbOptions();
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        db.Put("before", "1");

        // Several times, so a wrapper freed once per call is exercised.
        for (int i = 0; i < 10; i++)
        {
            db.GetDefaultColumnFamily();
        }

        db.Put("after", "2");
        Assert.Equal("1", db.GetString("before"));
        Assert.Equal("2", db.GetString("after"));

        using (Transaction txn = db.BeginTransaction())
        {
            txn.Put("in-txn", "3");
            txn.Commit();
        }

        Assert.Equal("3", db.GetString("in-txn"));

        db.Flush();
        Assert.Equal("1", db.GetString("before"));
    }

    // ── OptimisticTransactionDb ─────────────────────────────────────────────

    [Fact]
    public void OptimisticTransactionDb_ResolvesTheDefaultFamilyItLists()
    {
        using var dir = new TempDir();
        using OptimisticTransactionDb db = OptimisticTransactionDb.Open(NewDbOptions(), dir.Path);

        Assert.Contains("default", db.ColumnFamilyNames);

        ColumnFamilyHandle cf = db.GetDefaultColumnFamily();
        Assert.Equal("default", cf.Name);

        foreach (string name in db.ColumnFamilyNames)
        {
            Assert.True(db.TryGetColumnFamily(name, out _), $"{name} is listed but cannot be resolved");
        }
    }

    [Fact]
    public void OptimisticTransactionDb_TheDefaultFamilyHandleReadsAndWrites()
    {
        using var dir = new TempDir();
        using OptimisticTransactionDb db = OptimisticTransactionDb.Open(NewDbOptions(), dir.Path);

        ColumnFamilyHandle def = db.GetDefaultColumnFamily();

        using (Transaction txn = db.BeginTransaction())
        {
            txn.Put("k", "through-handle", def);
            txn.Commit();
        }

        using Transaction reader = db.BeginTransaction();
        Assert.Equal("through-handle", reader.GetString("k"));
        Assert.Equal("through-handle", reader.GetString("k", def));
    }

    [Fact]
    public void OptimisticTransactionDb_TheDefaultFamilyHandleIsCached()
    {
        using var dir = new TempDir();
        using OptimisticTransactionDb db = OptimisticTransactionDb.Open(NewDbOptions(), dir.Path);

        ColumnFamilyHandle first = db.GetDefaultColumnFamily();

        for (int i = 0; i < 50; i++)
        {
            Assert.Same(first, db.GetDefaultColumnFamily());
        }

        Assert.Same(first, db.GetColumnFamily("default"));
    }

    /// <inheritdoc cref="TransactionDb_StaysUsableAfterResolvingTheDefaultFamily"/>
    [Fact]
    public void OptimisticTransactionDb_StaysUsableAfterResolvingTheDefaultFamily()
    {
        using var dir = new TempDir();
        using OptimisticTransactionDb db = OptimisticTransactionDb.Open(NewDbOptions(), dir.Path);

        for (int i = 0; i < 10; i++)
        {
            db.GetDefaultColumnFamily();
        }

        using (Transaction txn = db.BeginTransaction())
        {
            txn.Put("k", "v");
            txn.Commit();
        }

        using Transaction reader = db.BeginTransaction();
        Assert.Equal("v", reader.GetString("k"));

        Assert.NotNull(db.GetProperty("rocksdb.stats"));
    }

    // ── The shared lock buckets, verified rather than asserted ──────────────

    /// <summary>
    /// <see cref="OccLockBuckets"/> may be disposed while the databases sharing
    /// it are still open and working.
    /// </summary>
    /// <remarks>
    /// RocksDb copies the <c>shared_ptr</c>, so destroying this library's
    /// handle drops only its own reference. That is the same rule the block
    /// cache follows, and it is asserted here rather than only documented
    /// because issue #111 proposed hold-counting these instead — which would be
    /// the right answer if the claim were false.
    /// </remarks>
    [Fact]
    public void SharedLockBuckets_MayBeDisposedUnderLiveDatabases()
    {
        using var firstDir = new TempDir();
        using var secondDir = new TempDir();

        using var otxnOpts = new OptimisticTransactionDbOptions();

        using OptimisticTransactionDb a = OpenSharing(otxnOpts, firstDir.Path, out OccLockBuckets buckets);
        using OptimisticTransactionDb b = OptimisticTransactionDb.Open(NewDbOptions(), otxnOpts, secondDir.Path);

        // The caller's reference goes while both databases are open and in use.
        buckets.Dispose();

        for (int i = 0; i < 100; i++)
        {
            using Transaction txn = a.BeginTransaction();
            txn.Put($"k{i}", $"a{i}");
            txn.Commit();

            using Transaction other = b.BeginTransaction();
            other.Put($"k{i}", $"b{i}");
            other.Commit();
        }

        using Transaction readA = a.BeginTransaction();
        using Transaction readB = b.BeginTransaction();

        Assert.Equal("a99", readA.GetString("k99"));
        Assert.Equal("b99", readB.GetString("k99"));

        // And conflict detection still works, which is what the buckets are for.
        using Transaction first = a.BeginTransaction();
        using Transaction second = a.BeginTransaction();
        first.Put("contended", "1");
        second.Put("contended", "2");
        first.Commit();
        Assert.Throws<RocksDbException>(second.Commit);
    }

    private static OptimisticTransactionDb OpenSharing(
        OptimisticTransactionDbOptions options, string path, out OccLockBuckets buckets)
    {
        buckets = new OccLockBuckets(32);
        options.SetSharedLockBuckets(buckets);
        return OptimisticTransactionDb.Open(NewDbOptions(), options, path);
    }
}
