using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// The second batch of small gaps from issue #81: cache construction and
/// introspection, the SST partitioner, and the batch create and iterate
/// operations.
/// </summary>
public class SmallGapsCachesAndBatchTests
{
    // ── Cache construction ───────────────────────────────────────────────────

    [Fact]
    public void CreateLru_FromOptions_Works()
    {
        Cache cache;
        using (var options = new LruCacheOptions { Capacity = 8 * 1024 * 1024, NumShardBits = 4 })
        {
            cache = Cache.CreateLru(options);
        }

        using (cache)
        {
            // Options disposed above, because RocksDb copied them.
            Assert.Equal(8UL * 1024 * 1024, cache.Capacity);
        }

        Assert.Throws<ArgumentNullException>(() => Cache.CreateLru((LruCacheOptions)null!));
    }

    [Fact]
    public void CreateHyperClock_FromOptions_Works()
    {
        Cache cache;
        using (var options = new HyperClockCacheOptions(8 * 1024 * 1024, estimatedEntryCharge: 4096)
        {
            NumShardBits = 4,
        })
        {
            cache = Cache.CreateHyperClock(options);
        }

        using (cache)
        {
            Assert.Equal(8UL * 1024 * 1024, cache.Capacity);
        }

        Assert.Throws<ArgumentNullException>(() => Cache.CreateHyperClock((HyperClockCacheOptions)null!));
    }

    /// <summary>
    /// A HyperClock cache must actually serve a database, since its fixed table
    /// makes it more sensitive to the entry-charge estimate than an LRU cache.
    /// </summary>
    [Fact]
    public void HyperClockCache_ServesADatabase()
    {
        using Cache cache = Cache.CreateHyperClock(16 * 1024 * 1024, estimatedEntryChargeBytes: 0);
        var tableOptions = new BlockBasedTableOptions();
        tableOptions.SetBlockCache(cache);

        var opts = new DbOptions { CreateIfMissing = true };
        opts.BlockBasedTableFactory = tableOptions;

        using var db = TestDb.OpenInMemory(opts);

        for (int i = 0; i < 500; i++)
        {
            db.Put($"key{i:D4}", new string('v', 256));
        }

        db.Flush();

        for (int i = 0; i < 500; i++)
        {
            Assert.NotNull(db.GetString($"key{i:D4}"));
        }

        Assert.True(cache.Usage > 0, "the cache should be holding blocks");
    }

    // ── Cache introspection ──────────────────────────────────────────────────

    /// <summary>
    /// Occupancy and usage together give the average entry size, which is what
    /// a HyperClock cache wants configured. Measuring beats guessing.
    /// </summary>
    [Fact]
    public void Cache_ReportsOccupancyAndTableSize()
    {
        using Cache cache = Cache.CreateLru(16 * 1024 * 1024);
        var tableOptions = new BlockBasedTableOptions();
        tableOptions.SetBlockCache(cache);

        var opts = new DbOptions { CreateIfMissing = true };
        opts.BlockBasedTableFactory = tableOptions;

        using var db = TestDb.OpenInMemory(opts);

        for (int i = 0; i < 500; i++)
        {
            db.Put($"key{i:D4}", new string('v', 256));
        }

        db.Flush();

        for (int i = 0; i < 500; i++)
        {
            _ = db.GetString($"key{i:D4}");
        }

        Assert.True(cache.OccupancyCount > 0, "reads should populate the cache");
        Assert.True(cache.TableAddressCount > 0, "the cache should report its table size");
    }

    // ── SST partitioner ──────────────────────────────────────────────────────

