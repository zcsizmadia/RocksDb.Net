using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Covers the identifying and descriptive fields on the event-listener info
/// records, and <see cref="ColumnFamilyMetadataOptions"/>. See issue #25.
/// </summary>
public class ListenerDetailTests
{
    // ── FlushJobInfo ─────────────────────────────────────────────────────────

    [Fact]
    public void FlushJobInfo_IdentifiesTheJobAndFile()
    {
        using var dir = new TempDir();
        var listener = new RecordingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("a", "1");
            db.Flush();
        }

        FlushJobInfo flush = Assert.Single(listener.FlushCompleted);

        Assert.True(flush.JobId > 0);
        Assert.True(flush.ThreadId > 0);
        Assert.Equal(0U, flush.ColumnFamilyId); // The default column family is 0.
        Assert.True(flush.FileNumber > 0);
        Assert.Equal(0UL, flush.OldestBlobFileNumber); // No blob files here.
    }

    [Fact]
    public void FlushJobInfo_ReportsOldestBlobFileNumberWhenBlobsAreUsed()
    {
        using var dir = new TempDir();
        var listener = new RecordingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;
        opts.EnableBlobs();

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("a", "a-value-large-enough-for-a-blob");
            db.Flush();
        }

        FlushJobInfo flush = Assert.Single(listener.FlushCompleted);

        Assert.True(flush.OldestBlobFileNumber > 0);
        Assert.Single(flush.BlobFileAdditions);
    }

    // ── CompactionJobInfo ────────────────────────────────────────────────────

    [Fact]
    public void CompactionJobInfo_IdentifiesTheJobAndItsFiles()
    {
        using var dir = new TempDir();
        var listener = new RecordingListener();

        using var opts = new DbOptions { CreateIfMissing = true, Compression = Compression.None };
        opts.EventListener = listener;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            // Overlapping keys in both files force a real merge rather than a
            // trivial move, which would leave most of this unpopulated.
            db.Put("a", "1");
            db.Put("b", "2");
            db.Flush();
            db.Put("a", "1-updated");
            db.Put("b", "2-updated");
            db.Flush();

            db.CompactRange();
        }

        Assert.NotEmpty(listener.CompactionCompleted);
        CompactionJobInfo compaction = listener.CompactionCompleted[0];

        Assert.True(compaction.JobId > 0);
        Assert.True(compaction.ThreadId > 0);
        Assert.Equal(0U, compaction.ColumnFamilyId);
        Assert.False(compaction.Aborted);
        Assert.Equal(Compression.None, compaction.Compression);

        // RocksDb documents num_l0_files as the level-0 count "right before and
        // after" the compaction. A full CompactRange drains level 0, and the
        // value observed here is 0, so it is reporting the post-compaction count.
        Assert.Equal(0, compaction.NumL0Files);

        Assert.Equal(2, compaction.InputFileInfos.Count);
        Assert.Single(compaction.OutputFileInfos);
        Assert.All(compaction.InputFileInfos, f => Assert.True(f.FileNumber > 0));
        Assert.All(compaction.InputFileInfos, f => Assert.Equal(0, f.Level));
        Assert.All(compaction.OutputFileInfos, f => Assert.True(f.Level > 0));
    }

    [Fact]
    public void CompactionJobInfo_TablePropertiesByFile_CoversInputsAndOutputs()
    {
        using var dir = new TempDir();
        var listener = new RecordingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("a", "1");
            db.Put("b", "2");
            db.Flush();
            db.Put("a", "1-updated");
            db.Put("b", "2-updated");
            db.Flush();

            db.CompactRange();
        }

        CompactionJobInfo compaction = listener.CompactionCompleted[0];

        // Two inputs plus one output.
        Assert.Equal(3, compaction.TablePropertiesByFile.Count);
        Assert.All(compaction.TablePropertiesByFile.Keys, k => Assert.False(string.IsNullOrEmpty(k)));
        Assert.All(compaction.TablePropertiesByFile.Values, p => Assert.True(p.NumEntries > 0));

        // The keys line up with the file lists RocksDb also reports.
        foreach (string inputFile in compaction.InputFiles)
        {
            Assert.Contains(inputFile, compaction.TablePropertiesByFile.Keys);
        }
    }

    // ── ExternalFileIngestionInfo ────────────────────────────────────────────

    [Fact]
    public void ExternalFileIngestionInfo_ReportsTheSourcePathAndProperties()
    {
        using var dir = new TempDir();
        string sstPath = Path.Combine(dir.Path, "ingest.sst");

        using (var writerOpts = new DbOptions())
        using (var writer = SstFileWriter.Create(writerOpts))
        {
            writer.Open(sstPath);
            writer.Put("a"u8, "1"u8);
            writer.Put("b"u8, "2"u8);
            writer.Finish();
        }

        var listener = new RecordingListener();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using (var db = RocksDb.Open(opts, dir.Sub("db")))
        {
            using var ingestOpts = new IngestExternalFileOptions();
            db.IngestExternalFile([sstPath], ingestOpts);
        }

        ExternalFileIngestionInfo ingestion = Assert.Single(listener.Ingested);

        Assert.Equal(sstPath, ingestion.ExternalFilePath);
        Assert.False(string.IsNullOrEmpty(ingestion.InternalFilePath));

        TableProperties props = Assert.IsType<TableProperties>(ingestion.TableProperties);
        Assert.Equal(2UL, props.NumEntries);
    }

    // ── MemTableInfo ─────────────────────────────────────────────────────────

    [Fact]
    public void MemTableInfo_NewestUdt_IsEmptyWithoutUserDefinedTimestamps()
    {
        using var dir = new TempDir();
        var listener = new RecordingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("a", "1");
            db.Flush();
        }

        // Sealing happens on flush. The column family has no user-defined
        // timestamps, so RocksDb reports none.
        Assert.NotEmpty(listener.MemTablesSealed);
        Assert.All(listener.MemTablesSealed, m => Assert.Empty(m.NewestUdt));
    }

    // ── ColumnFamilyMetadataOptions ──────────────────────────────────────────

    [Fact]
    public void ColumnFamilyMetadataOptions_Level_RoundTrips()
    {
        using var opts = new ColumnFamilyMetadataOptions();

        opts.Level = 2;
        Assert.Equal(2, opts.Level);
    }

    [Fact]
    public void ColumnFamilyMetadataOptions_Keys_RoundTrip()
    {
        using var opts = new ColumnFamilyMetadataOptions();

        Assert.Null(opts.StartKey);
        Assert.Null(opts.EndKey);

        opts.SetStartKey("a"u8);
        opts.SetEndKey("z"u8);

        Assert.Equal("a"u8.ToArray(), opts.StartKey);
        Assert.Equal("z"u8.ToArray(), opts.EndKey);
    }

    [Fact]
    public void ColumnFamilyMetadataOptions_KeysAreCopiedByRocksDb()
    {
        using var opts = new ColumnFamilyMetadataOptions();

        // RocksDb copies these into a std::string, so a caller buffer that goes
        // away must not affect the stored value.
        opts.SetStartKey(Encoding.UTF8.GetBytes("start-key"));

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        Assert.Equal(Encoding.UTF8.GetBytes("start-key"), opts.StartKey);
    }

    [Fact]
    public void GetColumnFamilyMetadata_WithOptions_ReportsTheWholeColumnFamily()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Flush();

        using var options = new ColumnFamilyMetadataOptions();
        using ColumnFamilyMetadata? metadata = db.Db.GetColumnFamilyMetadata(options);

        Assert.NotNull(metadata);
        Assert.Equal("default", metadata!.Name);
        Assert.True(metadata.FileCount > 0);
    }

    [Fact]
    public void GetColumnFamilyMetadata_WithLevelFilter_NarrowsTheResult()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Flush();

        using var allLevels = new ColumnFamilyMetadataOptions();
        using ColumnFamilyMetadata? everything = db.Db.GetColumnFamilyMetadata(allLevels);

        // The single flushed file lives in level 0, so asking for a level that
        // has no files must report none.
        using var emptyLevel = new ColumnFamilyMetadataOptions { Level = 5 };
        using ColumnFamilyMetadata? narrowed = db.Db.GetColumnFamilyMetadata(emptyLevel);

        Assert.NotNull(everything);
        Assert.NotNull(narrowed);
        Assert.True(everything!.FileCount > 0);
        Assert.Equal(0UL, (ulong)narrowed!.FileCount);
    }

    [Fact]
    public void GetColumnFamilyMetadata_WithColumnFamilyAndOptions_Works()
    {
        using var db = new TempDb();

        using var cfOpts = new DbOptions();
        using ColumnFamilyHandle cf = db.Db.CreateColumnFamily(cfOpts, "extra");

        db.Db.Put("a"u8, "1"u8, cf);
        db.Db.Flush(cf);

        using var options = new ColumnFamilyMetadataOptions();
        using ColumnFamilyMetadata? metadata = db.Db.GetColumnFamilyMetadata(cf, options);

        Assert.NotNull(metadata);
        Assert.Equal("extra", metadata!.Name);
    }

    [Fact]
    public void GetColumnFamilyMetadata_WithNullOptions_Throws()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(
            () => db.Db.GetColumnFamilyMetadata((ColumnFamilyMetadataOptions)null!));
    }
}
