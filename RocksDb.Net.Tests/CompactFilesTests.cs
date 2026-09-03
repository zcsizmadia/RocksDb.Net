using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Covers <see cref="RocksDb.CompactFiles(CompactFilesOptions, IReadOnlyList{string}, int, int)"/>,
/// <see cref="CompactFilesOptions"/>, and
/// <see cref="RocksDb.GetLiveFilesStorageInfo(LiveFilesStorageInfoOptions)"/>.
/// See issue #26.
/// </summary>
public class CompactFilesTests
{
    /// <summary>
    /// Writes two SST files whose key ranges overlap, so a compaction of both
    /// has to merge them rather than trivially moving either.
    /// </summary>
    private static string[] WriteTwoOverlappingSstFiles(RocksDb db)
    {
        db.Put("a", "1");
        db.Put("b", "2");
        db.Flush();
        db.Put("a", "1-updated");
        db.Put("b", "2-updated");
        db.Flush();

        using LiveFiles? live = db.GetLiveFiles();
        Assert.NotNull(live);

        return [.. live!.Files.Select(f => f.Name)];
    }

    // ── CompactFilesOptions ──────────────────────────────────────────────────

    [Fact]
    public void CompactFilesOptions_Properties_RoundTrip()
    {
        using var opts = new CompactFilesOptions();

        opts.Compression = Compression.None;
        opts.OutputFileSizeLimit = 1048576;
        opts.MaxSubcompactions = 2;
        opts.AllowTrivialMove = true;
        opts.OutputTemperatureOverride = Temperature.Warm;

        Assert.Equal(Compression.None, opts.Compression);
        Assert.Equal(1048576UL, opts.OutputFileSizeLimit);
        Assert.Equal(2U, opts.MaxSubcompactions);
        Assert.True(opts.AllowTrivialMove);
        Assert.Equal(Temperature.Warm, opts.OutputTemperatureOverride);
    }

    [Fact]
    public void CompactFilesOptions_CancellationFlag_RoundTrips()
    {
        using var opts = new CompactFilesOptions();
        using var flag = new CompactionCancellationFlag();

        Assert.Null(opts.CancellationFlag);

        opts.CancellationFlag = flag;
        Assert.Same(flag, opts.CancellationFlag);

        opts.CancellationFlag = null;
        Assert.Null(opts.CancellationFlag);
    }

    [Fact]
    public void CompactFilesOptions_DisposedCancellationFlag_Throws()
    {
        using var opts = new CompactFilesOptions();
        var flag = new CompactionCancellationFlag();
        flag.Dispose();

        Assert.Throws<ObjectDisposedException>(() => opts.CancellationFlag = flag);
    }

    [Fact]
    public void CancellationFlag_SetAfterDispose_Throws()
    {
        var flag = new CompactionCancellationFlag();
        flag.Dispose();

        Assert.True(flag.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => flag.Set(true));
    }

    [Fact]
    public void CancellationFlag_DisposeIsIdempotent()
    {
        var flag = new CompactionCancellationFlag();

        flag.Dispose();
        flag.Dispose();

        Assert.True(flag.IsDisposed);
    }

    // ── CompactFiles ─────────────────────────────────────────────────────────

    [Fact]
    public void CompactFiles_MergesTheNamedFilesIntoTheOutputLevel()
    {
        using var db = new TempDb();

        string[] inputs = WriteTwoOverlappingSstFiles(db.Db);
        Assert.Equal(2, inputs.Length);

        using var opts = new CompactFilesOptions { Compression = Compression.None };
        string[] outputs = db.Db.CompactFiles(opts, inputs, outputLevel: 1);

        Assert.Single(outputs);
        Assert.All(outputs, o => Assert.False(string.IsNullOrEmpty(o)));

        // The data survives, and now lives at level 1.
        Assert.Equal("1-updated", db.Db.GetString("a"));
        Assert.Equal("2-updated", db.Db.GetString("b"));

        using ColumnFamilyMetadata? metadata = db.Db.GetColumnFamilyMetadata();
        Assert.NotNull(metadata);
        Assert.Equal(1UL, (ulong)metadata!.FileCount);
    }

