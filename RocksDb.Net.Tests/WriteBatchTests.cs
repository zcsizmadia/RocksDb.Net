using System.Text;

namespace RocksDbNet.Tests;

public class WriteBatchTests
{
    [Fact]
    public void Put_String_IncreasesCount()
    {
        using var batch = new WriteBatch();

        batch.Put("key1", "val1");
        batch.Put("key2", "val2");

        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public void Put_Bytes_IncreasesCount()
    {
        using var batch = new WriteBatch();
        byte[] key = [1, 2, 3];
        byte[] val = [4, 5, 6];

        batch.Put(key, val);

        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void Delete_IncreasesCount()
    {
        using var batch = new WriteBatch();

        batch.Put("key1", "val1");
        batch.Delete("key1");

        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        using var batch = new WriteBatch();

        batch.Put("key1", "val1");
        batch.Put("key2", "val2");
        batch.Clear();

        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void Write_AppliesAll()
    {
        using var db = new TempDb();

        using var batch = new WriteBatch();
        batch.Put("a", "1");
        batch.Put("b", "2");
        batch.Put("c", "3");
        batch.Delete("b");

        db.Db.Write(batch);

        Assert.Equal("1", db.Db.GetString("a"));
        Assert.Null(db.Db.GetString("b"));
        Assert.Equal("3", db.Db.GetString("c"));
    }

    [Fact]
    public void Merge_String_IncreasesCount()
    {
        using var batch = new WriteBatch();

        batch.Merge(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("val"));

        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void SingleDelete_IncreasesCount()
    {
        using var batch = new WriteBatch();

        batch.SingleDelete(Encoding.UTF8.GetBytes("key"));

        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void DeleteRange_IncreasesCount()
    {
        using var batch = new WriteBatch();

        batch.DeleteRange(
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("z"));

        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void PutLogData_DoesNotChangeCount()
    {
        using var batch = new WriteBatch();

        batch.PutLogData(Encoding.UTF8.GetBytes("log entry"));

        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void SavePoint_RollbackToSavePoint()
    {
        using var db = new TempDb();
        using var batch = new WriteBatch();

        batch.Put("a", "1");
        batch.SetSavePoint();
        batch.Put("b", "2");
        batch.RollbackToSavePoint();

        db.Db.Write(batch);

        Assert.Equal("1", db.Db.GetString("a"));
        Assert.Null(db.Db.GetString("b"));
    }

    [Fact]
    public void PopSavePoint_DoesNotThrow()
    {
        using var batch = new WriteBatch();

        batch.SetSavePoint();
        batch.Put("a", "1");
        batch.PopSavePoint();
    }

    [Fact]
    public void GetData_ReturnsNonEmpty()
    {
        using var batch = new WriteBatch();
        batch.Put("key", "val");

        byte[] data = batch.GetData();

        Assert.NotEmpty(data);
    }

    [Fact]
    public void Fluent_Chaining_Works()
    {
        using var batch = new WriteBatch();

        var result = batch
            .Put("a", "1")
            .Put("b", "2")
            .Delete("a")
            .Clear()
            .Put("c", "3");

        Assert.Same(batch, result);
        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void Put_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        using var batch = new WriteBatch();
        batch.Put("key1", "val1", cf1);
        batch.Delete("key1", cf1);
        db.Write(batch);

        Assert.Null(db.GetString("key1", cf1));
    }

    [Fact]
    public void DeleteRange_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        using var batch = new WriteBatch();
        batch.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"), cf1);
        batch.Put(Encoding.UTF8.GetBytes("b"), Encoding.UTF8.GetBytes("2"), cf1);
        batch.Put(Encoding.UTF8.GetBytes("c"), Encoding.UTF8.GetBytes("3"), cf1);
        db.Write(batch);

        using var batch2 = new WriteBatch();
        batch2.DeleteRange(
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("c"),
            cf1);
        db.Write(batch2);

        Assert.Null(db.GetString("a", cf1));
        Assert.Null(db.GetString("b", cf1));
        Assert.Equal("3", db.GetString("c", cf1));
    }
}
