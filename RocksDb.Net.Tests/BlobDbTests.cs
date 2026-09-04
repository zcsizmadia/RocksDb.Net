namespace RocksDbNet.Tests;

/// <summary>
/// Integrated BlobDB: large values stored outside the SST files.
/// </summary>
/// <remarks>
/// <para>
/// Every option for this was already wrapped, and nothing exercised any of them
/// beyond a property round-trip, so the feature was configurable but unproven.
/// These tests write past the blob threshold and check RocksDb actually
/// separated the values out, rather than checking that a setter took a number.
/// </para>
/// <para>
/// This is the integrated implementation, configured through the column-family
/// options, not the older stacked BlobDB that was a wrapper class of its own.
/// </para>
/// </remarks>
public class BlobDbTests
{
    private const int MinBlobSize = 256;

    private static DbOptions BlobOptions(RecordingListener? listener = null)
    {
        var options = new DbOptions
        {
            CreateIfMissing = true,
            EnableBlobFiles = true,
            MinBlobSize = MinBlobSize,
        };

        if (listener is not null)
        {
            options.AddEventListener(listener);
        }

        return options;
    }

    /// <summary>
    /// A value over the threshold goes into a blob file, and one under it stays
    /// in the SST.
    /// </summary>
    /// <remarks>
    /// The flush job info reports the blob files it created, which is the only
    /// direct evidence from inside the process that the split happened.
    /// </remarks>
    [Fact]
    public void ValuesOverTheThreshold_GoIntoBlobFiles()
    {
        using var dir = new TempDir();

        var listener = new RecordingListener();
        using RocksDb db = RocksDb.Open(BlobOptions(listener), dir.Path);

        db.Put("small", new string('s', 16));
        db.Put("large", new string('L', MinBlobSize * 4));
        db.Flush();

        Assert.True(Wait.Until(() => listener.FlushCompleted.Count > 0), "no flush completed");

        FlushJobInfo flush = listener.FlushCompleted[0];

        // Exactly one blob file, holding exactly the one large value. The
        // small one stayed in the SST, which is what the threshold is for.
        BlobFileAdditionInfo blobFile = Assert.Single(flush.BlobFileAdditions);

        Assert.Equal(1UL, blobFile.TotalBlobCount);
        Assert.True(
            blobFile.TotalBlobBytes >= MinBlobSize * 4,
            $"the blob file holds {blobFile.TotalBlobBytes} bytes, less than the value written");

        Assert.EndsWith(".blob", blobFile.BlobFilePath, StringComparison.Ordinal);

        // And both values read back whole, which is the part a caller cares about.
        Assert.Equal(new string('s', 16), db.GetString("small"));
        Assert.Equal(new string('L', MinBlobSize * 4), db.GetString("large"));
    }

    /// <summary>Blob files appear on disk beside the SST files, not inside them.</summary>
    [Fact]
    public void BlobFiles_AreWrittenAsSeparateFiles()
    {
        using var dir = new TempDir();

        using (RocksDb db = RocksDb.Open(BlobOptions(), dir.Path))
        {
            for (int i = 0; i < 20; i++)
            {
                db.Put($"key{i:D3}", new string('v', MinBlobSize * 2));
            }

            db.Flush();
        }

        Assert.NotEmpty(Directory.GetFiles(dir.Path, "*.blob"));
        Assert.NotEmpty(Directory.GetFiles(dir.Path, "*.sst"));
    }

    /// <summary>
    /// Turning blob files off keeps the same values in the SST files, so the
    /// test above is measuring the setting rather than the value size.
    /// </summary>
    [Fact]
    public void WithoutBlobFiles_NothingIsWrittenAsABlob()
    {
        using var dir = new TempDir();

        var options = new DbOptions { CreateIfMissing = true };

        using (RocksDb db = RocksDb.Open(options, dir.Path))
        {
            for (int i = 0; i < 20; i++)
            {
                db.Put($"key{i:D3}", new string('v', MinBlobSize * 2));
            }

            db.Flush();
        }

        Assert.Empty(Directory.GetFiles(dir.Path, "*.blob"));
        Assert.NotEmpty(Directory.GetFiles(dir.Path, "*.sst"));
    }

    /// <summary>
    /// Statistics count the blob file a flush wrote.
    /// </summary>
    /// <remarks>
    /// Which counters move here had to be measured rather than assumed. The
    /// <c>BlobDb</c> prefix covers 49 tickers, and most of them belong to the
    /// older stacked BlobDB rather than the integrated one: this test first
    /// asserted <see cref="Ticker.BlobDbNumKeysRead"/> and would have failed,
    /// because integrated BlobDB never touches it. The ones below are what a
    /// write and a flush were observed to move.
    /// </remarks>
    [Fact]
    public void BlobWrites_AreCounted()
    {
        using var dir = new TempDir();

        DbOptions options = BlobOptions();
        options.EnableStatistics();

        using RocksDb db = RocksDb.Open(options, dir.Path);

        Assert.Equal(0UL, options.GetTickerCount(Ticker.BlobDbBlobFileSynced));

        db.Put("large", new string('L', MinBlobSize * 4));
        db.Flush();

        Assert.Equal(1UL, options.GetTickerCount(Ticker.BlobDbBlobFileSynced));

        Assert.True(
            options.GetTickerCount(Ticker.BlobDbBlobFileBytesWritten) >= MinBlobSize * 4,
            $"only {options.GetTickerCount(Ticker.BlobDbBlobFileBytesWritten)} blob bytes written");

        Assert.Equal(new string('L', MinBlobSize * 4), db.GetString("large"));

        // Stacked BlobDB's counter, which the integrated implementation leaves
        // alone. Pinned because the name reads as though it applies here.
        Assert.Equal(0UL, options.GetTickerCount(Ticker.BlobDbNumKeysRead));
    }

