using System.Text;

namespace RocksDbNet.Tests;

public class SstFileWriterTests
{
    [Fact]
    public void WriteAndIngest_Works()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string sstPath = Path.Combine(dir.Path, "test.sst");

        using var opts = new DbOptions { CreateIfMissing = true };

        // Write SST file
        using (var writer = SstFileWriter.Create(opts))
        {
            writer.Open(sstPath);
            writer.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
            writer.Put(Encoding.UTF8.GetBytes("b"), Encoding.UTF8.GetBytes("2"));
            writer.Put(Encoding.UTF8.GetBytes("c"), Encoding.UTF8.GetBytes("3"));
            writer.Finish();

            Assert.True(writer.FileSize > 0);
        }

        // Ingest into DB
        using var db = RocksDb.Open(opts, dbPath);
        using var ingestOpts = new IngestExternalFileOptions();
        db.IngestExternalFile(new[] { sstPath }, ingestOpts);

        Assert.Equal("1", db.GetString("a"));
        Assert.Equal("2", db.GetString("b"));
        Assert.Equal("3", db.GetString("c"));
    }

    [Fact]
    public void SstFileWriter_Delete()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string sstPath1 = Path.Combine(dir.Path, "data.sst");
        string sstPath2 = Path.Combine(dir.Path, "delete.sst");

        using var opts = new DbOptions { CreateIfMissing = true };

        // Write data SST
        using (var writer = SstFileWriter.Create(opts))
        {
            writer.Open(sstPath1);
            writer.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
            writer.Put(Encoding.UTF8.GetBytes("b"), Encoding.UTF8.GetBytes("2"));
            writer.Finish();
        }

        // Ingest data
        using var db = RocksDb.Open(opts, dbPath);
        using var ingestOpts = new IngestExternalFileOptions();
        db.IngestExternalFile(new[] { sstPath1 }, ingestOpts);

        Assert.Equal("1", db.GetString("a"));
        Assert.Equal("2", db.GetString("b"));

        // Write delete SST
        using (var writer = SstFileWriter.Create(opts))
        {
            writer.Open(sstPath2);
            writer.Delete(Encoding.UTF8.GetBytes("a"));
            writer.Finish();
        }

        db.IngestExternalFile(new[] { sstPath2 }, ingestOpts);

        Assert.Null(db.GetString("a"));
        Assert.Equal("2", db.GetString("b"));
    }

    [Fact]
    public void IngestExternalFile_ColumnFamily()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string sstPath = Path.Combine(dir.Path, "cf.sst");

        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        using (var writer = SstFileWriter.Create(opts))
        {
            writer.Open(sstPath);
            writer.Put(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("value"));
            writer.Finish();
        }

        using var db = RocksDb.Open(opts, dbPath, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        using var ingestOpts = new IngestExternalFileOptions();
        db.IngestExternalFile(new[] { sstPath }, cf1, ingestOpts);

        Assert.Equal("value", db.GetString("key", cf1));
        Assert.Null(db.GetString("key")); // Not in default CF
    }

    [Fact]
    public void IngestExternalFileOptions_Properties()
    {
        using var opts = new IngestExternalFileOptions();

        // Just verify these don't throw
        opts.MoveFiles = false;
        opts.SnapshotConsistency = true;
        opts.AllowGlobalSeqno = true;
        opts.AllowBlockingFlush = true;
    }
}
