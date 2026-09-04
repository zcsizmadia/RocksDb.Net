using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Locks in the behaviours whose documentation was wrong, from issue #60.
/// Every doc comment corrected there was corrected against the RocksDb
/// headers; these assert the same claims against the running library, so the
/// docs cannot drift back without a test failing.
/// </summary>
public class DocumentedBehaviourTests
{
    // ── Flush with an empty list ─────────────────────────────────────────────

    /// <summary>
    /// The no-argument flush covers the default column family only, and an
    /// empty list flushes nothing at all.
    /// </summary>
    /// <remarks>
    /// Two corrections, in two passes. Issue #60 asserted that an empty list
    /// flushes every family; measured, it flushed only the default, because the
    /// wrapper fell through to the no-argument overload. That fall-through is
    /// now gone, so an empty list flushes nothing, which is what RocksDb does
    /// with an empty array of handles and what a caller who filtered a list down
    /// to nothing means.
    /// </remarks>
    [Fact]
    public void Flush_CoversTheDefaultColumnFamilyOnly()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using var db = RocksDb.Open(opts, dir.Path, [new("default"), new("other")]);
        ColumnFamilyHandle other = db.GetColumnFamily("other");

        db.Put("a", "1");
        db.Put("b", "2", other);

        // Both families have an unflushed entry at this point.
        Assert.Equal("1", db.GetProperty("rocksdb.num-entries-active-mem-table"));
        Assert.Equal("1", db.GetProperty("rocksdb.num-entries-active-mem-table", other));

        // Nothing named, nothing flushed. Both entries are still in memory.
        db.Flush([]);

        Assert.Equal("1", db.GetProperty("rocksdb.num-entries-active-mem-table"));
        Assert.Equal("1", db.GetProperty("rocksdb.num-entries-active-mem-table", other));

        // The no-argument overload is the one that means the default family.
        db.Flush();

        Assert.Equal("0", db.GetProperty("rocksdb.num-entries-active-mem-table"));
        Assert.Equal("1", db.GetProperty("rocksdb.num-entries-active-mem-table", other));

        // Naming the family explicitly is what flushes it.
        db.Flush([other]);
        Assert.Equal("0", db.GetProperty("rocksdb.num-entries-active-mem-table", other));

