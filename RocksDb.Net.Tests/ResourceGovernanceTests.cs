namespace RocksDbNet.Tests;

/// <summary>
/// Disk-space and memory governance. See issue #74.
/// </summary>
public class ResourceGovernanceTests
{
    // ── SstFileManager ───────────────────────────────────────────────────────

    [Fact]
    public void SstFileManager_PropertiesRoundTrip()
    {
        using SstFileManager manager = SstFileManager.Create();

        manager.DeleteRateBytesPerSecond = 1024 * 1024;
        Assert.Equal(1024 * 1024, manager.DeleteRateBytesPerSecond);

        manager.MaxTrashDbRatio = 0.5;
        Assert.Equal(0.5, manager.MaxTrashDbRatio, 6);

        // No limit set yet, so nothing is reached.
        Assert.False(manager.IsMaxAllowedSpaceReached());
        Assert.False(manager.IsMaxAllowedSpaceReached(includingCompactions: true));
    }

    [Fact]
    public void SstFileManager_AcceptsACallerSuppliedEnv()
    {
        using Env env = Env.Create();
        using SstFileManager manager = SstFileManager.Create(env);

        Assert.Equal(0UL, manager.TotalTrashSize);
    }

    /// <summary>
    /// The manager must actually observe the database's files, which is what
    /// makes the space cap meaningful.
    /// </summary>
    [Fact]
    public void SstFileManager_TracksTheDatabaseFiles()
    {
        using var dir = new TempDir();
        using SstFileManager manager = SstFileManager.Create();

        var opts = new DbOptions { CreateIfMissing = true };
        opts.SstFileManager = manager;

        using var db = RocksDb.Open(opts, dir.Path);

        Assert.Equal(0UL, manager.TotalSize);

        for (int i = 0; i < 200; i++)
        {
            db.Put($"key{i:D4}", new string('v', 1024));
        }

        db.Flush();

        Assert.True(manager.TotalSize > 0, "the manager should account for the flushed SST");
    }

    /// <summary>
    /// The behavioural test: past the cap, writes must fail rather than
    /// consuming more disk.
    /// </summary>
    [Fact]
    public void SstFileManager_SpaceCap_EventuallyStopsWrites()
    {
        using var dir = new TempDir();
        using SstFileManager manager = SstFileManager.Create();

        // Small enough that a few flushes exceed it.
        manager.MaxAllowedSpaceUsage = 64 * 1024;

        var opts = new DbOptions { CreateIfMissing = true, WriteBufferSize = 16 * 1024 };
        opts.SstFileManager = manager;

        using var db = RocksDb.Open(opts, dir.Path);

        Exception? failure = null;
        for (int i = 0; i < 400 && failure is null; i++)
        {
            failure = Record.Exception(() =>
            {
                db.Put($"key{i:D4}", new string('v', 1024));
                db.Flush();
            });
        }

        Assert.NotNull(failure);
        Assert.IsType<RocksDbException>(failure);
        Assert.True(manager.IsMaxAllowedSpaceReached(), "the cap should report itself as reached");
    }

    [Fact]
    public void SstFileManager_CompactionBufferSize_IsAccepted()
    {
        using var dir = new TempDir();
        using SstFileManager manager = SstFileManager.Create();

        manager.MaxAllowedSpaceUsage = 64 * 1024 * 1024;
        manager.CompactionBufferSize = 8 * 1024 * 1024;

        var opts = new DbOptions { CreateIfMissing = true };
        opts.SstFileManager = manager;

        using var db = RocksDb.Open(opts, dir.Path);
        db.Put("key", "value");
        db.Flush();

        Assert.Equal("value", db.GetString("key"));
    }

    /// <summary>
    /// RocksDb takes a shared reference, so the same manager may govern two
    /// databases and disposing it after assignment is safe.
    /// </summary>
    [Fact]
    public void SstFileManager_CanBeSharedAndDisposedAfterAssignment()
    {
        using var firstDir = new TempDir();
        using var secondDir = new TempDir();

        var firstOptions = new DbOptions { CreateIfMissing = true };
        var secondOptions = new DbOptions { CreateIfMissing = true };

        SstFileManager manager = SstFileManager.Create();
        firstOptions.SstFileManager = manager;
        secondOptions.SstFileManager = manager;

        // Disposed while both databases still reference it natively.
        manager.Dispose();

        using var first = RocksDb.Open(firstOptions, firstDir.Path);
        using var second = RocksDb.Open(secondOptions, secondDir.Path);

        first.Put("a", "1");
        second.Put("b", "2");
        first.Flush();
        second.Flush();

        Assert.Equal("1", first.GetString("a"));
        Assert.Equal("2", second.GetString("b"));
    }

    // ── WriteBufferManager ───────────────────────────────────────────────────

