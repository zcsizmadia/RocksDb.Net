namespace RocksDbNet.Tests;

public class SnapshotTests
{
    [Fact]
    public void Snapshot_SeesOldData()
    {
        using var db = new TempDb();

        db.Db.Put("key", "v1");

        using var snapshot = db.Db.NewSnapshot();
        Assert.True(snapshot.SequenceNumber > 0);

        // Write new value after snapshot
        db.Db.Put("key", "v2");

        // Reading with snapshot should see old value
        using var readOpts = new ReadOptions();
        readOpts.SetSnapshot(snapshot);

        var val = db.Db.GetString("key", readOpts);
        Assert.Equal("v1", val);

        // Reading without snapshot sees new value
        var val2 = db.Db.GetString("key");
        Assert.Equal("v2", val2);
    }

    [Fact]
    public void Snapshot_SequenceNumber_Increases()
    {
        using var db = new TempDb();

        db.Db.Put("k1", "v1");
        using var snap1 = db.Db.NewSnapshot();

        db.Db.Put("k2", "v2");
        using var snap2 = db.Db.NewSnapshot();

        Assert.True(snap2.SequenceNumber > snap1.SequenceNumber);
    }

    [Fact]
    public void Snapshot_DeletedKey_StillVisible()
    {
        using var db = new TempDb();

        db.Db.Put("key", "value");
        using var snapshot = db.Db.NewSnapshot();

        db.Db.Delete("key");

        using var readOpts = new ReadOptions();
        readOpts.SetSnapshot(snapshot);

        Assert.Equal("value", db.Db.GetString("key", readOpts));
        Assert.Null(db.Db.GetString("key"));
    }

    [Fact]
    public void Snapshot_ClearSnapshot_ReadsLatest()
    {
        using var db = new TempDb();

        db.Db.Put("key", "v1");
        using var snapshot = db.Db.NewSnapshot();
        db.Db.Put("key", "v2");

        using var readOpts = new ReadOptions();
        readOpts.SetSnapshot(snapshot);
        Assert.Equal("v1", db.Db.GetString("key", readOpts));

        // Clear snapshot from read options
        readOpts.SetSnapshot(null);
        Assert.Equal("v2", db.Db.GetString("key", readOpts));
    }
}
