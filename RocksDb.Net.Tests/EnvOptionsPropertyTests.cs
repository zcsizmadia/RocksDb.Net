using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Covers the <see cref="EnvOptions"/> properties, and
/// the <see cref="SstFileWriter"/> overload that finally gives the class a
/// consumer. See issue #25.
/// </summary>
public class EnvOptionsPropertyTests
{
    /// <summary>
    /// Each of these round-trips both ways on its own, without moving any of
    /// the others.
    /// </summary>
    [Fact]
    public void BoolProperties_RoundTrip()
    {
        using var opts = new EnvOptions();

        BoolProperty.AssertRoundTripsIndependently(
            opts,
            (nameof(opts.UseDirectReads), (o, v) => o.UseDirectReads = v, o => o.UseDirectReads),
            (nameof(opts.UseDirectWrites), (o, v) => o.UseDirectWrites = v, o => o.UseDirectWrites),
            (nameof(opts.UseMmapReads), (o, v) => o.UseMmapReads = v, o => o.UseMmapReads),
            (nameof(opts.UseMmapWrites), (o, v) => o.UseMmapWrites = v, o => o.UseMmapWrites),
            (nameof(opts.AllowFallocate), (o, v) => o.AllowFallocate = v, o => o.AllowFallocate),
            (nameof(opts.FallocateWithKeepSize), (o, v) => o.FallocateWithKeepSize = v, o => o.FallocateWithKeepSize),
            (nameof(opts.FdCloexec), (o, v) => o.FdCloexec = v, o => o.FdCloexec),
            (nameof(opts.StrictBytesPerSync), (o, v) => o.StrictBytesPerSync = v, o => o.StrictBytesPerSync));
    }

    [Fact]
    public void NumericProperties_RoundTrip()
    {
        using var opts = new EnvOptions();

        opts.BytesPerSync = 1048576;
        opts.CompactionReadaheadSize = 2097152;
        opts.WritableFileMaxBufferSize = 524288;

        Assert.Equal(1048576UL, opts.BytesPerSync);
        Assert.Equal(2097152UL, opts.CompactionReadaheadSize);
        Assert.Equal(524288UL, opts.WritableFileMaxBufferSize);
    }

    [Fact]
    public void SetRateLimiter_DoesNotTakeOwnership()
    {
        using var opts = new EnvOptions();

        // RocksDb copies the shared_ptr rather than taking ownership, so the
        // caller keeps it and this using block is correct.
        using var limiter = new RateLimiter(1048576);
        opts.SetRateLimiter(limiter);

        Assert.False(limiter.IsDisposed);

        opts.SetRateLimiter(null);
    }

    // ── SstFileWriter consumer ───────────────────────────────────────────────

    /// <summary>
    /// Before this overload existed, <see cref="EnvOptions"/> had no consumer at
    /// all: <see cref="SstFileWriter.Create(DbOptions)"/> built and discarded its
    /// own native environment options.
    /// </summary>
    [Fact]
    public void SstFileWriter_WithEnvOptions_WritesAnIngestibleFile()
    {
        using var dir = new TempDir();
        string sstPath = Path.Combine(dir.Path, "written.sst");

        using var dbOpts = new DbOptions { CreateIfMissing = true };
        using var envOpts = new EnvOptions
        {
            BytesPerSync = 65536,
            WritableFileMaxBufferSize = 262144,
        };

        using (var writer = SstFileWriter.Create(envOpts, dbOpts))
        {
            writer.Open(sstPath);
            writer.Put("a"u8, "1"u8);
            writer.Put("b"u8, "2"u8);
            writer.Finish();
        }

        Assert.True(File.Exists(sstPath));

        // The file is only proven good once RocksDb accepts it.
        using var db = RocksDb.Open(dbOpts, dir.Sub("db"));
        using var ingestOpts = new IngestExternalFileOptions();
        db.IngestExternalFile([sstPath], ingestOpts);

        Assert.Equal("1", db.GetString("a"));
        Assert.Equal("2", db.GetString("b"));
    }

    [Fact]
    public void SstFileWriter_WithNullEnvOptions_Throws()
    {
        using var dbOpts = new DbOptions();

        Assert.Throws<ArgumentNullException>(() => SstFileWriter.Create(null!, dbOpts));
    }

    [Fact]
    public void SstFileWriter_WithNullDbOptions_Throws()
    {
        using var envOpts = new EnvOptions();

        Assert.Throws<ArgumentNullException>(() => SstFileWriter.Create(envOpts, null!));
    }

    /// <summary>
    /// The overload reads both option objects and does not retain them, so
    /// disposing them straight afterwards must leave the writer usable.
    /// </summary>
    [Fact]
    public void SstFileWriter_OptionsCanBeDisposedAfterCreate()
    {
        using var dir = new TempDir();
        string sstPath = Path.Combine(dir.Path, "written.sst");

        var dbOpts = new DbOptions { CreateIfMissing = true };
        var envOpts = new EnvOptions();

        using var writer = SstFileWriter.Create(envOpts, dbOpts);
        envOpts.Dispose();

        writer.Open(sstPath);
        writer.Put("a"u8, "1"u8);
        writer.Finish();

        dbOpts.Dispose();

        Assert.True(File.Exists(sstPath));
    }
}