    [Fact]
    public void CompactFiles_WithNullOptions_UsesDefaults()
    {
        using var db = new TempDb();

        string[] inputs = WriteTwoOverlappingSstFiles(db.Db);
        string[] outputs = db.Db.CompactFiles(null, inputs, outputLevel: 1);

        Assert.NotEmpty(outputs);
    }

    [Fact]
    public void CompactFiles_ReportsTheJobInfo()
    {
        using var db = new TempDb();

        string[] inputs = WriteTwoOverlappingSstFiles(db.Db);

        using var opts = new CompactFilesOptions();
        string[] outputs = db.Db.CompactFiles(opts, inputs, outputLevel: 1, out CompactionJobInfo? jobInfo);

        Assert.Single(outputs);
        Assert.NotNull(jobInfo);

        // This is the only synchronous way to get a populated CompactionJobInfo.
        CompactionJobStats stats = Assert.IsType<CompactionJobStats>(jobInfo!.Stats);
        Assert.Equal(2UL, stats.NumInputFiles);
        Assert.Equal(1UL, stats.NumOutputFiles);
        Assert.Equal(4UL, stats.NumInputRecords);
        Assert.Equal(2UL, stats.NumOutputRecords);
        Assert.False(jobInfo.Aborted);
        Assert.Equal(2, jobInfo.InputFileInfos.Count);
        Assert.Single(jobInfo.OutputFileInfos);
    }

    [Fact]
    public void CompactFiles_WithColumnFamily_Works()
    {
        using var db = new TempDb();

        using var cfOpts = new DbOptions();
        using ColumnFamilyHandle cf = db.Db.CreateColumnFamily(cfOpts, "extra");

        db.Db.Put("a"u8, "1"u8, cf);
        db.Db.Flush(cf);
        db.Db.Put("a"u8, "1-updated"u8, cf);
        db.Db.Flush(cf);

        using ColumnFamilyMetadata? before = db.Db.GetColumnFamilyMetadata(cf);
        Assert.NotNull(before);

        string[] inputs = [.. before!.Levels.SelectMany(l => l.Files).Select(f => f.RelativeFilename)];

        Assert.Equal(2, inputs.Length);

        using var opts = new CompactFilesOptions();
        string[] outputs = db.Db.CompactFiles(cf, opts, inputs, outputLevel: 1);

        Assert.Single(outputs);
        Assert.Equal("1-updated", Encoding.UTF8.GetString(db.Db.Get("a"u8, cf)!));
    }

    [Fact]
    public void CompactFiles_WhenCancelled_Throws()
    {
        using var db = new TempDb();

        string[] inputs = WriteTwoOverlappingSstFiles(db.Db);

        using var flag = new CompactionCancellationFlag();
        flag.Set(true);

        using var opts = new CompactFilesOptions();
        opts.CancellationFlag = flag;

        // A cancelled compaction fails rather than returning a partial result.
        Assert.Throws<RocksDbException>(() => db.Db.CompactFiles(opts, inputs, outputLevel: 1));

        // The data is untouched.
        Assert.Equal("1-updated", db.Db.GetString("a"));
    }

    [Fact]
    public void CompactFiles_WithNoInputFiles_Throws()
    {
        using var db = new TempDb();

        using var opts = new CompactFilesOptions();

        Assert.Throws<ArgumentException>(() => db.Db.CompactFiles(opts, [], outputLevel: 1));
    }

    [Fact]
    public void CompactFiles_WithNullInputFiles_Throws()
    {
        using var db = new TempDb();

        using var opts = new CompactFilesOptions();

        Assert.Throws<ArgumentNullException>(() => db.Db.CompactFiles(opts, null!, outputLevel: 1));
    }

    [Fact]
    public void CompactFiles_WithUnknownFile_Throws()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Flush();

        using var opts = new CompactFilesOptions();

