namespace RocksDbNet.Tests;

/// <summary>
/// Covers the lifetime rules that RocksDb imposes on objects created from a
/// database, and that the wrapper used to enforce by crashing. See issues #58
/// and #64.
/// </summary>
/// <remarks>
/// Every one of these describes something a caller does by accident: not
/// keeping options in a variable, or forgetting to dispose a child before the
/// database. None of them should terminate the process, and the crashes
/// happened on the finalizer thread where nothing can catch them.
/// </remarks>
public class LifetimeTests
{
    /// <summary>
    /// An iterator built from a throwaway <see cref="ReadOptions"/> must keep
    /// working.
    /// </summary>
    /// <remarks>
    /// RocksDb stores an iterate bound as a pointer into the options struct and
    /// copies the options by value when creating the iterator, so a live
    /// iterator dereferences that address on every step. Written this way the
    /// options are unreachable the moment the call returns, so a collection
    /// would leave the iterator reading freed memory. Forcing collections here
    /// is the point.
    /// </remarks>
    [Fact]
    public void Iterator_KeepsThrowawayReadOptionsAlive()
    {
        using var db = new TempDb();

        for (int i = 0; i < 200; i++)
        {
            db.Db.Put($"key{i:D4}", $"value{i}");
        }

        // No variable holds these options.
        using Iterator iter = db.Db.NewIterator(
            new ReadOptions().SetIterateUpperBound("key0100"u8.ToArray()));

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        var seen = new List<string>();
        for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
        {
            seen.Add(iter.KeyAsString()!);
        }

        // The bound was honoured, which means it was still readable.
        Assert.NotEmpty(seen);
        Assert.Equal("key0000", seen[0]);
        Assert.DoesNotContain("key0100", seen);
        Assert.Equal(100, seen.Count);
    }

    /// <summary>
    /// Leaking a child and then disposing the database must not crash when the
    /// child is finalized.
    /// </summary>
    /// <remarks>
    /// This is the case that used to terminate the process.
    /// <c>rocksdb_release_snapshot</c> dereferences the database pointer with no
    /// null check, and an iterator's and column family handle's destructors
    /// reach into database internals that closing has already freed.
    /// </remarks>
    [Theory]
    [InlineData("snapshot")]
    [InlineData("iterator")]
    [InlineData("columnfamily")]
    public void LeakedChild_FinalizedAfterTheDatabaseIsClosed_DoesNotCrash(string child)
    {
        using var dir = new TempDir();

        CreateAndAbandon(dir.Path, child);

        // The child is unreachable and the database is closed. Finalizing it now
        // must not touch the freed database.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        // Reaching here at all is the assertion. Reopening confirms the database
        // was closed cleanly rather than left corrupt.
        using var opts = new DbOptions { CreateIfMissing = true };

        // RocksDb requires every existing column family to be named at open, so
        // the abandoned one has to be listed.
        var families = new List<ColumnFamilyDescriptor> { new("default") };
        if (child == "columnfamily")
        {
            families.Add(new ColumnFamilyDescriptor("abandoned"));
        }

        using var reopened = RocksDb.Open(opts, dir.Path, families);
        Assert.Equal("value", reopened.GetString("key"));
    }

    // Separated so the child cannot stay alive in a local of the test method.
    private static void CreateAndAbandon(string path, string child)
    {
        using var opts = new DbOptions { CreateIfMissing = true };
        using var db = RocksDb.Open(opts, path);

        db.Put("key", "value");

        // Deliberately not disposed, and deliberately not returned.
        switch (child)
        {
            case "snapshot":
                _ = db.NewSnapshot();
                break;
            case "iterator":
                _ = db.NewIterator();
                break;
            case "columnfamily":
                using (var cfOpts = new DbOptions())
                {
                    _ = db.CreateColumnFamily(cfOpts, "abandoned");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(child), child, "unknown child kind");
        }
    }

    /// <summary>
    /// Disposing a child explicitly after the database is closed must also be
    /// safe, since that is the same hazard without the GC involved.
    /// </summary>
    [Fact]
    public void Child_DisposedAfterTheDatabaseIsClosed_DoesNotCrash()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };

        Snapshot snapshot;
        Iterator iterator;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("key", "value");
            snapshot = db.NewSnapshot();
            iterator = db.NewIterator();
        }

        // Out of order on purpose.
        snapshot.Dispose();
        iterator.Dispose();

        Assert.True(snapshot.IsDisposed);
        Assert.True(iterator.IsDisposed);
    }

    /// <summary>
    /// A column family created after open must be findable by name, like the
    /// ones passed to <see cref="RocksDb.Open(DbOptions, string, System.Collections.Generic.IReadOnlyList{ColumnFamilyDescriptor})"/>.
    /// </summary>
    [Fact]
    public void CreateColumnFamily_RegistersTheHandleForLookup()
    {
        using var db = new TempDb();
        using var cfOpts = new DbOptions();

        ColumnFamilyHandle created = db.Db.CreateColumnFamily(cfOpts, "later");

        Assert.Same(created, db.Db.GetColumnFamily("later"));
        Assert.True(db.Db.TryGetColumnFamily("later", out ColumnFamilyHandle? found));
        Assert.Same(created, found);
        Assert.Contains("later", db.Db.ColumnFamilyNames);

        db.Db.Put("key", "value", created);
        Assert.Equal("value", db.Db.GetString("key", db.Db.GetColumnFamily("later")));
    }

    /// <summary>
    /// An unknown name used to come back as null from a non-nullable signature,
    /// so the mistake surfaced as a NullReferenceException elsewhere.
    /// </summary>
    [Fact]
    public void GetColumnFamily_UnknownName_ThrowsNamingWhatIsKnown()
    {
        using var db = new TempDb();

        KeyNotFoundException ex = Assert.Throws<KeyNotFoundException>(() => db.Db.GetColumnFamily("nope"));

        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
        Assert.Contains("default", ex.Message, StringComparison.Ordinal);

        Assert.False(db.Db.TryGetColumnFamily("nope", out ColumnFamilyHandle? missing));
        Assert.Null(missing);
    }

    /// <summary>
    /// The default column family wrapper is handed out from a cache, because
    /// each native call allocates a fresh non-owning wrapper that nothing frees.
    /// </summary>
    [Fact]
    public void GetDefaultColumnFamily_ReturnsTheSameInstanceEachTime()
    {
        using var db = new TempDb();

        ColumnFamilyHandle first = db.Db.GetDefaultColumnFamily();
        ColumnFamilyHandle second = db.Db.GetDefaultColumnFamily();

        Assert.Same(first, second);
        Assert.False(first.Owned);
    }
}
