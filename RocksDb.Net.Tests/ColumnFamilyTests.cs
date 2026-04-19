using System.Text;

namespace RocksDbNet.Tests;

public class ColumnFamilyTests
{
    [Fact]
    public void CreateColumnFamily_Works()
    {
        using var db = new TempDb();
        using var cfOpts = new DbOptions();

        using var cf = db.Db.CreateColumnFamily(cfOpts, "test_cf");

        Assert.NotNull(cf);
        Assert.Equal("test_cf", cf.Name);
    }

    [Fact]
    public void DropColumnFamily_Works()
    {
        using var db = new TempDb();
        using var cfOpts = new DbOptions();
        using var cf = db.Db.CreateColumnFamily(cfOpts, "to_drop");

        db.Db.DropColumnFamily(cf);
        // Should not throw
    }

    [Fact]
    public void OpenWithColumnFamilies_Works()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
            new("cf2"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);

        var cf1 = db.GetColumnFamily("cf1");
        var cf2 = db.GetColumnFamily("cf2");

        Assert.NotNull(cf1);
        Assert.NotNull(cf2);
        Assert.Equal("cf1", cf1.Name);
        Assert.Equal("cf2", cf2.Name);
    }

    [Fact]
    public void PutGet_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("data"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var dataCf = db.GetColumnFamily("data");

        db.Put("key", "value", dataCf);
        var result = db.GetString("key", dataCf);

        Assert.Equal("value", result);
    }

    [Fact]
    public void ColumnFamilies_AreIsolated()
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

        db.Put("key", "default_value");
        db.Put("key", "cf1_value", cf1);

        Assert.Equal("default_value", db.GetString("key"));
        Assert.Equal("cf1_value", db.GetString("key", cf1));
    }

    [Fact]
    public void Delete_ColumnFamily()
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

        db.Put("key", "value", cf1);
        Assert.Equal("value", db.GetString("key", cf1));

        db.Delete("key", cf1);
        Assert.Null(db.GetString("key", cf1));
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

        db.Put("a", "1", cf1);
        db.Put("b", "2", cf1);
        db.Put("d", "4", cf1);

        db.DeleteRange(
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("c"),
            cf1);

        Assert.Null(db.GetString("a", cf1));
        Assert.Null(db.GetString("b", cf1));
        Assert.Equal("4", db.GetString("d", cf1));
    }

    [Fact]
    public void GetDefaultColumnFamily_Works()
    {
        using var db = new TempDb();

        using var defaultCf = db.Db.GetDefaultColumnFamily();
        Assert.NotNull(defaultCf);
        Assert.Equal("default", defaultCf.Name);
    }

    [Fact]
    public void ColumnFamilyHandle_Id_IsValid()
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

        uint id = cf1.Id;
        Assert.True(id > 0); // cf1 should have an id > 0 (default is 0)
    }

    [Fact]
    public void ColumnFamilyHandle_ToString_ReturnsName()
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

        Assert.Equal("cf1", cf1.ToString());
    }

    [Fact]
    public void ColumnFamilyDescriptor_DefaultOptions()
    {
        var desc = new ColumnFamilyDescriptor("test");

        Assert.Equal("test", desc.Name);
        Assert.NotNull(desc.Options);
    }

    [Fact]
    public void ListColumnFamilies_AfterCreate()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            using var cfOpts = new DbOptions();
            using var cf = db.CreateColumnFamily(cfOpts, "new_cf");
        }

        var families = RocksDb.ListColumnFamilies(opts, dir.Path);
        Assert.Contains("default", families);
        Assert.Contains("new_cf", families);
    }

    [Fact]
    public void Flush_ColumnFamily()
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

        db.Put("key", "value", cf1);
        db.Flush(cf1);
    }

    [Fact]
    public void Merge_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        opts.SetUInt64AddMergeOperator();

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default", opts),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);

        byte[] key = Encoding.UTF8.GetBytes("counter");
        byte[] val1 = BitConverter.GetBytes(1UL);
        byte[] val2 = BitConverter.GetBytes(2UL);

        db.Merge(key, val1);
        db.Merge(key, val2);

        byte[]? result = db.Get(key.AsSpan());
        Assert.NotNull(result);

        ulong merged = BitConverter.ToUInt64(result);
        Assert.Equal(3UL, merged);
    }

    [Fact]
    public void CompactRange_ColumnFamily()
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

        db.Put("a", "1", cf1);
        db.Flush(cf1);

        db.CompactRange(cf1);
    }

    [Fact]
    public void OpenReadOnly_ColumnFamilies()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        // Create and populate
        using (var db = RocksDb.Open(opts, dir.Path, cfDescs))
        {
            var cf1 = db.GetColumnFamily("cf1");
            db.Put("key", "value", cf1);
        }

        // Open read-only
        using var roOpts = new DbOptions();
        using var rodb = RocksDb.OpenReadOnly(roOpts, dir.Path, cfDescs);
        var roCf1 = rodb.GetColumnFamily("cf1");

        Assert.Equal("value", rodb.GetString("key", roCf1));
    }

}
