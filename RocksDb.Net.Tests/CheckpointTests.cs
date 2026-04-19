namespace RocksDbNet.Tests;

public class CheckpointTests
{
    [Fact]
    public void CreateCheckpoint_Works()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string cpPath = Path.Combine(dir.Path, "checkpoint");

        using var opts = new DbOptions { CreateIfMissing = true };
        using var db = RocksDb.Open(opts, dbPath);
        db.Put("key", "value");

        using var checkpoint = Checkpoint.Create(db);
        checkpoint.CreateCheckpoint(cpPath);

        // Open the checkpoint directory as a regular DB
        using var cpOpts = new DbOptions();
        using var cpDb = RocksDb.Open(cpOpts, cpPath);

        Assert.Equal("value", cpDb.GetString("key"));
    }

    [Fact]
    public void Checkpoint_Isolation()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string cpPath = Path.Combine(dir.Path, "checkpoint");

        using var opts = new DbOptions { CreateIfMissing = true };
        using var db = RocksDb.Open(opts, dbPath);
        db.Put("key", "v1");

        using var checkpoint = Checkpoint.Create(db);
        checkpoint.CreateCheckpoint(cpPath);

        // Write more to original after checkpoint
        db.Put("key", "v2");
        db.Put("key2", "new");

        // Checkpoint should still have original data
        using var cpOpts = new DbOptions();
        using var cpDb = RocksDb.Open(cpOpts, cpPath);

        Assert.Equal("v1", cpDb.GetString("key"));
        Assert.Null(cpDb.GetString("key2"));
    }
}
