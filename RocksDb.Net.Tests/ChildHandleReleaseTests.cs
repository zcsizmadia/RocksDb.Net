namespace RocksDbNet.Tests;

/// <summary>
/// Handles that live inside another handle are released when it closes, and
/// abandoning one does not take the process down. See issue #118.
/// </summary>
/// <remarks>
/// <para>
/// The guard deciding whether a child releases its native handle used to ask
/// whether its parent <em>was disposed</em>, which is true from the moment the
/// parent's teardown begins. A parent disposing its own children from inside
/// that teardown therefore had every one of them decide the parent was already
/// closed and skip the release. Every column family handle leaked on every
/// close, along with any iterator still open on a transaction.
/// </para>
/// <para>
/// Correcting the guard alone turns that leak into an access violation, which is
/// why it was left alone once before. Measured on the stress below: correcting
/// the guard by itself crashed four runs out of four, in
/// <c>rocksdb_release_snapshot</c> on the finalizer thread. The parent now holds
/// its children reachable and releases them itself, so a child cannot be
/// finalized while its parent is closing, and the corrected guard is safe. Same
/// stress, same machine: four runs out of four clean.
/// </para>
/// </remarks>
public class ChildHandleReleaseTests
{
    /// <summary>
    /// Snapshots, iterators and column families abandoned to the finalizer while
    /// the database closes on this thread.
    /// </summary>
    /// <remarks>
    /// The collection is deliberately started before the close rather than after
    /// it, so the finalizer thread is working while the database tears down. That
    /// overlap is the whole point: it is what a correct-looking guard turned into
    /// a use-after-free.
    /// </remarks>
    [Fact]
    public void AbandonedChildren_DoNotCrashWhenTheDatabaseCloses()
    {
        for (int round = 0; round < 60; round++)
        {
            using var dir = new TempDir();

            var options = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

            RocksDb db = RocksDb.Open(options, dir.Path, [new("default"), new("events")]);

            db.Put("a", "1");
            db.Put("b", "2", db.GetColumnFamily("events"));

            Abandon(db);

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);

            db.Dispose();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    // A separate method so none of these stay reachable from a local.
    private static void Abandon(RocksDb db)
    {
        for (int i = 0; i < 8; i++)
        {
            Snapshot snapshot = db.NewSnapshot();

            Iterator iterator = db.NewIterator();
            iterator.SeekToFirst();

            Iterator cfIterator = db.NewIterator(db.GetColumnFamily("events"));
            cfIterator.SeekToFirst();

            _ = snapshot.Handle;
            _ = iterator.IsValid();
            _ = cfIterator.IsValid();
        }
    }

    /// <summary>The database itself left to the finalizer along with its children.</summary>
    [Fact]
    public void AbandonedDatabaseAndChildren_DoNotCrash()
    {
        for (int round = 0; round < 60; round++)
        {
            AbandonEverything();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private static void AbandonEverything()
    {
        var dir = new TempDir();

        var options = new DbOptions { CreateIfMissing = true };
        RocksDb db = RocksDb.Open(options, dir.Path);

        db.Put("a", "1");

        Snapshot snapshot = db.NewSnapshot();
        Iterator iterator = db.NewIterator();
        iterator.SeekToFirst();

        _ = snapshot.Handle;
        _ = iterator.IsValid();
    }

    /// <summary>
    /// A child disposed on its own drops out of its parent, so a long-lived
    /// database does not accumulate every iterator ever opened against it.
    /// </summary>
    /// <remarks>
    /// The parent holding its children is what makes the release safe, and it is
    /// also what could turn a database into a leak of a different kind. This
    /// opens and closes far more iterators than a list would tolerate holding.
    /// </remarks>
    [Fact]
    public void DisposedChildren_DoNotAccumulateOnTheParent()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");

        long before = GC.GetTotalMemory(forceFullCollection: true);

        for (int i = 0; i < 20_000; i++)
        {
            using Iterator iterator = db.Db.NewIterator();
            iterator.SeekToFirst();
        }

        long grew = GC.GetTotalMemory(forceFullCollection: true) - before;

        // Twenty thousand retained iterators would be megabytes. A few hundred
        // kilobytes of ordinary noise is not.
        Assert.True(
            grew < 4L * 1024 * 1024,
            $"managed memory grew by {grew / 1024} KB over 20,000 disposed iterators, so the parent kept them");
    }

    /// <summary>
    /// A handle whose constructor threw before the base constructor ran is still
    /// finalized, and must survive it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DbPath"/> used to validate its path inside the argument it
    /// passed to <c>base(...)</c>. Throwing from there leaves an allocated,
    /// finalizable object with every inherited field at its default, including
    /// the lock the parent uses to release its children. Taking that lock threw
    /// <see cref="ArgumentNullException"/> from a finalizer, which is not
    /// catchable and ends the process.
    /// </para>
    /// <para>
    /// Fixed twice over: <see cref="DbPath"/> validates in its body now, and the
    /// teardown tolerates a handle that was never constructed, since any future
    /// class could reintroduce the shape.
    /// </para>
    /// </remarks>
    [Fact]
    public void HandleThatFailedToConstruct_IsFinalizedSafely()
    {
        for (int i = 0; i < 200; i++)
        {
            Assert.Throws<ArgumentException>(() => new DbPath(string.Empty, 0));
            Assert.Throws<ArgumentNullException>(() => new DbPath(null!, 0));
        }

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