    [Fact]
    public void SstPartitionerFactory_RejectsABadPrefixLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SstPartitionerFactory.CreateFixedPrefix(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SstPartitionerFactory.CreateFixedPrefix(-1));
    }

    /// <summary>
    /// With a prefix partitioner, compaction output should split on prefix
    /// boundaries, which means more files than the size target alone would give
    /// for data spread across many prefixes.
    /// </summary>
    [Fact]
    public void SstPartitionerFactory_SplitsFilesOnPrefixBoundaries()
    {
        static int FileCountWith(string path, SstPartitionerFactory? partitioner)
        {
            var opts = new DbOptions
            {
                CreateIfMissing = true,
                DisableAutoCompactions = true,
                TargetFileSizeBase = 4 * 1024 * 1024,
            };

            if (partitioner is not null)
            {
                opts.SstPartitionerFactory = partitioner;
            }

            using var db = RocksDb.Open(opts, path);

            var random = new Random(7);
            byte[] value = new byte[128];

            // Twenty prefixes, so a partitioner has plenty of boundaries to
            // split on while the size target alone would produce one file.
            //
            // Two overlapping passes and two flushes, so the compaction has to
            // rewrite rather than trivially move a single file. A trivial move
            // produces no new output and so consults no partitioner, which is
            // why the first version of this test saw one file either way.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int prefix = 0; prefix < 20; prefix++)
                {
                    for (int i = 0; i < 100; i++)
                    {
                        random.NextBytes(value);
                        db.Put(Encoding.UTF8.GetBytes($"p{prefix:D2}-key{i:D4}"), value);
                    }
                }

                db.Flush();
            }

            db.CompactRange();

            ColumnFamilyMetadata? metadata = db.GetColumnFamilyMetadata();
            return metadata!.FileCount;
        }

        using var withoutDir = new TempDir();
        using var withDir = new TempDir();

        int without = FileCountWith(withoutDir.Path, null);

        int with;
        using (SstPartitionerFactory partitioner = SstPartitionerFactory.CreateFixedPrefix(3))
        {
            with = FileCountWith(withDir.Path, partitioner);
        }

        Assert.True(
            with > without,
            $"the partitioner should split into more files, saw {with} with and {without} without");
    }

    [Fact]
    public void DbOptions_RejectsANullPartitionerFactory()
    {
        using var opts = new DbOptions();

        Assert.Throws<ArgumentNullException>(() => opts.SstPartitionerFactory = null!);
    }

    // ── Batch column family creation ─────────────────────────────────────────

    [Fact]
    public void CreateColumnFamilies_CreatesThemAllAndRegistersThem()
    {
        using var db = new TempDb();
        using var cfOptions = new DbOptions();

        IReadOnlyList<ColumnFamilyHandle> created =
            db.Db.CreateColumnFamilies(cfOptions, ["alpha", "beta", "gamma"]);

        Assert.Equal(3, created.Count);

        // Order matches the names given.
        Assert.Same(created[0], db.Db.GetColumnFamily("alpha"));
        Assert.Same(created[1], db.Db.GetColumnFamily("beta"));
        Assert.Same(created[2], db.Db.GetColumnFamily("gamma"));

        db.Db.Put("key", "in-beta", created[1]);
        Assert.Equal("in-beta", db.Db.GetString("key", created[1]));
        Assert.Null(db.Db.GetString("key"));

        foreach (string name in new[] { "alpha", "beta", "gamma" })
        {
            Assert.Contains(name, db.Db.ColumnFamilyNames);
        }
    }

    [Fact]
    public void CreateColumnFamilies_SurvivesReopen()
    {
        using var dir = new TempDir();

        using (var opts = new DbOptions { CreateIfMissing = true })
        using (var db = RocksDb.Open(opts, dir.Path))
        using (var cfOptions = new DbOptions())
        {
            IReadOnlyList<ColumnFamilyHandle> created = db.CreateColumnFamilies(cfOptions, ["one", "two"]);
            db.Put("key", "value", created[0]);
        }

        using var reopenOptions = new DbOptions { CreateIfMissing = true };
        using var reopened = RocksDb.Open(
            reopenOptions, dir.Path, [new("default"), new("one"), new("two")]);

        Assert.Equal("value", reopened.GetString("key", reopened.GetColumnFamily("one")));
    }

    [Fact]
    public void CreateColumnFamilies_EmptyList_ReturnsEmpty()
    {
        using var db = new TempDb();
        using var cfOptions = new DbOptions();

        Assert.Empty(db.Db.CreateColumnFamilies(cfOptions, []));
    }

    [Fact]
    public void CreateColumnFamilies_RejectsBadArguments()
    {
        using var db = new TempDb();
        using var cfOptions = new DbOptions();

        Assert.Throws<ArgumentNullException>(() => db.Db.CreateColumnFamilies(null!, ["x"]));
        Assert.Throws<ArgumentNullException>(() => db.Db.CreateColumnFamilies(cfOptions, null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.CreateColumnFamilies(cfOptions, [null!]));
        Assert.Throws<ArgumentException>(() => db.Db.CreateColumnFamilies(cfOptions, [string.Empty]));
    }

    // ── Multi-family iterators ───────────────────────────────────────────────

    /// <summary>
    /// The reason the call exists: iterators created together share one view,
    /// so a write landing afterwards is invisible to all of them rather than to
    /// some.
    /// </summary>
    [Fact]
    public void NewIterators_ShareOneConsistentView()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using var db = RocksDb.Open(opts, dir.Path, [new("default"), new("cf1"), new("cf2")]);

        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");
        ColumnFamilyHandle cf2 = db.GetColumnFamily("cf2");

        db.Put("before", "1", cf1);
        db.Put("before", "2", cf2);

        IReadOnlyList<Iterator> iterators = db.NewIterators([cf1, cf2]);

        try
        {
            Assert.Equal(2, iterators.Count);

            // Written after the iterators were created.
            db.Put("after", "3", cf1);
            db.Put("after", "4", cf2);

            foreach (Iterator iter in iterators)
            {
                var keys = new List<string>();
                for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
                {
                    keys.Add(iter.KeyAsString());
                }

                // Neither iterator sees the later write.
                Assert.Equal(["before"], keys);
            }
        }
        finally
        {
            foreach (Iterator iter in iterators)
            {
                iter.Dispose();
            }
        }

        // And the write did land.
        Assert.Equal("3", db.GetString("after", cf1));
    }

    [Fact]
    public void NewIterators_EmptyList_ReturnsEmpty()
    {
        using var db = new TempDb();

        Assert.Empty(db.Db.NewIterators([]));
    }

    [Fact]
    public void NewIterators_RejectsBadArguments()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.NewIterators(null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.NewIterators([null!]));
    }

    [Fact]
    public void NewIterators_HonoursReadOptions()
    {
        using var db = new TempDb();
        ColumnFamilyHandle defaultCf = db.Db.GetDefaultColumnFamily();

        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");

        using var readOptions = new ReadOptions();
        readOptions.SetIterateUpperBound("c"u8.ToArray());

        IReadOnlyList<Iterator> iterators = db.Db.NewIterators([defaultCf], readOptions);

        try
        {
            Iterator iter = Assert.Single(iterators);

            var keys = new List<string>();
            for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
            {
                keys.Add(iter.KeyAsString());
            }

            Assert.Equal(["a", "b"], keys);
        }
        finally
        {
            foreach (Iterator iter in iterators)
            {
                iter.Dispose();
            }
        }
    }
}
