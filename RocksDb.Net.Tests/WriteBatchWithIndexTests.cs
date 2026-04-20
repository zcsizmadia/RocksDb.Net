using System.Text;

namespace RocksDbNet.Tests;

public class WriteBatchWithIndexTests
{
    [Fact]
    public void Constructor_Default_CreatesEmptyBatch()
    {
        using var batch = new WriteBatchWithIndex();
        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void Constructor_WithReservedBytes()
    {
        using var batch = new WriteBatchWithIndex(reservedBytes: 1024);
        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void Constructor_NegativeReservedBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WriteBatchWithIndex(reservedBytes: -1));
    }

    [Fact]
    public void Put_String_IncreasesCount()
    {
        using var batch = new WriteBatchWithIndex();

        batch.Put("key1", "val1");
        batch.Put("key2", "val2");

        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public void Put_Bytes_IncreasesCount()
    {
        using var batch = new WriteBatchWithIndex();
        byte[] key = [1, 2, 3];
        byte[] val = [4, 5, 6];

        batch.Put(key, val);

        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        using var batch = new WriteBatchWithIndex();

        batch.Put("key1", "val1");
        batch.Put("key2", "val2");
        batch.Clear();

        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void Delete_String_IncreasesCount()
    {
        using var batch = new WriteBatchWithIndex();

        batch.Put("key1", "val1");
        batch.Delete("key1");

        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public void Delete_Bytes_IncreasesCount()
    {
        using var batch = new WriteBatchWithIndex();

        batch.Delete(Encoding.UTF8.GetBytes("key1"));

        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void Merge_IncreasesCount()
    {
        using var batch = new WriteBatchWithIndex();

        batch.Merge(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("val"));

        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void SingleDelete_IncreasesCount()
    {
        using var batch = new WriteBatchWithIndex();

        batch.SingleDelete(Encoding.UTF8.GetBytes("key"));

        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void DeleteRange_DoesNotThrow()
    {
        using var batch = new WriteBatchWithIndex();

        batch.DeleteRange(
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("z"));
    }

    [Fact]
    public void PutLogData_DoesNotChangeCount()
    {
        using var batch = new WriteBatchWithIndex();

        batch.PutLogData(Encoding.UTF8.GetBytes("log entry"));

        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void SavePoint_RollbackToSavePoint()
    {
        using var batch = new WriteBatchWithIndex();

        batch.Put("a", "1");
        batch.SetSavePoint();
        batch.Put("b", "2");
        Assert.Equal(2, batch.Count);

        batch.RollbackToSavePoint();
        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void GetData_ReturnsNonEmpty()
    {
        using var batch = new WriteBatchWithIndex();
        batch.Put("key", "val");

        byte[] data = batch.GetData();

        Assert.NotEmpty(data);
    }

    [Fact]
    public void Fluent_Chaining_Works()
    {
        using var batch = new WriteBatchWithIndex();

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

        using var batch = new WriteBatchWithIndex();
        batch.Put("key1", "val1", cf1);
        batch.Delete("key1", cf1);

        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public void Merge_ColumnFamily()
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

        using var batch = new WriteBatchWithIndex();
        batch.Merge(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("val"), cf1);

        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void SingleDelete_ColumnFamily()
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

        using var batch = new WriteBatchWithIndex();
        batch.SingleDelete(Encoding.UTF8.GetBytes("key"), cf1);

        Assert.Equal(1, batch.Count);
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

        using var batch = new WriteBatchWithIndex();
        batch.DeleteRange(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("z"), cf1);
    }
}
