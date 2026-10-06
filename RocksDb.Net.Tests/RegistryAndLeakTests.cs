namespace RocksDbNet.Tests;

/// <summary>
/// Handles that stayed reachable after disposal, and the column family
/// registry under concurrent use. See issues #177, #179 and #183.
/// </summary>
public class RegistryAndLeakTests
{
    // ── #177: snapshots registered twice ─────────────────────────────────────

    /// <summary>
    /// A disposed snapshot is no longer referenced by its database. Each one
    /// used to be registered twice, by its constructor and by NewSnapshot, and
    /// disposal removed only one of the two entries.
    /// </summary>
    [Fact]
    public void DisposedSnapshots_AreReleasedByTheDatabase()
    {
        using var db = new TempDb();
        db.Db.Put("k", "v");

        int before = db.Db.ChildCount;

        for (int i = 0; i < 100; i++)
        {
            using Snapshot snapshot = db.Db.NewSnapshot();
            Assert.Equal(before + 1, db.Db.ChildCount);
        }

        Assert.Equal(before, db.Db.ChildCount);
    }

    [Fact]
    public void DisposedSnapshots_AreReleasedByATransactionDatabase()
    {
        using var dir = new TempDir();
        using var dbOpts = new DbOptions { CreateIfMissing = true };
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        int before = db.ChildCount;

        for (int i = 0; i < 100; i++)
        {
            using Snapshot snapshot = db.NewSnapshot();
        }

        Assert.Equal(before, db.ChildCount);
    }

    // ── #179: the registry under concurrent use ──────────────────────────────

    /// <summary>
    /// Families created on one thread while others look them up and list them.
    /// The registry was a bare dictionary, written by one and read by the
    /// others with no lock.
    /// </summary>
    [Fact]
    public async Task ColumnFamilyRegistry_ToleratesConcurrentCreateAndLookup()
    {
        using var db = new TempDb();
        using var cfOptions = new DbOptions();

        const int Families = 200;
        using var done = new CancellationTokenSource();
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        Task[] readers = Enumerable.Range(0, 4).Select(r => Task.Run(() =>
        {
            try
            {
                while (!done.IsCancellationRequested)
                {
                    foreach (string name in db.Db.ColumnFamilyNames)
                    {
                        Assert.True(db.Db.TryGetColumnFamily(name, out _), $"{name} is listed but cannot be resolved");
                    }

                    db.Db.TryGetColumnFamily($"cf{r}", out _);
                    Assert.NotNull(db.Db.GetDefaultColumnFamily());
                }
            }
            catch (Exception e)
            {
                failures.Enqueue(e);
            }
        })).ToArray();

        try
        {
            for (int i = 0; i < Families; i++)
            {
                db.Db.CreateColumnFamily(cfOptions, $"cf{i}");
            }
        }
        finally
        {
            done.Cancel();
            await Task.WhenAll(readers);
        }

        Assert.Empty(failures);
        Assert.Equal(Families + 1, db.Db.ColumnFamilyNames.Count);
    }

    /// <summary>
    /// Concurrent first requests for the default family share one handle,
    /// rather than each creating and caching its own.
    /// </summary>
    [Fact]
    public void DefaultColumnFamily_IsResolvedOnceUnderConcurrentFirstUse()
    {
        using var db = new TempDb();

        ColumnFamilyHandle[] seen = new ColumnFamilyHandle[8];
        using var start = new Barrier(seen.Length);

        Parallel.For(0, seen.Length, new ParallelOptions { MaxDegreeOfParallelism = seen.Length }, i =>
        {
            start.SignalAndWait();
            seen[i] = db.Db.GetDefaultColumnFamily();
        });

        Assert.All(seen, cf => Assert.Same(seen[0], cf));
    }

    // ── #183: disposed handles in the registry ───────────────────────────────

    /// <summary>
    /// The default family can always be resolved again, so a stray dispose of
    /// the cached handle no longer breaks every later lookup.
    /// </summary>
    [Fact]
    public void DisposedDefaultColumnFamily_IsResolvedAgain()
    {
        using var db = new TempDb();

        ColumnFamilyHandle first = db.Db.GetDefaultColumnFamily();
        first.Dispose();

        ColumnFamilyHandle second = db.Db.GetDefaultColumnFamily();
        Assert.NotSame(first, second);
        Assert.False(second.IsDisposed);

        db.Db.Put("k", "v", second);
        Assert.Equal("v", db.Db.GetString("k", second));

        Assert.Same(second, db.Db.GetColumnFamily("default"));
    }

    [Fact]
    public void DisposedDefaultColumnFamily_IsResolvedAgainOnATransactionDatabase()
    {
        using var dir = new TempDir();
        using var dbOpts = new DbOptions { CreateIfMissing = true };
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        ColumnFamilyHandle first = db.GetDefaultColumnFamily();
        first.Dispose();

        ColumnFamilyHandle second = db.GetDefaultColumnFamily();
        Assert.False(second.IsDisposed);
        Assert.Equal("default", second.Name);
    }

    /// <summary>
    /// A named family cannot be reopened by name, so a disposed one is
    /// reported where it is looked up, naming the family, rather than handed
    /// back to fail on its next use.
    /// </summary>
    [Fact]
    public void DisposedNamedColumnFamily_IsReportedAtLookup()
    {
        using var db = new TempDb();
        using var cfOptions = new DbOptions();

        db.Db.CreateColumnFamily(cfOptions, "orders").Dispose();

        var e = Assert.Throws<ObjectDisposedException>(() => db.Db.GetColumnFamily("orders"));
        Assert.Contains("'orders'", e.Message);

        // Still listed: the family exists, only its handle is gone.
        Assert.Contains("orders", db.Db.ColumnFamilyNames);
    }

    // ── #183: a transaction's iterator list ──────────────────────────────────

    /// <summary>
    /// Iterators opened and disposed inside one long transaction do not
    /// accumulate. The list used to be cleared only by Commit and Rollback.
    /// </summary>
    [Fact]
    public void Transaction_DoesNotAccumulateDisposedIterators()
    {
        using var dir = new TempDir();
        using var dbOpts = new DbOptions { CreateIfMissing = true };
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);
        using Transaction txn = db.BeginTransaction();

        txn.Put("k", "v");

        for (int i = 0; i < 1_000; i++)
        {
            using Iterator it = txn.NewIterator();
            it.SeekToFirst();
            Assert.True(it.IsValid());
        }

        Assert.True(txn.TrackedIteratorCount <= 32,
            $"{txn.TrackedIteratorCount} iterators tracked after all were disposed");

        // Live ones are still tracked, and still closed by Commit.
        Iterator open = txn.NewIterator();
        txn.Commit();
        Assert.True(open.IsDisposed);
    }
}
