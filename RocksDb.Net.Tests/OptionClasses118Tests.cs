namespace RocksDbNet.Tests;

/// <summary>
/// Covers the option-class properties added in RocksDb 11.8.1, plus
/// <see cref="RocksDb.SetDbOptions"/> and the <see cref="FlushWalOptions"/>
/// overload of <see cref="RocksDb.FlushWal(FlushWalOptions)"/>. See issue #25.
/// </summary>
public class OptionClasses118Tests
{
    // ── WriteOptions ─────────────────────────────────────────────────────────

    [Fact]
    public void WriteOptions_ProtectionBytesPerKey_GetSet()
    {
        using var opts = new WriteOptions();

        // RocksDb only accepts 0 or 8 here; anything else is rejected.
        opts.ProtectionBytesPerKey = 8;
        Assert.Equal(8UL, opts.ProtectionBytesPerKey);

        opts.ProtectionBytesPerKey = 0;
        Assert.Equal(0UL, opts.ProtectionBytesPerKey);
    }

    [Theory]
    [InlineData(RateLimiterPriority.Low)]
    [InlineData(RateLimiterPriority.Mid)]
    [InlineData(RateLimiterPriority.High)]
    [InlineData(RateLimiterPriority.User)]
    public void WriteOptions_RateLimiterPriority_GetSet(RateLimiterPriority priority)
    {
        using var opts = new WriteOptions();

        opts.RateLimiterPriority = priority;
        Assert.Equal(priority, opts.RateLimiterPriority);
    }

    [Theory]
    [InlineData(IoActivity.Flush)]
    [InlineData(IoActivity.Compaction)]
    [InlineData(IoActivity.DbOpen)]
    [InlineData(IoActivity.Get)]
    [InlineData(IoActivity.MultiGet)]
    [InlineData(IoActivity.DbIterator)]
    [InlineData(IoActivity.GetFileChecksumsFromCurrentManifest)]
    public void WriteOptions_IoActivity_GetSet(IoActivity activity)
    {
        using var opts = new WriteOptions();

        opts.IoActivity = activity;
        Assert.Equal(activity, opts.IoActivity);
    }

    // ── FlushOptions ─────────────────────────────────────────────────────────

    [Fact]
    public void FlushOptions_AllowWriteStall_GetSet()
    {
        using var opts = new FlushOptions();

        opts.AllowWriteStall = true;
        Assert.True(opts.AllowWriteStall);

        opts.AllowWriteStall = false;
        Assert.False(opts.AllowWriteStall);
    }

    [Fact]
    public void FlushOptions_ForceAtomicFlush_GetSet()
    {
        using var opts = new FlushOptions();

        opts.ForceAtomicFlush = true;
        Assert.True(opts.ForceAtomicFlush);

        opts.ForceAtomicFlush = false;
        Assert.False(opts.ForceAtomicFlush);
    }

    [Fact]
    public void FlushOptions_ListenerWait_GetSet()
    {
        using var opts = new FlushOptions();

        opts.ListenerWait = true;
        Assert.True(opts.ListenerWait);

        opts.ListenerWait = false;
        Assert.False(opts.ListenerWait);
    }

    /// <summary>
    /// The point of ListenerWait: with it set, the flush call does not return
    /// until the listener callback has run, so no polling is needed.
    /// </summary>
    [Fact]
    public void FlushOptions_ListenerWait_ObservesCallbackBeforeReturning()
    {
        using var dir = new TempDir();
        var listener = new CountingFlushListener();

        using var dbOpts = new DbOptions { CreateIfMissing = true };
        dbOpts.EventListener = listener;

        using var db = RocksDb.Open(dbOpts, dir.Path);
        db.Put("a", "1");

        using var flushOpts = new FlushOptions { Wait = true, ListenerWait = true };
        db.Flush(flushOpts);

        Assert.True(listener.Count > 0);
    }