        Assert.Throws<RocksDbException>(() => db.Db.CompactFiles(opts, ["/000999.sst"], outputLevel: 1));
    }

    // ── GetLiveFilesStorageInfo ──────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveFilesStorageInfoOptions_BoolProperties_RoundTrip(bool value)
    {
        using var opts = new LiveFilesStorageInfoOptions();

        opts.IncludeChecksumInfo = value;
        opts.AtomicFlush = value;

        Assert.Equal(value, opts.IncludeChecksumInfo);
        Assert.Equal(value, opts.AtomicFlush);
    }

    [Fact]
    public void LiveFilesStorageInfoOptions_WalSizeForFlush_RoundTrips()
    {
        using var opts = new LiveFilesStorageInfoOptions();

        opts.WalSizeForFlush = 1048576;
        Assert.Equal(1048576UL, opts.WalSizeForFlush);
    }

    [Fact]
    public void GetLiveFilesStorageInfo_DescribesEveryFileNeededForACopy()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Flush();

        IReadOnlyList<LiveFileStorageInfo> files = db.Db.GetLiveFilesStorageInfo();

        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.False(string.IsNullOrEmpty(f.RelativeFilename)));
        Assert.All(files, f => Assert.False(string.IsNullOrEmpty(f.Directory)));

        // A copy of a live database needs the manifest, the CURRENT pointer, the
        // options file and at least one SST.
        Assert.Contains(files, f => f.FileType == FileType.TableFile);
        Assert.Contains(files, f => f.FileType == FileType.DescriptorFile);
        Assert.Contains(files, f => f.FileType == FileType.CurrentFile);
        Assert.Contains(files, f => f.FileType == FileType.OptionsFile);

        // CURRENT is tiny and RocksDb hands back its content rather than asking
        // the caller to read the file.
        LiveFileStorageInfo current = files.First(f => f.FileType == FileType.CurrentFile);
        Assert.NotEmpty(current.ReplacementContents);

        LiveFileStorageInfo sst = files.First(f => f.FileType == FileType.TableFile);
        Assert.True(sst.Size > 0);
        Assert.True(sst.FileNumber > 0);
    }

    [Fact]
    public void GetLiveFilesStorageInfo_WithChecksums_ReportsThem()
    {
        using var dir = new TempDir();
        using var factory = FileChecksumGenFactory.CreateCrc32c();

        using var dbOpts = new DbOptions { CreateIfMissing = true };
        dbOpts.SetFileChecksumGenFactory(factory);

        using var db = RocksDb.Open(dbOpts, dir.Path);
        db.Put("a", "1");
        db.Flush();

        using var options = new LiveFilesStorageInfoOptions { IncludeChecksumInfo = true };
        IReadOnlyList<LiveFileStorageInfo> files = db.GetLiveFilesStorageInfo(options);

        LiveFileStorageInfo sst = files.First(f => f.FileType == FileType.TableFile);

        Assert.NotEmpty(sst.FileChecksum);
        Assert.False(string.IsNullOrEmpty(sst.FileChecksumFuncName));
    }

    [Fact]
    public void GetLiveFilesStorageInfo_WithoutChecksums_LeavesThemEmpty()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Flush();

        using var options = new LiveFilesStorageInfoOptions { IncludeChecksumInfo = false };
        IReadOnlyList<LiveFileStorageInfo> files = db.Db.GetLiveFilesStorageInfo(options);

        Assert.All(files, f => Assert.Empty(f.FileChecksum));
    }

    /// <summary>
    /// The WAL is still being appended to, so its copy has to be truncated to
    /// the reported size. That is what TrimToSize is for.
    /// </summary>
    [Fact]
    public void GetLiveFilesStorageInfo_MarksTheLiveWalForTrimming()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");

        using var options = new LiveFilesStorageInfoOptions { WalSizeForFlush = ulong.MaxValue };
        IReadOnlyList<LiveFileStorageInfo> files = db.Db.GetLiveFilesStorageInfo(options);

        Assert.Contains(files, f => f.FileType == FileType.WalFile && f.TrimToSize);
    }
}
