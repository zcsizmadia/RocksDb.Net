using System.Runtime.CompilerServices;

namespace RocksDbNet.Tests;

/// <summary>
/// Objects that something else still reads through are not released under it.
/// See issues #176 and #181.
/// </summary>
/// <remarks>
/// Each of these used to be a use-after-free: the native object was destroyed
/// while a column family, an iterator or a set of read options still pointed at
/// it. The fix defers the release until the reader lets go, which is what the
/// <c>IsDisposed</c> assertions pin down, and then performs it, so a disposal
/// asked for early is not lost either.
/// </remarks>
public class ReadThroughLifetimeTests
{
    private sealed class ReverseComparator : Comparator
    {
        public ReverseComparator()
            : base("lifetime.reverse")
        {
        }

        public override int Compare(ReadOnlySpan<byte> keyA, ReadOnlySpan<byte> keyB)
            => keyB.SequenceCompareTo(keyA);
    }

    private static void CollectEverything()
    {
        for (int i = 0; i < 2; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private static void WriteThree(Action<string, string> put)
    {
        put("b", "2");
        put("a", "1");
        put("c", "3");
    }

    // ── #176: column family options ──────────────────────────────────────────

    /// <summary>
    /// Disposing a column family's options after creating it is deferred until
    /// the database closes, so the comparator they own keeps working.
    /// </summary>
    [Fact]
    public void ColumnFamilyOptionsDisposedAfterCreate_KeepTheirComparatorAlive()
    {
        using var db = new TempDb();

        var cfOptions = new DbOptions();
        using (var comparator = new ReverseComparator())
        {
            cfOptions.Comparator = comparator;
        }

        ColumnFamilyHandle cf = db.Db.CreateColumnFamily(cfOptions, "reversed");

        cfOptions.Dispose();
        Assert.False(cfOptions.IsDisposed);

        CollectEverything();

        WriteThree((k, v) => db.Db.Put(k, v, cf));
        db.Db.Flush(cf);
        db.Db.CompactRange(cf);

        using (Iterator it = db.Db.NewIterator(cf))
        {
            it.SeekToFirst();
            Assert.Equal("c", it.KeyAsString());
        }

        // The deferred disposal happens once the database has closed.
        db.Dispose();
        Assert.True(cfOptions.IsDisposed);
    }

    /// <summary>
    /// Options the caller did not dispose are left alone when the database
    /// closes, so they can be handed to the next database.
    /// </summary>
    [Fact]
    public void ColumnFamilyOptionsNotDisposedByTheCaller_SurviveTheClose()
    {
        using var cfOptions = new DbOptions();

        using (var first = new TempDb())
        {
            first.Db.CreateColumnFamily(cfOptions, "x");
        }

        Assert.False(cfOptions.IsDisposed);

        using var second = new TempDb();
        ColumnFamilyHandle cf = second.Db.CreateColumnFamily(cfOptions, "x");
        second.Db.Put("k", "v", cf);
        Assert.Equal("v", second.Db.GetString("k", cf));
    }

    /// <summary>
    /// A transaction database whose descriptors are abandoned after open keeps
    /// the comparator their options own. It used to keep no reference to the
    /// descriptors at all, so their finalizers destroyed it.
    /// </summary>
    [Fact]
    public void TransactionDb_AbandonedDescriptors_KeepTheirComparatorAlive()
    {
        using var dir = new TempDir();
        using var dbOptions = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using var txnOptions = new TransactionDbOptions();

        using TransactionDb db = OpenWithAbandonedDescriptors(dbOptions, txnOptions, dir.Path);

        CollectEverything();

        ColumnFamilyHandle cf = db.GetColumnFamily("reversed");
        WriteThree((k, v) => db.Put(k, v, cf));
        db.Flush();

        using Iterator it = db.NewIterator(cf);
        it.SeekToFirst();
        Assert.Equal("c", it.KeyAsString());
    }

    // Not inlined, so the descriptors and the comparator are unreachable from
    // the test method once this returns.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TransactionDb OpenWithAbandonedDescriptors(
        DbOptions dbOptions, TransactionDbOptions txnOptions, string path)
    {
        var reversed = new ColumnFamilyDescriptor("reversed");
        reversed.Options.Comparator = new ReverseComparator();

        return TransactionDb.Open(dbOptions, txnOptions, path,
            [new ColumnFamilyDescriptor("default"), reversed]);
    }

    // ── #181: what an iterator reads through ─────────────────────────────────

    /// <summary>
    /// Disposing the read options an iterator was created with is deferred
    /// until the iterator is disposed. The iterate bound lives inside the
    /// native options struct, so freeing it early left the iterator reading a
    /// bound out of freed memory.
    /// </summary>
    [Fact]
    public void ReadOptionsDisposedUnderALiveIterator_AreDeferred()
    {
        using var db = new TempDb();
        foreach (string key in new[] { "a", "b", "c", "d" })
        {
            db.Db.Put(key, key);
        }

        var options = new ReadOptions().SetIterateUpperBound("c"u8);
        Iterator it = db.Db.NewIterator(options);

        options.Dispose();
        Assert.False(options.IsDisposed);

        CollectEverything();

        var keys = new List<string>();
        for (it.SeekToFirst(); it.IsValid(); it.Next())
        {
            keys.Add(it.KeyAsString());
        }

        Assert.Equal(new[] { "a", "b" }, keys);

        it.Dispose();
        Assert.True(options.IsDisposed);
    }

    /// <summary>
    /// Read options nobody disposed are not disposed by the iterator either.
    /// </summary>
    [Fact]
    public void ReadOptionsStillOwnedByTheCaller_SurviveTheIterator()
    {
        using var db = new TempDb();
        using var options = new ReadOptions();

        using (Iterator it = db.Db.NewIterator(options))
        {
            it.SeekToFirst();
        }

        Assert.False(options.IsDisposed);
        using Iterator again = db.Db.NewIterator(options);
    }

    /// <summary>
    /// Disposing an indexed write batch under an overlay iterator is deferred
    /// until the iterator is disposed.
    /// </summary>
    [Fact]
    public void WriteBatchDisposedUnderAnOverlayIterator_IsDeferred()
    {
        using var db = new TempDb();
        db.Db.Put("a", "db");

        var batch = new WriteBatchWithIndex();
        batch.Put("b", "batch");

        Iterator it = batch.NewIteratorWithBase(db.Db);

        batch.Dispose();
        Assert.False(batch.IsDisposed);

        var keys = new List<string>();
        for (it.SeekToFirst(); it.IsValid(); it.Next())
        {
            keys.Add(it.KeyAsString());
        }

        Assert.Equal(new[] { "a", "b" }, keys);

        it.Dispose();
        Assert.True(batch.IsDisposed);
    }

    // ── #181: a snapshot attached to read options ─────────────────────────────

    /// <summary>
    /// Disposing a snapshot that read options are attached to is deferred
    /// until it is detached, so reads through the options still see it.
    /// </summary>
    [Fact]
    public void SnapshotDisposedWhileAttached_IsDeferredUntilDetached()
    {
        using var db = new TempDb();
        db.Db.Put("k", "old");

        using var options = new ReadOptions();
        Snapshot snapshot = db.Db.NewSnapshot();
        options.SetSnapshot(snapshot);

        snapshot.Dispose();
        Assert.False(snapshot.IsDisposed);

        db.Db.Put("k", "new");
        Assert.Equal("old", db.Db.GetString("k", options));

        options.SetSnapshot(null);
        Assert.True(snapshot.IsDisposed);
        Assert.Equal("new", db.Db.GetString("k", options));
    }

    /// <summary>
    /// Disposing the options releases a snapshot whose disposal was deferred.
    /// </summary>
    [Fact]
    public void DisposingTheOptions_ReleasesADeferredSnapshot()
    {
        using var db = new TempDb();

        var options = new ReadOptions();
        Snapshot snapshot = db.Db.NewSnapshot();
        options.SetSnapshot(snapshot);

        snapshot.Dispose();
        Assert.False(snapshot.IsDisposed);

        options.Dispose();
        Assert.True(snapshot.IsDisposed);
    }

    /// <summary>
    /// Replacing the attached snapshot lets go of the previous one, but does
    /// not dispose a snapshot the caller still owns.
    /// </summary>
    [Fact]
    public void ReplacingTheSnapshot_DoesNotDisposeOneTheCallerStillOwns()
    {
        using var db = new TempDb();
        using var options = new ReadOptions();
        using Snapshot first = db.Db.NewSnapshot();
        using Snapshot second = db.Db.NewSnapshot();

        options.SetSnapshot(first);
        options.SetSnapshot(second);

        Assert.False(first.IsDisposed);

        // Still usable: attaching it again takes a fresh keep-alive, which a
        // released snapshot would refuse.
        options.SetSnapshot(first);
    }

    /// <summary>
    /// Closing the database releases its snapshots even while read options
    /// still hold one. A held snapshot cannot outlive the database it belongs
    /// to: RocksDb closes regardless, and releasing it afterwards would reach
    /// into the closed database.
    /// </summary>
    [Fact]
    public void ClosingTheDatabase_ReleasesASnapshotStillAttachedToOptions()
    {
        var db = new TempDb();

        using var options = new ReadOptions();
        Snapshot snapshot = db.Db.NewSnapshot();
        options.SetSnapshot(snapshot);

        db.Dispose();
        Assert.True(snapshot.IsDisposed);

        // Letting go afterwards is harmless.
        options.SetSnapshot(null);
    }
}