    private sealed class CountingFlushListener : EventListener
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override void OnFlushCompleted(FlushJobInfo info)
            => Interlocked.Increment(ref _count);
    }

    // ── FlushWalOptions ──────────────────────────────────────────────────────

    [Fact]
    public void FlushWalOptions_Sync_GetSet()
    {
        using var opts = new FlushWalOptions();

        opts.Sync = true;
        Assert.True(opts.Sync);

        opts.Sync = false;
        Assert.False(opts.Sync);
    }

    [Theory]
    [InlineData(RateLimiterPriority.Low)]
    [InlineData(RateLimiterPriority.High)]
    [InlineData(RateLimiterPriority.User)]
    public void FlushWalOptions_RateLimiterPriority_GetSet(RateLimiterPriority priority)
    {
        using var opts = new FlushWalOptions();

        opts.RateLimiterPriority = priority;
        Assert.Equal(priority, opts.RateLimiterPriority);
    }

    [Fact]
    public void FlushWal_WithOptions_Succeeds()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");

        using var opts = new FlushWalOptions { Sync = true };
        db.Db.FlushWal(opts);

        Assert.Equal("1", db.Db.GetString("a"));
    }

    [Fact]
    public void FlushWal_WithNullOptions_Throws()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.FlushWal(null!));
    }

    // ── CompactRangeOptions ──────────────────────────────────────────────────

    [Theory]
    [InlineData(BlobGarbageCollectionPolicy.Force)]
    [InlineData(BlobGarbageCollectionPolicy.Disable)]
    [InlineData(BlobGarbageCollectionPolicy.UseDefault)]
    public void CompactRangeOptions_BlobGarbageCollectionPolicy_GetSet(BlobGarbageCollectionPolicy policy)
    {
        using var opts = new CompactRangeOptions();

        opts.BlobGarbageCollectionPolicy = policy;
        Assert.Equal(policy, opts.BlobGarbageCollectionPolicy);
    }

    [Fact]
    public void CompactRangeOptions_BlobGarbageCollectionAgeCutoff_GetSet()
    {
        using var opts = new CompactRangeOptions();

        opts.BlobGarbageCollectionAgeCutoff = 0.5;
        Assert.Equal(0.5, opts.BlobGarbageCollectionAgeCutoff);
    }

    // ── WaitForCompactOptions ────────────────────────────────────────────────

    [Fact]
    public void WaitForCompactOptions_WaitForPurge_GetSet()
    {
        using var opts = new WaitForCompactOptions();

        opts.WaitForPurge = true;
        Assert.True(opts.WaitForPurge);

        opts.WaitForPurge = false;
        Assert.False(opts.WaitForPurge);
    }

    // ── IngestExternalFileOptions ────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IngestExternalFileOptions_BoolProperties_RoundTrip(bool value)
    {
        using var opts = new IngestExternalFileOptions();

        opts.MoveFiles = value;
        opts.FailedMoveFallBackToCopy = value;
        opts.LinkFiles = value;
        opts.SnapshotConsistency = value;
        opts.AllowGlobalSeqno = value;
        opts.WriteGlobalSeqno = value;
        opts.AllowBlockingFlush = value;
        opts.FailIfNotBottommostLevel = value;
        opts.VerifyChecksumsBeforeIngest = value;
        opts.VerifyFileChecksum = value;
        opts.FillCache = value;
        opts.PrefetchLmaxIndexAndFilterBlocks = value;
        opts.AllowDbGeneratedFiles = value;

        Assert.Equal(value, opts.MoveFiles);
        Assert.Equal(value, opts.FailedMoveFallBackToCopy);
        Assert.Equal(value, opts.LinkFiles);
        Assert.Equal(value, opts.SnapshotConsistency);
        Assert.Equal(value, opts.AllowGlobalSeqno);
        Assert.Equal(value, opts.WriteGlobalSeqno);
        Assert.Equal(value, opts.AllowBlockingFlush);
        Assert.Equal(value, opts.FailIfNotBottommostLevel);
        Assert.Equal(value, opts.VerifyChecksumsBeforeIngest);
        Assert.Equal(value, opts.VerifyFileChecksum);
        Assert.Equal(value, opts.FillCache);
        Assert.Equal(value, opts.PrefetchLmaxIndexAndFilterBlocks);
        Assert.Equal(value, opts.AllowDbGeneratedFiles);
    }

    [Fact]
    public void IngestExternalFileOptions_IngestBehind_GetSet()
    {
        using var opts = new IngestExternalFileOptions();

        opts.IngestBehind = true;
        Assert.True(opts.IngestBehind);

        opts.IngestBehind = false;
        Assert.False(opts.IngestBehind);
    }

    [Fact]
    public void IngestExternalFileOptions_VerifyChecksumsReadaheadSize_GetSet()
    {
        using var opts = new IngestExternalFileOptions();

        opts.VerifyChecksumsReadaheadSize = 65536;
        Assert.Equal(65536UL, opts.VerifyChecksumsReadaheadSize);
    }

    [Fact]
    public void IngestExternalFileOptions_FileOpeningThreads_GetSet()
    {
        using var opts = new IngestExternalFileOptions();

        opts.FileOpeningThreads = 8;
        Assert.Equal(8, opts.FileOpeningThreads);
    }

    // ── SetDbOptions ─────────────────────────────────────────────────────────

    /// <summary>
    /// max_background_jobs lives on the database, not on a column family, so it
    /// is accepted by SetDbOptions and rejected by SetOptions. That contrast is
    /// what shows the new method really targets the database scope, since
    /// RocksDb exposes no getter to read the value back.
    /// </summary>
    [Fact]
    public void SetDbOptions_AppliesDatabaseWideOption()
    {
        using var db = new TempDb();

        db.Db.SetDbOptions(new Dictionary<string, string> { ["max_background_jobs"] = "3" });

        Assert.Throws<RocksDbException>(() =>
            db.Db.SetOptions(new Dictionary<string, string> { ["max_background_jobs"] = "3" }));
    }

    /// <summary>
    /// And the reverse: a column-family option is rejected at database scope.
    /// </summary>
    [Fact]
    public void SetDbOptions_RejectsColumnFamilyOption()
    {
        using var db = new TempDb();

        db.Db.SetOptions(new Dictionary<string, string> { ["write_buffer_size"] = "131072" });

        Assert.Throws<RocksDbException>(() =>
            db.Db.SetDbOptions(new Dictionary<string, string> { ["write_buffer_size"] = "131072" }));
    }

    [Fact]
    public void SetDbOptions_UnknownOption_Throws()
    {
        using var db = new TempDb();

        Assert.Throws<RocksDbException>(() =>
            db.Db.SetDbOptions(new Dictionary<string, string> { ["not_a_real_option"] = "1" }));
    }

    [Fact]
    public void SetDbOptions_EmptyCollection_DoesNothing()
    {
        using var db = new TempDb();

        db.Db.SetDbOptions(new Dictionary<string, string>());
    }

    [Fact]
    public void SetDbOptions_Null_Throws()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.SetDbOptions(null!));
    }

    /// <summary>
    /// The three set-options entry points share one pinning helper now, so a
    /// regression in it would break all of them. This covers the other two.
    /// </summary>
    [Fact]
    public void SetOptions_StillWorksAfterSharingThePinningHelper()
    {
        using var db = new TempDb();

        db.Db.SetOptions(new Dictionary<string, string> { ["write_buffer_size"] = "131072" });

        using var cfOpts = new DbOptions();
        using var cf = db.Db.CreateColumnFamily(cfOpts, "extra");
        db.Db.SetOptions(cf, new Dictionary<string, string> { ["write_buffer_size"] = "131072" });
    }
}
