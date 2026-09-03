using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// The first batch of assorted small gaps from issue #81: pinning tiers,
/// SingleDelete, options from a string, multiple database paths, and the
/// manual compaction toggle.
/// </summary>
public class SmallGapsTests
{
    // ── Pinning tiers ────────────────────────────────────────────────────────

    /// <summary>
    /// A correction worth recording. During the 11.8.1 upgrade I drafted this
    /// enum and then dropped it, having concluded the setting was a boolean.
    /// That was true of <c>pin_top_level_index_and_filter</c> and false of these
    /// three, which take a real four-value tier.
    /// </summary>
    [Fact]
    public void PinningTier_MatchesTheNativeValues()
    {
        Assert.Equal(0, (int)PinningTier.Fallback);
        Assert.Equal(1, (int)PinningTier.None);
        Assert.Equal(2, (int)PinningTier.FlushedAndSimilar);
        Assert.Equal(3, (int)PinningTier.All);
        Assert.Equal(4, Enum.GetValues<PinningTier>().Length);
    }

    [Fact]
    public void PinningTiers_AreAcceptedAndTheDatabaseStillWorks()
    {
        using var dir = new TempDir();
        using var cache = Cache.CreateLru(8 * 1024 * 1024);
        var tableOptions = new BlockBasedTableOptions
        {
            TopLevelIndexPinningTier = PinningTier.All,
            PartitionPinningTier = PinningTier.FlushedAndSimilar,
            UnpartitionedPinningTier = PinningTier.None,
        };
        tableOptions.SetBlockCache(cache);

        var opts = new DbOptions { CreateIfMissing = true };
        opts.BlockBasedTableFactory = tableOptions;

        using var db = RocksDb.Open(opts, dir.Path);

        for (int i = 0; i < 200; i++)
        {
            db.Put($"key{i:D4}", "value");
        }

        db.Flush();

        Assert.Equal("value", db.GetString("key0100"));
    }

    // ── SingleDelete ─────────────────────────────────────────────────────────

    /// <summary>
    /// For a key written exactly once, a single delete behaves like an ordinary
    /// one.
    /// </summary>
    [Fact]
    public void SingleDelete_RemovesAKeyWrittenOnce()
    {
        using var db = new TempDb();

        db.Db.Put("write-once", "value");
        Assert.Equal("value", db.Db.GetString("write-once"));

        db.Db.SingleDelete("write-once");

        Assert.Null(db.Db.GetString("write-once"));
    }

    [Fact]
    public void SingleDelete_SurvivesFlushAndCompaction()
    {
        using var db = new TempDb();

        for (int i = 0; i < 100; i++)
        {
            db.Db.Put($"key{i:D3}", "value");
        }

        db.Db.Flush();

        for (int i = 0; i < 50; i++)
        {
            db.Db.SingleDelete($"key{i:D3}");
        }

        db.Db.Flush();
        db.Db.CompactRange();

        Assert.Null(db.Db.GetString("key000"));
        Assert.Null(db.Db.GetString("key049"));
        Assert.Equal("value", db.Db.GetString("key050"));
        Assert.Equal("value", db.Db.GetString("key099"));
    }

    [Fact]
    public void SingleDelete_ColumnFamily_AffectsThatFamilyOnly()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using var db = RocksDb.Open(opts, dir.Path, [new("default"), new("cf1")]);
        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");

        db.Put("key", "in-default");
        db.Put("key", "in-cf1", cf1);

        db.SingleDelete("key"u8, cf1);

        Assert.Null(db.GetString("key", cf1));
        Assert.Equal("in-default", db.GetString("key"));

