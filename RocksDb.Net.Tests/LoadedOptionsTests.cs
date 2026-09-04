namespace RocksDbNet.Tests;

/// <summary>
/// Reading back the options a database was last opened with.
/// </summary>
/// <remarks>
/// On disk throughout, because the whole feature is about the <c>OPTIONS-</c>
/// file RocksDb writes into the database directory. An in-memory environment
/// has nowhere to write it.
/// </remarks>
public class LoadedOptionsTests
{
    /// <summary>
    /// Non-default settings survive a close and come back, each from the half
    /// of the file that carries it.
    /// </summary>
    /// <remarks>
    /// The split is easy to get wrong, and this asserts it rather than assuming
    /// it: RocksDb builds the database options from the file's DBOptions plus a
    /// <em>default</em> set of column family options, so a column-family setting
    /// read from <see cref="LoadedOptions.DatabaseOptions"/> is the default and
    /// not what the database was using. <c>MaxOpenFiles</c> is database-wide;
    /// <c>WriteBufferSize</c> is per family.
    /// </remarks>
    [Fact]
    public void LoadLatest_ReturnsTheOptionsTheDatabaseWasOpenedWith()
    {
        using var dir = new TempDir();

        using (var options = new DbOptions
        {
            CreateIfMissing = true,
            MaxOpenFiles = 123,
            WriteBufferSize = 2 * 1024 * 1024,
        })
        {
            using RocksDb db = RocksDb.Open(options, dir.Path);
            db.Put("k", "v");
        }

        using LoadedOptions loaded = LoadedOptions.LoadLatest(dir.Path);

        Assert.Equal(123, loaded.DatabaseOptions.MaxOpenFiles);

        // Per family, not database-wide. Asserting the default is absent from
        // the database options as well, because that is the mistake the
        // documentation warns about and a test that only checked the right
        // place would not notice if the wrong one started working.
        Assert.Equal(2UL * 1024 * 1024, loaded.ColumnFamilyOptions(0).WriteBufferSize);
        Assert.NotEqual(2UL * 1024 * 1024, loaded.DatabaseOptions.WriteBufferSize);
    }

    /// <summary>Every column family is reported, in order, with its own options.</summary>
    [Fact]
    public void LoadLatest_ReportsEveryColumnFamily()
    {
        using var dir = new TempDir();

        using (var options = new DbOptions
        {
            CreateIfMissing = true,
            CreateMissingColumnFamilies = true,
        })
        {
            using RocksDb db = RocksDb.Open(
                options, dir.Path, [new("default"), new("orders"), new("invoices")]);

            db.Put("k", "v");
        }

        using LoadedOptions loaded = LoadedOptions.LoadLatest(dir.Path);

        Assert.Equal(["default", "orders", "invoices"], loaded.ColumnFamilyNames);

        // Each family's options are readable, which is the half that would be
        // useless if only the names came back.
        for (int i = 0; i < loaded.ColumnFamilyNames.Count; i++)
        {
            Assert.True(loaded.ColumnFamilyOptions(i).WriteBufferSize > 0);
        }
    }

    /// <summary>
    /// The options this hands out are borrowed, and saying so is enforced
    /// rather than documented.
    /// </summary>
    /// <remarks>
    /// RocksDb allocates the database options, the names and the per-family
    /// options in one call and takes them back in one call, so none of them may
    /// be freed individually. Disposing the owner marks every wrapper unusable
    /// without freeing it, which turns a use-after-free into an exception —
    /// this is the assertion that keeps that true.
    /// </remarks>
    [Fact]
    public void UsingTheOptionsAfterDisposingTheOwnerThrows()
    {
        using var dir = new TempDir();

        using (var options = new DbOptions { CreateIfMissing = true })
        {
            using RocksDb db = RocksDb.Open(options, dir.Path);
            db.Put("k", "v");
        }

        LoadedOptions loaded = LoadedOptions.LoadLatest(dir.Path);
        DbOptions borrowed = loaded.DatabaseOptions;

        loaded.Dispose();

        Assert.Throws<ObjectDisposedException>(() => borrowed.MaxOpenFiles);
        Assert.Throws<ObjectDisposedException>(() => loaded.ColumnFamilyOptions(0));

        // Idempotent, so a using block around one that was disposed early does
        // not free the same block twice.
        loaded.Dispose();
    }

    /// <summary>A directory with no database fails rather than returning empty.</summary>
    [Fact]
    public void LoadLatest_WithoutAnOptionsFile_Throws()
    {
        using var dir = new TempDir();

        Assert.Throws<RocksDbException>(() => LoadedOptions.LoadLatest(dir.Sub("empty")));
    }

