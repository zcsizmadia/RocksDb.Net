using System.Globalization;
using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Covers background-work control, the integrity checks, and size approximation
/// with options. See issue #26.
/// </summary>
public class DatabaseOperationsTests
{
    // ── Pause / continue background work ─────────────────────────────────────

    [Fact]
    public void PauseAndContinueBackgroundWork_RoundTrip()
    {
        using var db = new TempDb();

        db.Db.PauseBackgroundWork();
        db.Db.ContinueBackgroundWork();
    }

    /// <summary>
    /// While paused, RocksDb starts no flushes, so a listener sees nothing until
    /// work is resumed.
    /// </summary>
    /// <remarks>
    /// The write buffer is set at 64 KB rather than below it. RocksDb clamps
    /// write_buffer_size to a 64 KB floor, so the 4 KB this test used to ask
    /// for became 64 KB, and the 200 small puts never filled it. No flush was
    /// ever scheduled, and the assertion that none completed held whether or
    /// not pausing did anything at all.
    /// </remarks>
    [Fact]
    public void PauseBackgroundWork_PreventsBackgroundFlush()
    {
        using var dir = new TempDir();
        var listener = new CountingFlushListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        // The floor RocksDb enforces. Asking for less does not get less.
        opts.WriteBufferSize = 64 * 1024;

        // Room for the memtables that pile up while nothing may flush them.
        // At the default of two, writes would stall instead.
        opts.MaxWriteBufferNumber = 8;

        using var db = RocksDb.Open(opts, dir.Path);

        db.PauseBackgroundWork();
        try
        {
            // Several times the write buffer, so memtables are filled and
            // switched, and flushes are genuinely due.
            for (int i = 0; i < 1200; i++)
            {
                db.Put($"key{i:D5}", new string('v', 256));
            }

            // The half the old test was missing: work is waiting to be done.
            // Without this, zero completed flushes proves nothing.
            int immutable = int.Parse(
                db.GetProperty("rocksdb.num-immutable-mem-table") ?? "0",
                CultureInfo.InvariantCulture);

            Assert.True(immutable > 0, $"expected memtables waiting to flush, found {immutable}");
            Assert.Equal(0, listener.Count);
        }
        finally
        {
            db.ContinueBackgroundWork();
        }

        // And once resumed, the work that was waiting actually happens.
        Assert.True(
            Wait.Until(() => listener.Count > 0),
            "no flush completed after background work resumed");
    }

