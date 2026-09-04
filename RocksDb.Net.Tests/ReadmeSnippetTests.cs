using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// The code from the README, compiled and run.
/// </summary>
/// <remarks>
/// <para>
/// The four guides under <c>docs/articles</c> have had this since #117. The
/// README did not, and it showed: it used <c>entry.CurrentKey</c> where the
/// member is <c>Key</c>, a <c>readOptions:</c> argument name where the parameter
/// is <c>options</c>, a <c>using</c> on a <c>GetLiveFiles()</c> result that is
/// not disposable and has no <c>Files</c> property, an <c>IngestExternalFile</c>
/// without its required options, <c>BackupEngine.Open</c> and
/// <c>RestoreDbFromLatestBackup</c> overloads that do not exist, and string
/// overloads on <c>SstFileWriter</c> that do not exist. It also opened a
/// database with options it had put in a <c>using</c>, which
/// <c>getting-started.md</c> tells readers not to do.
/// </para>
/// <para>
/// The code here is kept identical to what the README shows, apart from paths,
/// which point at an in-memory environment, or at a temporary directory where
/// the test needs real files, rather than <c>"mydb"</c>. If you change the
/// README, change the matching test with it.
/// </para>
/// </remarks>
public class ReadmeSnippetTests
{
    [Fact]
    public void BasicUsage()
    {
        // No `using` on the options: Open takes ownership of them.
        var options = new DbOptions { CreateIfMissing = true };
        string path = TestDb.InMemory(options);
        using var db = RocksDb.Open(options, path);

        // Write
        db.Put("hello", "world");

        // Read
        string? value = db.GetString("hello");
        Assert.Equal("world", value);

        // Delete
        db.Delete("hello");

        Assert.Null(db.GetString("hello"));
    }

    [Fact]
    public void Iteration()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Put("b", "2");

        var seen = new List<string>();

        using var iterator = db.Db.NewIterator();
        iterator.SeekToFirst();

        foreach (var entry in iterator)
        {
            // Spans into the iterator's own buffers, valid until it moves.
            seen.Add($"{Encoding.UTF8.GetString(entry.Key)} = {Encoding.UTF8.GetString(entry.Value)}");
        }