        Assert.Equal("1", db.GetString("a"));
        Assert.Equal("2", db.GetString("b", other));
    }

    // ── Adding event listeners accumulates ──────────────────────────────────

    private sealed class CountingListener : EventListener
    {
        public int Flushes;

        public override void OnFlushCompleted(FlushJobInfo info) => Interlocked.Increment(ref Flushes);
    }

    /// <summary>
    /// Adding two listeners leaves both installed and both firing.
    /// </summary>
    /// <remarks>
    /// This was a property setter, and a property that accumulates reads like an
    /// assignment that replaces: <c>options.EventListener = a;</c> then
    /// <c>options.EventListener = b;</c> left both installed with no way to take
    /// either off. The behaviour is unchanged and correct, since RocksDb cannot
    /// remove a listener; only the spelling changed, to one that says what it
    /// does.
    /// </remarks>
    [Fact]
    public void AddEventListener_Accumulates_SoBothListenersFire()
    {
        var first = new CountingListener();
        var second = new CountingListener();

        using var dir = new TempDir();
        var opts = new DbOptions { CreateIfMissing = true };
        opts.AddEventListener(first);
        opts.AddEventListener(second);

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("k", "v");
            db.Flush();
        }

        // The second assignment did not displace the first.
        Assert.True(first.Flushes > 0, "the first listener should still receive events");
        Assert.True(second.Flushes > 0, "the second listener should also receive events");
    }

    // ── IsEmpty is an estimate that counts tombstones ───────────────────────

    /// <summary>
    /// The estimate subtracts twice the deletion count, so deleting keys that
    /// were never present drives it to zero while every real key remains.
    /// </summary>
    /// <remarks>
    /// Issue #60 said the estimate counts tombstones and therefore
    /// over-reports. It is the other way round: it under-reports, and this is
    /// the case that shows it.
    /// </remarks>
    [Fact]
    public void IsEmpty_CanReportEmptyWhileEveryKeyIsStillThere()
    {
        using var db = new TempDb();

        Assert.True(db.Db.IsEmpty);

        for (int i = 0; i < 100; i++)
        {
            db.Db.Put($"real{i:D3}", "value");
        }

        db.Db.Flush();
        Assert.False(db.Db.IsEmpty);

        // Delete a hundred keys that never existed. Nothing is removed, but the
        // estimate drops by two for each of them.
        for (int i = 0; i < 100; i++)
        {
            db.Db.Delete($"ghost{i:D3}");
        }

        Assert.True(db.Db.IsEmpty);

        // And yet every one of the real keys still reads back.
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal("value", db.Db.GetString($"real{i:D3}"));
        }
    }

    // ── ValueSizeSoftLimit: zero is the smallest limit, not "no limit" ──────

    /// <summary>
    /// Zero is not "unlimited". It is the tightest possible limit, so every key
    /// after the first comes back aborted, which this wrapper surfaces as a
    /// thrown exception.
    /// </summary>
    [Fact]
    public void ValueSizeSoftLimit_Zero_AbortsEveryKeyAfterTheFirst()
    {
        using var db = new TempDb();

        for (int i = 0; i < 5; i++)
        {
            db.Db.Put($"key{i}", "value");
        }

        List<byte[]> keys = [.. Enumerable.Range(0, 5).Select(i => Encoding.UTF8.GetBytes($"key{i}"))];

        // The default reads all five.
        Assert.All(db.Db.MultiGet(keys), v => Assert.NotNull(v));

        using var limited = new ReadOptions { ValueSizeSoftLimit = 0 };
        Assert.Throws<RocksDbException>(() => db.Db.MultiGet(keys, limited));

        // A single key still succeeds, because the read always makes progress.
        Assert.NotNull(db.Db.MultiGet([keys[0]], limited)[0]);
    }

    [Fact]
    public void ValueSizeSoftLimit_DefaultsToNoEffectiveLimit()
    {
        using var opts = new ReadOptions();

        Assert.Equal(ulong.MaxValue, opts.ValueSizeSoftLimit);
    }

    // IgnoreSnapshots is gone. It was documented as defaulting to false with
    // true meaning "skip snapshotted entries", and both halves were wrong:
    // RocksDb creates it true, has deprecated the setting, and fails table file
    // creation if a filter reports false. A property whose only legal value is
    // the one it already holds is better not offered.

    // ── A null block cache is ignored, not a way to disable caching ─────────

    /// <summary>
    /// Passing null does not clear a cache set earlier, because the C API
    /// ignores a null argument. The cache stays in use.
    /// </summary>
    [Fact]
    public void SetBlockCache_Null_DoesNotClearTheCacheAlreadySet()
    {
        using var dir = new TempDir();
        using var cache = Cache.CreateLru(8 * 1024 * 1024);

        var tableOptions = new BlockBasedTableOptions();
        tableOptions.SetBlockCache(cache);

        // If this cleared the cache, the database below would not use it.
        tableOptions.SetBlockCache(null);

        var opts = new DbOptions { CreateIfMissing = true };
        opts.BlockBasedTableFactory = tableOptions;

        using var db = RocksDb.Open(opts, dir.Path);

        for (int i = 0; i < 200; i++)
        {
            db.Put($"key{i:D4}", "value");
        }

        db.Flush();

        for (int i = 0; i < 200; i++)
        {
            Assert.Equal("value", db.GetString($"key{i:D4}"));
        }

        Assert.True(cache.Usage > 0, "the cache set before the null call should still be in use");
    }

    // ── StopBackup retires the engine for good ──────────────────────────────

    /// <summary>
    /// One-way: it is not just the interrupted backup that fails, but every
    /// later one on the same engine. A new engine is needed.
    /// </summary>
    [Fact]
    public void StopBackup_IsOneWay_SoLaterBackupsAlsoFail()
    {
        using var db = new TempDb();
        using var backupDir = new TempDir();

        db.Db.Put("k", "v");

        using (var engine = BackupEngine.Open(db.Options, backupDir.Path))
        {
            // Works before stopping.
            engine.CreateNewBackup(db.Db);

            engine.StopBackup();

            // And fails afterwards, with no backup running to interrupt.
            Assert.Throws<RocksDbException>(() => engine.CreateNewBackup(db.Db));
        }

        // A fresh engine over the same directory works again.
        using var replacement = BackupEngine.Open(db.Options, backupDir.Path);
        replacement.CreateNewBackup(db.Db);

        Assert.NotEmpty(replacement.AsEnumerable());
    }

    // ── ProtectionBytesPerKey accepts only 0 and 8 ──────────────────────────

    [Fact]
    public void ProtectionBytesPerKey_AcceptsZeroAndEight()
    {
        using var db = new TempDb();

        using var unprotected = new WriteOptions { ProtectionBytesPerKey = 0 };
        db.Db.Put("a", "1", unprotected);
        Assert.Equal("1", db.Db.GetString("a"));

        using var protectedWrites = new WriteOptions { ProtectionBytesPerKey = 8 };
        db.Db.Put("b", "2", protectedWrites);
        Assert.Equal("2", db.Db.GetString("b"));

        Assert.Equal(0UL, unprotected.ProtectionBytesPerKey);
        Assert.Equal(8UL, protectedWrites.ProtectionBytesPerKey);
    }

    // ── Incremental sync is off at zero ─────────────────────────────────────

    [Fact]
    public void BytesPerSync_DefaultsToZeroMeaningDisabled()
    {
        using var opts = new DbOptions();

        Assert.Equal(0UL, opts.BytesPerSync);
        Assert.Equal(0UL, opts.WalBytesPerSync);
    }

    // ── A histogram with no statistics reads as zeros, not null ─────────────

    /// <summary>
    /// The nullable return type suggests absence is signalled by null. It is
    /// not: without a statistics object every field reads zero, which is
    /// indistinguishable from "no samples".
    /// </summary>
    [Fact]
    public void GetHistogramData_WithoutStatistics_ReturnsZerosRatherThanNull()
    {
        using var opts = new DbOptions();

        HistogramData? data = opts.GetHistogramData(0);

        Assert.NotNull(data);
        Assert.Equal(0UL, data.Count);
        Assert.Equal(0UL, data.Sum);
        Assert.Equal(0d, data.Median);
    }
}