    private sealed class CountingFlushListener : EventListener
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override void OnFlushCompleted(FlushJobInfo info)
            => Interlocked.Increment(ref _count);
    }

    [Fact]
    public void PauseBackgroundWork_Nests()
    {
        using var db = new TempDb();

        // Each pause needs its own continue before work resumes.
        db.Db.PauseBackgroundWork();
        db.Db.PauseBackgroundWork();
        db.Db.ContinueBackgroundWork();
        db.Db.ContinueBackgroundWork();
    }

    // ── VerifyChecksum ───────────────────────────────────────────────────────

    [Fact]
    public void VerifyChecksum_OnHealthyDatabase_Succeeds()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Flush();

        db.Db.VerifyChecksum();
    }

    [Fact]
    public void VerifyChecksum_WithOptions_Succeeds()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Flush();

        using var readOpts = new ReadOptions { VerifyChecksums = true };
        db.Db.VerifyChecksum(readOpts);
    }

    [Fact]
    public void VerifyChecksum_WithNullOptions_Throws()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.VerifyChecksum(null!));
    }

    /// <summary>
    /// The check has to actually read the data, so a corrupted SST must fail it.
    /// </summary>
    [Fact]
    public void VerifyChecksum_OnCorruptedSst_Throws()
    {
        using var dir = new TempDir();

        using (var opts = new DbOptions { CreateIfMissing = true })
        using (var db = RocksDb.Open(opts, dir.Path))
        {
            for (int i = 0; i < 100; i++)
            {
                db.Put($"key{i:D5}", $"value{i}");
            }

            db.Flush();
        }

        string sst = Directory.GetFiles(dir.Path, "*.sst").Single();
        CorruptMiddleOfFile(sst);

        using var reopenOpts = new DbOptions { CreateIfMissing = true, ParanoidChecks = false };
        using var reopened = RocksDb.Open(reopenOpts, dir.Path);

        Assert.Throws<RocksDbException>(reopened.VerifyChecksum);
    }

    private static void CorruptMiddleOfFile(string path)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);

        // Flip bits in the data region rather than the footer, so the file still
        // opens and the corruption is only found by reading blocks.
        stream.Seek(stream.Length / 4, SeekOrigin.Begin);
        int b = stream.ReadByte();
        stream.Seek(stream.Length / 4, SeekOrigin.Begin);
        stream.WriteByte((byte)~b);
    }

    // ── VerifyFileChecksums ──────────────────────────────────────────────────

    /// <summary>
    /// RocksDb refuses the check outright when nothing recorded the checksums,
    /// rather than passing vacuously. Worth pinning down, because it makes
    /// VerifyFileChecksums useless unless a generator is configured.
    /// </summary>
    [Fact]
    public void VerifyFileChecksums_WithoutAGenerator_Throws()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Flush();

        RocksDbException ex = Assert.Throws<RocksDbException>(db.Db.VerifyFileChecksums);
        Assert.Contains("file_checksum_gen_factory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyFileChecksums_WithAGenerator_Succeeds()
    {
        using var dir = new TempDir();
        using var factory = FileChecksumGenFactory.CreateCrc32c();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetFileChecksumGenFactory(factory);

        using var db = RocksDb.Open(opts, dir.Path);
        db.Put("a", "1");
        db.Put("b", "2");
        db.Flush();

        db.VerifyFileChecksums();

        using var readOpts = new ReadOptions();
        db.VerifyFileChecksums(readOpts);
    }

    [Fact]
    public void SetFileChecksumGenFactory_DoesNotTakeOwnership()
    {
        using var opts = new DbOptions();

        // RocksDb copies the shared_ptr, so the caller still owns the factory.
        using var factory = FileChecksumGenFactory.CreateCrc32c();
        opts.SetFileChecksumGenFactory(factory);

        Assert.False(factory.IsDisposed);
    }

    [Fact]
    public void SetFileChecksumGenFactory_WithNull_Throws()
    {
        using var opts = new DbOptions();

        Assert.Throws<ArgumentNullException>(() => opts.SetFileChecksumGenFactory(null!));
    }

    [Fact]
    public void VerifyFileChecksums_WithNullOptions_Throws()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.VerifyFileChecksums(null!));
    }

    // ── WriteBatch.VerifyChecksum ────────────────────────────────────────────

    [Fact]
    public void WriteBatch_VerifyChecksum_OnValidBatch_Succeeds()
    {
        using var batch = new WriteBatch();

        batch.Put("a"u8, "1"u8);
        batch.Put("b"u8, "2"u8);

        batch.VerifyChecksum();
    }

    [Fact]
    public void WriteBatch_VerifyChecksum_OnEmptyBatch_Succeeds()
    {
        using var batch = new WriteBatch();

        batch.VerifyChecksum();
    }

    // ── SizeApproximationOptions ─────────────────────────────────────────────

    /// <summary>
    /// Each of these round-trips both ways on its own, without moving any of
    /// the others.
    /// </summary>
    [Fact]
    public void SizeApproximationOptions_BoolProperties_RoundTrip()
    {
        using var opts = new SizeApproximationOptions();

        BoolProperty.AssertRoundTripsIndependently(
            opts,
            (nameof(opts.IncludeMemtables), (o, v) => o.IncludeMemtables = v, o => o.IncludeMemtables),
            (nameof(opts.IncludeFiles), (o, v) => o.IncludeFiles = v, o => o.IncludeFiles),
            (nameof(opts.IncludeBlobFiles), (o, v) => o.IncludeBlobFiles = v, o => o.IncludeBlobFiles));
    }

    [Fact]
    public void SizeApproximationOptions_FilesSizeErrorMargin_RoundTrips()
    {
        using var opts = new SizeApproximationOptions();

        opts.FilesSizeErrorMargin = 0.1;
        Assert.Equal(0.1, opts.FilesSizeErrorMargin);
    }

    /// <summary>
    /// The point of the options overload: by default only SST files count, so
    /// unflushed data estimates as zero until memtables are included.
    /// </summary>
    [Fact]
    public void ApproximateSizes_IncludeMemtables_CountsUnflushedData()
    {
        using var db = new TempDb();

        for (int i = 0; i < 200; i++)
        {
            db.Db.Put($"key{i:D5}", new string('v', 512));
        }

        // Deliberately not flushed.
        (string, string)[] ranges = [("key00000", "key99999")];

        using var filesOnly = new SizeApproximationOptions { IncludeMemtables = false };
        using var withMemtables = new SizeApproximationOptions { IncludeMemtables = true };

        ulong[] withoutMemtables = db.Db.ApproximateSizes(filesOnly, ranges);
        ulong[] including = db.Db.ApproximateSizes(withMemtables, ranges);

        Assert.Equal(0UL, withoutMemtables[0]);
        Assert.True(including[0] > 0);
    }

    [Fact]
    public void ApproximateSizes_WithOptions_MatchesTheDefaultAfterFlush()
    {
        using var db = new TempDb();

        for (int i = 0; i < 200; i++)
        {
            db.Db.Put($"key{i:D5}", new string('v', 512));
        }

        db.Db.Flush();

        (string, string)[] ranges = [("key00000", "key99999")];

        using var options = new SizeApproximationOptions();
        ulong[] withOptions = db.Db.ApproximateSizes(options, ranges);
        ulong[] withoutOptions = db.Db.ApproximateSizes(ranges);

        Assert.True(withOptions[0] > 0);
        Assert.True(withoutOptions[0] > 0);
    }

    [Fact]
    public void ApproximateSizes_WithColumnFamilyAndOptions_Works()
    {
        using var db = new TempDb();

        using var cfOpts = new DbOptions();
        using ColumnFamilyHandle cf = db.Db.CreateColumnFamily(cfOpts, "extra");

        for (int i = 0; i < 100; i++)
        {
            db.Db.Put(Encoding.UTF8.GetBytes($"key{i:D5}"), Encoding.UTF8.GetBytes(new string('v', 512)), cf);
        }

        db.Db.Flush(cf);

        using var options = new SizeApproximationOptions();
        ulong[] sizes = db.Db.ApproximateSizes(cf, options, [("key00000", "key99999")]);

        Assert.Single(sizes);
        Assert.True(sizes[0] > 0);
    }

    [Fact]
    public void ApproximateSizes_WithOptions_EmptyRanges_ReturnsEmpty()
    {
        using var db = new TempDb();

        using var options = new SizeApproximationOptions();

        Assert.Empty(db.Db.ApproximateSizes(options, []));
    }

    [Fact]
    public void ApproximateSizes_WithNullOptions_Throws()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(
            () => db.Db.ApproximateSizes((SizeApproximationOptions)null!, [("a", "z")]));
    }

    /// <summary>
    /// All four overloads share one pinning helper now, so this covers the two
    /// that existed before alongside the new ones.
    /// </summary>
    [Fact]
    public void ApproximateSizes_AllOverloadsAgreeOnAFlushedDatabase()
    {
        using var db = new TempDb();

        for (int i = 0; i < 200; i++)
        {
            db.Db.Put($"key{i:D5}", new string('v', 512));
        }

        db.Db.Flush();

        (string, string)[] ranges = [("key00000", "key99999")];
        using var options = new SizeApproximationOptions();
        ColumnFamilyHandle defaultCf = db.Db.GetDefaultColumnFamily();

        Assert.True(db.Db.ApproximateSizes(ranges)[0] > 0);
        Assert.True(db.Db.ApproximateSizes(defaultCf, ranges)[0] > 0);
        Assert.True(db.Db.ApproximateSizes(options, ranges)[0] > 0);
        Assert.True(db.Db.ApproximateSizes(defaultCf, options, ranges)[0] > 0);
    }
}