        Assert.Equal(["a = 1", "b = 2"], seen);
    }

    [Fact]
    public void ColumnFamilies()
    {
        using var dir = new TempDir();

        var options = new DbOptions
        {
            CreateIfMissing = true,
            CreateMissingColumnFamilies = true
        };

        var descriptors = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("logs"),
            new("metrics")
        };

        using var db = RocksDb.Open(options, dir.Path, descriptors);

        var logsCf = db.GetColumnFamily("logs");
        db.Put("entry1", "data", logsCf);

        Assert.Equal("data", db.GetString("entry1", logsCf));
    }

    [Fact]
    public void WriteBatchIsAtomic()
    {
        using var db = new TempDb();

        db.Db.Put("old_key", "gone");

        using var batch = new WriteBatch();
        batch.Put("key1", "val1")
             .Put("key2", "val2")
             .Delete("old_key");

        db.Db.Write(batch);

        Assert.Equal("val1", db.Db.GetString("key1"));
        Assert.Equal("val2", db.Db.GetString("key2"));
        Assert.Null(db.Db.GetString("old_key"));
    }

    [Fact]
    public void Snapshots()
    {
        using var db = new TempDb();

        db.Db.Put("key", "before");

        using var snapshot = db.Db.NewSnapshot();
        using var readOpts = new ReadOptions();
        readOpts.SetSnapshot(snapshot);

        db.Db.Put("key", "after");

        // Reads see the database state at snapshot time
        string? val = db.Db.GetString("key", options: readOpts);

        Assert.Equal("before", val);
    }

    [Fact]
    public void MergeOperators()
    {
        // Built-in UInt64 addition
        var options = new DbOptions { CreateIfMissing = true };
        options.SetUInt64AddMergeOperator();
        string path = TestDb.InMemory(options);
        using var db = RocksDb.Open(options, path);

        db.Merge("visits"u8, BitConverter.GetBytes(1UL));
        db.Merge("visits"u8, BitConverter.GetBytes(5UL));

        ulong total = BitConverter.ToUInt64(db.Get("visits"u8));
        // total == 6

        Assert.Equal(6UL, total);
    }

    [Fact]
    public void MetadataAndStatistics()
    {
        // Statistics live on the options, so keep a reference to read them back.
        // These are the options the database owns; do not dispose them yourself.
        var options = new DbOptions { CreateIfMissing = true };
        options.EnableStatistics();

        string path = TestDb.InMemory(options);
        using var db = RocksDb.Open(options, path);

        db.Put("a", "1");
        db.Flush();

        var metadata = db.GetColumnFamilyMetadata();
        Assert.Equal("default", metadata?.Name); // "default"

        var histogram = options.GetHistogramData(Histogram.DbWrite);
        Assert.NotNull(histogram);
    }

    [Fact]
    public void LiveFilesAndApproximateSizes()
    {
        using var dir = new TempDir();

        using var db = RocksDb.Open(new DbOptions { CreateIfMissing = true }, dir.Path);

        db.Put("a", "1");
        db.Put("z", "2");
        db.Flush();

        // Read in full and copied out, so there is nothing to dispose.
        IReadOnlyList<LiveFileMetadata> liveFiles = db.GetLiveFiles();
        Assert.NotEmpty(liveFiles);

        ulong[] sizes = db.ApproximateSizes(new[] { ("a", "z") });
        Assert.Single(sizes);

        ulong[] cfSizes = db.ApproximateSizes(db.GetDefaultColumnFamily(), new[] { ("a", "z") });
        Assert.Single(cfSizes);
    }

    [Fact]
    public void AdvancedMaintenanceHelpers()
    {
        using var dir = new TempDir();

        using var db = RocksDb.Open(new DbOptions { CreateIfMissing = true }, dir.Path);

        db.Put("b", "1");

        using var compactOpts = new WaitForCompactOptions { Flush = true, TimeoutMicros = 5_000_000 };
        db.SuggestCompactRange(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("z"));
        db.DeleteFilesInRange("a", "z");
        db.WaitForCompact(compactOpts);

        // Last, and not before WaitForCompact: cancelling puts the database into
        // shutdown, and waiting after that fails with "Shutdown in progress".
        db.CancelAllBackgroundWork(wait: false);
    }

    [Fact]
    public void BackupAndRestore()
    {
        using var dir = new TempDir();
        using var backupDir = new TempDir();
        using var restoreDir = new TempDir();

        using var db = RocksDb.Open(new DbOptions { CreateIfMissing = true }, dir.Sub("source"));

        db.Put("key", "value");

        // The options say how to reach the database being backed up; the path is
        // where the backups go.
        using var backupOptions = new DbOptions();
        using var engine = BackupEngine.Open(backupOptions, backupDir.Path);

        engine.CreateNewBackup(db);

        // Later: restore, into a database directory and a WAL directory.
        engine.RestoreDbFromLatestBackup(restoreDir.Sub("restored"), restoreDir.Sub("restored"));

        using var restored = RocksDb.Open(new DbOptions(), restoreDir.Sub("restored"));
        Assert.Equal("value", restored.GetString("key"));
    }

    [Fact]
    public void SstFileIngestion()
    {
        using var dir = new TempDir();
        using var db = TempDb.OnDisk();

        string sstPath = Path.Combine(dir.Path, "data.sst");

        using var envOpts = new EnvOptions();
        using var dbOpts = new DbOptions();
        using var writer = SstFileWriter.Create(envOpts, dbOpts);

        writer.Open(sstPath);

        // Keys and values are bytes here, and must be in sorted order.
        writer.Put("key1"u8, "val1"u8);
        writer.Put("key2"u8, "val2"u8);
        writer.Finish();

        using var ingestOptions = new IngestExternalFileOptions();
        db.Db.IngestExternalFile(new[] { sstPath }, ingestOptions);

        Assert.Equal("val1", db.Db.GetString("key1"));
        Assert.Equal("val2", db.Db.GetString("key2"));
    }

    [Fact]
    public void BloomFilters()
    {
        using var tableOptions = new BlockBasedTableOptions();
        tableOptions.SetFilterPolicy(FilterPolicy.CreateBloomFull(10));

        var options = new DbOptions { CreateIfMissing = true };
        options.BlockBasedTableFactory = tableOptions;

        string path = TestDb.InMemory(options);
        using var db = RocksDb.Open(options, path);

        db.Put("key", "value");
        Assert.Equal("value", db.GetString("key"));
    }

    [Fact]
    public void LargeValuesBlobDb()
    {
        using var dir = new TempDir();

        var options = new DbOptions
        {
            CreateIfMissing = true,
            EnableBlobFiles = true,
            MinBlobSize = 1024,          // values at or above this go to a blob file

            // Reclaim space from blob files whose values have been overwritten.
            EnableBlobGarbageCollection = true,
        };

        // Blobs live outside the SST files, so the block cache never holds them.
        // Without a blob cache, every blob read goes to the file system.
        using var blobCache = Cache.CreateLru(256 * 1024 * 1024);
        options.BlobCache = blobCache;
        options.PrepopulateBlobCache = PrepopulateBlobCache.FlushOnly;

        using var db = RocksDb.Open(options, dir.Path);

        db.Put("large", new string('v', 4096));   // stored as a blob
        db.Put("small", "v");                     // stays in the SST

        db.Flush();

        Assert.NotEmpty(Directory.GetFiles(dir.Path, "*.blob"));
        Assert.Equal(new string('v', 4096), db.GetString("large"));
        Assert.Equal("v", db.GetString("small"));
    }
}
