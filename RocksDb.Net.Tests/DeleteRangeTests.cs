using System.Text;

namespace RocksDbNet.Tests;

public class DeleteRangeTests
{
    [Fact]
    public void DeleteRange_DefaultCf()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");
        db.Db.Put("d", "4");

        // Delete range [a, c) — a and b are deleted, c and d remain
        db.Db.DeleteRange(
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("c"));

        Assert.Null(db.Db.GetString("a"));
        Assert.Null(db.Db.GetString("b"));
        Assert.Equal("3", db.Db.GetString("c"));
        Assert.Equal("4", db.Db.GetString("d"));
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
        db.Put("c", "3", cf1);

        db.DeleteRange(
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("c"),
            cf1);

        Assert.Null(db.GetString("a", cf1));
        Assert.Null(db.GetString("b", cf1));
        Assert.Equal("3", db.GetString("c", cf1));
    }

    [Fact]
    public void DeleteRange_EmptyRange_NoEffect()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");

        db.Db.DeleteRange(
            Encoding.UTF8.GetBytes("x"),
            Encoding.UTF8.GetBytes("z"));

        Assert.Equal("1", db.Db.GetString("a"));
    }
}