    [Fact]
    public void WriteBufferManager_PropertiesRoundTrip()
    {
        using WriteBufferManager manager = WriteBufferManager.Create(8 * 1024 * 1024);

        Assert.True(manager.IsEnabled);
        Assert.False(manager.CostsToCache);
        Assert.Equal((ulong)(8 * 1024 * 1024), manager.BufferSize);

        manager.BufferSize = 16 * 1024 * 1024;
        Assert.Equal((ulong)(16 * 1024 * 1024), manager.BufferSize);

        // Write-only natively, so only the assignment can be exercised.
        manager.AllowStall = true;
        manager.AllowStall = false;
    }

    /// <summary>
    /// A zero budget means the manager tracks without enforcing, which is what
    /// RocksDb calls disabled.
    /// </summary>
    [Fact]
    public void WriteBufferManager_ZeroBudget_IsDisabled()
    {
        using WriteBufferManager manager = WriteBufferManager.Create(0);

        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public void WriteBufferManager_WithACache_CostsToIt()
    {
        using Cache cache = Cache.CreateLru(16 * 1024 * 1024);
        using WriteBufferManager manager = WriteBufferManager.Create(4 * 1024 * 1024, cache);

        Assert.True(manager.IsEnabled);
        Assert.True(manager.CostsToCache);

        Assert.Throws<ArgumentNullException>(() => WriteBufferManager.Create(1024, null!));
    }

    /// <summary>
    /// The behavioural test: memtable memory across two column families must be
    /// visible in one place, which is the point of the type.
    /// </summary>
    [Fact]
    public void WriteBufferManager_ReportsUsageAcrossColumnFamilies()
    {
        using var dir = new TempDir();
        using WriteBufferManager manager = WriteBufferManager.Create(64 * 1024 * 1024);

        var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        opts.WriteBufferManager = manager;

        using var db = RocksDb.Open(opts, dir.Path, [new("default"), new("cf1")]);
        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");

        // Not zero at rest: each memtable takes an arena block as soon as the
        // column family exists, so the baseline has to be measured rather than
        // assumed.
        ulong baseline = manager.MemoryUsage;

        for (int i = 0; i < 500; i++)
        {
            db.Put($"key{i:D4}", new string('v', 1024));
            db.Put($"key{i:D4}", new string('w', 1024), cf1);
        }

        Assert.True(
            manager.MemoryUsage > baseline,
            $"memtable memory should grow past the {baseline} byte baseline");
        Assert.True(
            manager.MutableMemtableMemoryUsage <= manager.MemoryUsage,
            "mutable usage is a subset of total usage");
    }

    /// <summary>
    /// With a cache, memtable memory is reserved through placeholder entries so
    /// the cache's accounting can see it.
    /// </summary>
    [Fact]
    public void WriteBufferManager_WithACache_ReservesThroughDummyEntries()
    {
        using Cache cache = Cache.CreateLru(64 * 1024 * 1024);
        using WriteBufferManager manager = WriteBufferManager.Create(32 * 1024 * 1024, cache);

        var opts = new DbOptions { CreateIfMissing = true };
        opts.WriteBufferManager = manager;

        using var db = TestDb.OpenInMemory(opts);

        for (int i = 0; i < 500; i++)
        {
            db.Put($"key{i:D4}", new string('v', 1024));
        }

        Assert.True(manager.DummyEntriesInCacheUsage > 0, "the cache should hold placeholder entries");
    }

    /// <summary>
    /// Sharing one budget between databases is the reason the type exists, so
    /// usage must reflect both.
    /// </summary>
    [Fact]
    public void WriteBufferManager_SharedBetweenDatabases_AccountsForBoth()
    {
        using var firstDir = new TempDir();
        using var secondDir = new TempDir();
        using WriteBufferManager manager = WriteBufferManager.Create(64 * 1024 * 1024);

        var firstOptions = new DbOptions { CreateIfMissing = true };
        var secondOptions = new DbOptions { CreateIfMissing = true };
        firstOptions.WriteBufferManager = manager;
        secondOptions.WriteBufferManager = manager;

        using var first = RocksDb.Open(firstOptions, firstDir.Path);

        for (int i = 0; i < 300; i++)
        {
            first.Put($"key{i:D4}", new string('v', 1024));
        }

        ulong afterFirst = manager.MemoryUsage;
        Assert.True(afterFirst > 0);


        using var second = RocksDb.Open(secondOptions, secondDir.Path);

        for (int i = 0; i < 300; i++)
        {
            second.Put($"key{i:D4}", new string('w', 1024));
        }

        Assert.True(manager.MemoryUsage > afterFirst, "the second database should add to the shared budget");
    }

    [Fact]
    public void DbOptions_RejectNullGovernors()
    {
        using var opts = new DbOptions();

        Assert.Throws<ArgumentNullException>(() => opts.SstFileManager = null!);
        Assert.Throws<ArgumentNullException>(() => opts.WriteBufferManager = null!);
    }
}