        Assert.Throws<ArgumentNullException>(() => db.SingleDelete("key"u8, (ColumnFamilyHandle)null!));
    }

    // ── Options from a string ────────────────────────────────────────────────

    [Fact]
    public void WithOptionsFromString_AppliesTheSettings()
    {
        using var baseOptions = new DbOptions { CreateIfMissing = true };

        using DbOptions parsed = baseOptions.WithOptionsFromString(
            "write_buffer_size=131072;max_write_buffer_number=5");

        Assert.Equal((nuint)131072, parsed.WriteBufferSize);
        Assert.Equal(5, parsed.MaxWriteBufferNumber);
    }

    /// <summary>
    /// It returns a new object rather than mutating the receiver, so the base
    /// options are reusable as a template.
    /// </summary>
    [Fact]
    public void WithOptionsFromString_LeavesTheReceiverUnchanged()
    {
        using var baseOptions = new DbOptions { WriteBufferSize = 64 * 1024 };

        using DbOptions parsed = baseOptions.WithOptionsFromString("write_buffer_size=262144");

        Assert.Equal((nuint)(64 * 1024), baseOptions.WriteBufferSize);
        Assert.Equal((nuint)262144, parsed.WriteBufferSize);
    }

    /// <summary>
    /// A bad setting throws rather than being quietly ignored, which is the
    /// whole point of parsing configuration.
    /// </summary>
    [Fact]
    public void WithOptionsFromString_RejectsNonsense()
    {
        using var baseOptions = new DbOptions();

        Assert.Throws<RocksDbException>(() => baseOptions.WithOptionsFromString("not_a_real_option=1"));
        Assert.Throws<ArgumentNullException>(() => baseOptions.WithOptionsFromString(null!));
        Assert.Throws<ArgumentException>(() => baseOptions.WithOptionsFromString(string.Empty));
    }

    [Fact]
    public void WithOptionsFromString_ProducesUsableOptions()
    {
        using var dir = new TempDir();
        using var baseOptions = new DbOptions { CreateIfMissing = true };

        DbOptions parsed = baseOptions.WithOptionsFromString("write_buffer_size=131072");

        using var db = RocksDb.Open(parsed, dir.Path);
        db.Put("key", "value");
        db.Flush();

        Assert.Equal("value", db.GetString("key"));
    }

    // ── Multiple database paths ──────────────────────────────────────────────

    [Fact]
    public void DbPath_ExposesWhatItWasGiven()
    {
        using var dir = new TempDir();
        using var path = new DbPath(dir.Path, 1024 * 1024);

        Assert.Equal(dir.Path, path.Path);
        Assert.Equal(1024UL * 1024, path.TargetSizeBytes);

        Assert.Throws<ArgumentNullException>(() => new DbPath(null!, 0));
        Assert.Throws<ArgumentException>(() => new DbPath(string.Empty, 0));
    }

    /// <summary>
    /// A database opens across two directories and its data lands in them,
    /// which is the whole point.
    /// </summary>
    [Fact]
    public void SetDbPaths_SpreadsDataAcrossDirectories()
    {
        using var root = new TempDir();
        string first = Path.Combine(root.Path, "fast");
        string second = Path.Combine(root.Path, "slow");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        var opts = new DbOptions { CreateIfMissing = true, WriteBufferSize = 16 * 1024 };

        // Tiny target on the first path, so data overflows to the second.
        using (var fastPath = new DbPath(first, 32 * 1024))
        using (var slowPath = new DbPath(second, 0))
        {
            opts.SetDbPaths([fastPath, slowPath]);
        }

        // The path objects are disposed above, because RocksDb copied them.
        using var db = RocksDb.Open(opts, root.Path);

        var random = new Random(1);
        byte[] value = new byte[512];

        for (int i = 0; i < 400; i++)
        {
            random.NextBytes(value);
            db.Put(Encoding.UTF8.GetBytes($"key{i:D4}"), value);
        }

        db.Flush();
        db.CompactRange();
        db.WaitForCompact();

        int firstCount = Directory.GetFiles(first, "*.sst").Length;
        int secondCount = Directory.GetFiles(second, "*.sst").Length;

        Assert.True(
            firstCount + secondCount > 0,
            $"data should live in the configured paths, saw {firstCount} and {secondCount}");

        Assert.NotNull(db.Get(Encoding.UTF8.GetBytes("key0200")));
    }

    [Fact]
    public void SetDbPaths_RejectsBadArguments()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions();
        using var path = new DbPath(dir.Path, 0);

        Assert.Throws<ArgumentNullException>(() => opts.SetDbPaths(null!));
        Assert.Throws<ArgumentException>(() => opts.SetDbPaths([]));
        Assert.Throws<ArgumentNullException>(() => opts.SetDbPaths([null!]));
        Assert.Same(opts, opts.SetDbPaths([path]));
    }

    // ── Manual compaction toggle ─────────────────────────────────────────────

    /// <summary>
    /// Unlike <c>CancelAllBackgroundWork</c>, this is reversible, and unlike
    /// <c>PauseBackgroundWork</c> it leaves automatic compaction alone.
    /// </summary>
    [Fact]
    public void ManualCompaction_CanBeDisabledAndEnabledAgain()
    {
        using var db = new TempDb();

        db.Db.WriteOverlappingSstFiles();

        db.Db.DisableManualCompaction();

        // Automatic work still runs, and the database stays usable.
        db.Db.Put("while-disabled", "value");
        db.Db.Flush();
        Assert.Equal("value", db.Db.GetString("while-disabled"));

        db.Db.EnableManualCompaction();

        // And a manual compaction works again afterwards.
        db.Db.CompactRange();
        Assert.Equal("1-updated", db.Db.GetString("a"));
    }

    [Fact]
    public void ManualCompaction_ToggleIsIdempotent()
    {
        using var db = new TempDb();

        db.Db.DisableManualCompaction();
        db.Db.DisableManualCompaction();
        db.Db.EnableManualCompaction();
        db.Db.EnableManualCompaction();

        db.Db.Put("key", "value");
        db.Db.Flush();
        db.Db.CompactRange();

        Assert.Equal("value", db.Db.GetString("key"));
    }

    // ── CompactRangeOptions, the remaining settings ──────────────────────────

    [Fact]
    public void CompactRangeOptions_AllowWriteStallAndTargetPathIdRoundTrip()
    {
        using var opts = new CompactRangeOptions
        {
            AllowWriteStall = true,
            TargetPathId = 1,
        };

        Assert.True(opts.AllowWriteStall);
        Assert.Equal(1, opts.TargetPathId);

        opts.AllowWriteStall = false;
        Assert.False(opts.AllowWriteStall);
    }
}