    // ── The blob cache ──────────────────────────────────────────────────────

    /// <summary>
    /// A blob cache can be attached, which nothing could do before: the option
    /// was the one blob binding the wrapper did not reach.
    /// </summary>
    /// <remarks>
    /// <see cref="DbOptions.PrepopulateBlobCache"/> was already wrapped and
    /// could not do anything without this, since there was no cache for a flush
    /// to put its blobs into.
    /// </remarks>
    [Fact]
    public void BlobCache_CanBeAttachedAndServesReads()
    {
        using var dir = new TempDir();

        using var cache = Cache.CreateLru(8 * 1024 * 1024);

        DbOptions options = BlobOptions();
        options.EnableStatistics();
        options.BlobCache = cache;
        options.PrepopulateBlobCache = PrepopulateBlobCache.FlushOnly;

        using RocksDb db = RocksDb.Open(options, dir.Path);

        db.Put("large", new string('L', MinBlobSize * 4));
        db.Flush();

        Assert.Equal(new string('L', MinBlobSize * 4), db.GetString("large"));

        // Prepopulate put the blob in on flush, and the read came back out of
        // the cache rather than off the file. Both halves are asserted because
        // either alone could pass with the cache doing nothing useful.
        Assert.True(
            options.GetTickerCount(Ticker.BlobDbCacheAdd) > 0,
            "the flush did not prepopulate the cache");

        Assert.True(
            options.GetTickerCount(Ticker.BlobDbCacheHit) > 0,
            "the blob cache recorded no hit, so the read did not come from it");
    }

    /// <summary>
    /// Null is refused rather than passed through.
    /// </summary>
    /// <remarks>
    /// The C API dereferences the cache without checking it, unlike the
    /// block-cache setter beside it which ignores a null. So a null here would
    /// be an access violation rather than a no-op, and it is stopped in managed
    /// code.
    /// </remarks>
    [Fact]
    public void BlobCache_RejectsNull()
    {
        using var options = new DbOptions();

        Assert.Throws<ArgumentNullException>(() => options.BlobCache = null!);
    }

    /// <summary>
    /// The cache is shared with RocksDb rather than handed over, so disposing it
    /// while a database still uses it defers rather than freeing it.
    /// </summary>
    [Fact]
    public void BlobCache_IsHeldWhileTheOptionsUseIt()
    {
        using var dir = new TempDir();

        var cache = Cache.CreateLru(8 * 1024 * 1024);

        DbOptions options = BlobOptions();
        options.BlobCache = cache;

        using RocksDb db = RocksDb.Open(options, dir.Path);

        // The caller lets go while the database is open.
        cache.Dispose();
        Assert.False(cache.IsDisposed);

        db.Put("large", new string('L', MinBlobSize * 4));
        db.Flush();

        Assert.Equal(new string('L', MinBlobSize * 4), db.GetString("large"));
    }

    // ── Garbage collection ──────────────────────────────────────────────────

    /// <summary>
    /// The garbage-collection settings round-trip under their full names, which
    /// now match RocksDb and the per-compaction overrides on
    /// <see cref="CompactRangeOptions"/>.
    /// </summary>
    [Fact]
    public void GarbageCollectionSettings_RoundTrip()
    {
        using var options = new DbOptions
        {
            EnableBlobGarbageCollection = true,
            BlobGarbageCollectionAgeCutoff = 0.5,
            BlobGarbageCollectionForceThreshold = 0.75,
        };

        Assert.True(options.EnableBlobGarbageCollection);
        Assert.Equal(0.5, options.BlobGarbageCollectionAgeCutoff);
        Assert.Equal(0.75, options.BlobGarbageCollectionForceThreshold);
    }

    /// <summary>
    /// Overwriting every value and compacting reclaims the blob files the old
    /// values were in.
    /// </summary>
    [Fact]
    public void GarbageCollection_ReclaimsBlobFilesForOverwrittenValues()
    {
        using var dir = new TempDir();

        DbOptions options = BlobOptions();
        options.EnableBlobGarbageCollection = true;

        // Ordinary settings on purpose. The first version of this test used the
        // degenerate pair, an age cutoff of 1.0 with a force threshold of 0.0,
        // to guarantee collection. That means "rewrite any file with any
        // garbage at all", and every rewrite produces another file that
        // immediately qualifies, so compaction never finished and the test hung
        // rather than failed.
        options.BlobGarbageCollectionAgeCutoff = 0.25;
        options.BlobGarbageCollectionForceThreshold = 0.5;

        using RocksDb db = RocksDb.Open(options, dir.Path);

        for (int round = 0; round < 4; round++)
        {
            for (int i = 0; i < 25; i++)
            {
                db.Put($"key{i:D3}", new string((char)('a' + round), MinBlobSize * 2));
            }

            db.Flush();
        }

        int before = Directory.GetFiles(dir.Path, "*.blob").Length;
        Assert.True(before > 1, $"expected several blob files to collect, found {before}");

        db.CompactRange();

        // Bounded. A waiting call with no timeout turns any mistake in the
        // settings above into a hang instead of a failure.
        using var waitOptions = new WaitForCompactOptions { TimeoutMicros = 30_000_000 };
        db.WaitForCompact(waitOptions);

        int after = Directory.GetFiles(dir.Path, "*.blob").Length;

        Assert.True(after < before, $"still {after} blob files, from {before}");

        // The surviving values are the last ones written.
        Assert.Equal(new string('d', MinBlobSize * 2), db.GetString("key010"));
    }
}