    [Fact]
    public void LoadLatest_RejectsAMissingPath()
    {
        Assert.Throws<ArgumentNullException>(() => LoadedOptions.LoadLatest(null!));
        Assert.Throws<ArgumentException>(() => LoadedOptions.LoadLatest(string.Empty));
    }
}

/// <summary>
/// Aggregating memory usage across several databases and caches at once.
/// </summary>
/// <remarks>
/// That aggregation is the whole point: every individual figure is already
/// reachable through <see cref="Cache.Usage"/> or a property read with
/// <see cref="RocksDb.GetAggregatedPropertyInt"/>. So these tests assert the
/// spanning behaviour rather than the numbers, which belong to RocksDb.
/// </remarks>
public class ApproximateMemoryUsageTests
{
    [Fact]
    public void Snapshot_CoversTheDatabasesAndCachesItWasGiven()
    {
        using var cache = Cache.CreateLru(8 * 1024 * 1024);

        using var firstOptions = new DbOptions { CreateIfMissing = true };
        firstOptions.Env = Env.CreateInMemory();
        using RocksDb first = RocksDb.Open(firstOptions, TestDb.InMemoryPath);

        using var secondOptions = new DbOptions { CreateIfMissing = true };
        secondOptions.Env = Env.CreateInMemory();
        using RocksDb second = RocksDb.Open(secondOptions, TestDb.InMemoryPath);

        for (int i = 0; i < 200; i++)
        {
            first.Put($"key{i:D4}", new string('a', 512));
            second.Put($"key{i:D4}", new string('b', 512));
        }

        using MemoryConsumers consumers = MemoryConsumers.Create()
            .Add(first)
            .Add(second)
            .Add(cache);

        using ApproximateMemoryUsage usage = ApproximateMemoryUsage.Take(consumers);

        // Both databases have unflushed data, so the memtable totals must be
        // non-zero and the unflushed part cannot exceed the whole.
        Assert.True(usage.MemTableTotal > 0, "memtables should be holding something");
        Assert.True(
            usage.MemTableUnflushed <= usage.MemTableTotal,
            $"unflushed {usage.MemTableUnflushed} exceeded total {usage.MemTableTotal}");
    }

    /// <summary>
    /// A snapshot over two databases exceeds one over either alone, which is
    /// the aggregation this type exists for.
    /// </summary>
    [Fact]
    public void SnapshotOverTwoDatabasesExceedsEither()
    {
        using var firstOptions = new DbOptions { CreateIfMissing = true };
        firstOptions.Env = Env.CreateInMemory();
        using RocksDb first = RocksDb.Open(firstOptions, TestDb.InMemoryPath);

        using var secondOptions = new DbOptions { CreateIfMissing = true };
        secondOptions.Env = Env.CreateInMemory();
        using RocksDb second = RocksDb.Open(secondOptions, TestDb.InMemoryPath);

        for (int i = 0; i < 200; i++)
        {
            first.Put($"key{i:D4}", new string('a', 512));
            second.Put($"key{i:D4}", new string('b', 512));
        }

        ulong Measure(params RocksDb[] databases)
        {
            using MemoryConsumers consumers = MemoryConsumers.Create();

            foreach (RocksDb db in databases)
            {
                consumers.Add(db);
            }

            using ApproximateMemoryUsage usage = ApproximateMemoryUsage.Take(consumers);
            return usage.MemTableTotal;
        }

        ulong justFirst = Measure(first);
        ulong both = Measure(first, second);

        Assert.True(
            both > justFirst,
            $"two databases reported {both}, which is not above the {justFirst} of one");
    }

    /// <summary>A snapshot outlives the consumers it was taken from.</summary>
    /// <remarks>
    /// The figures are read when the snapshot is created, so the collection is
    /// finished with immediately. Documented, and therefore asserted.
    /// </remarks>
    [Fact]
    public void SnapshotSurvivesTheConsumersBeingDisposed()
    {
        using var db = new TempDb();

        db.Db.Put("k", new string('v', 4096));

        ApproximateMemoryUsage usage;

        using (MemoryConsumers consumers = MemoryConsumers.Create().Add(db.Db))
        {
            usage = ApproximateMemoryUsage.Take(consumers);
        }

        using (usage)
        {
            Assert.True(usage.MemTableTotal > 0);
        }
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        using MemoryConsumers consumers = MemoryConsumers.Create();

        Assert.Throws<ArgumentNullException>(() => consumers.Add((RocksDb)null!));
        Assert.Throws<ArgumentNullException>(() => consumers.Add((Cache)null!));
        Assert.Throws<ArgumentNullException>(() => ApproximateMemoryUsage.Take(null!));
    }
}
