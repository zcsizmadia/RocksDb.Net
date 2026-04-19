using System.Text;

namespace RocksDbNet.Tests;

public class IteratorTests
{
    [Fact]
    public void SeekToFirst_IteratesForward()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");

        using var iter = db.Db.NewIterator();
        iter.SeekToFirst();

        Assert.True(iter.IsValid());
        Assert.Equal("a", iter.KeyAsString());
        Assert.Equal("1", iter.ValueAsString());

        iter.Next();
        Assert.True(iter.IsValid());
        Assert.Equal("b", iter.KeyAsString());

        iter.Next();
        Assert.True(iter.IsValid());
        Assert.Equal("c", iter.KeyAsString());

        iter.Next();
        Assert.False(iter.IsValid());
    }

    [Fact]
    public void SeekToLast_IteratesBackward()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");

        using var iter = db.Db.NewIterator();
        iter.SeekToLast();

        Assert.True(iter.IsValid());
        Assert.Equal("c", iter.KeyAsString());

        iter.Prev();
        Assert.True(iter.IsValid());
        Assert.Equal("b", iter.KeyAsString());

        iter.Prev();
        Assert.Equal("a", iter.KeyAsString());
    }

    [Fact]
    public void Seek_PositionsCorrectly()
    {
        using var db = new TempDb();
        db.Db.Put("apple", "1");
        db.Db.Put("banana", "2");
        db.Db.Put("cherry", "3");

        using var iter = db.Db.NewIterator();
        iter.Seek("banana");

        Assert.True(iter.IsValid());
        Assert.Equal("banana", iter.KeyAsString());
    }

    [Fact]
    public void Seek_Bytes()
    {
        using var db = new TempDb();
        db.Db.Put("aa", "1");
        db.Db.Put("bb", "2");

        using var iter = db.Db.NewIterator();
        iter.Seek(Encoding.UTF8.GetBytes("b"));

        Assert.True(iter.IsValid());
        Assert.Equal("bb", iter.KeyAsString());
    }

    [Fact]
    public void SeekForPrev_PositionsCorrectly()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("c", "3");
        db.Db.Put("e", "5");

        using var iter = db.Db.NewIterator();
        iter.SeekForPrev("d");

        Assert.True(iter.IsValid());
        Assert.Equal("c", iter.KeyAsString());
    }

    [Fact]
    public void SeekForPrev_String()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("z", "2");

        using var iter = db.Db.NewIterator();
        iter.SeekForPrev("m");

        Assert.True(iter.IsValid());
        Assert.Equal("a", iter.KeyAsString());
    }

    [Fact]
    public void Key_Value_AsArray()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");

        using var iter = db.Db.NewIterator();
        iter.SeekToFirst();

        byte[] keyArr = iter.KeyToArray();
        byte[] valArr = iter.ValueToArray();

        Assert.Equal(Encoding.UTF8.GetBytes("key"), keyArr);
        Assert.Equal(Encoding.UTF8.GetBytes("value"), valArr);
    }

    [Fact]
    public void AsEnumerable_IteratesAll()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");

        using var iter = db.Db.NewIterator();
        iter.SeekToFirst();

        var items = iter.AsEnumerable().ToList();

        Assert.Equal(3, items.Count);
        Assert.Equal("a", Encoding.UTF8.GetString(items[0].Key));
        Assert.Equal("1", Encoding.UTF8.GetString(items[0].Value));
    }

    [Fact]
    public void ForEach_InvokesForAll()
    {
        using var db = new TempDb();
        db.Db.Put("x", "10");
        db.Db.Put("y", "20");

        using var iter = db.Db.NewIterator();
        iter.SeekToFirst();

        var collected = new List<string>();
        iter.ForEach((key, value) =>
        {
            collected.Add(Encoding.UTF8.GetString(key));
        });

        Assert.Equal(2, collected.Count);
        Assert.Contains("x", collected);
        Assert.Contains("y", collected);
    }

    [Fact]
    public void CheckForError_DoesNotThrowOnValid()
    {
        using var db = new TempDb();
        db.Db.Put("k", "v");

        using var iter = db.Db.NewIterator();
        iter.SeekToFirst();

        iter.CheckForError(); // Should not throw
    }

    [Fact]
    public void EmptyDatabase_IteratorInvalid()
    {
        using var db = new TempDb();

        using var iter = db.Db.NewIterator();
        iter.SeekToFirst();

        Assert.False(iter.IsValid());
    }

    [Fact]
    public void Refresh_DoesNotThrow()
    {
        using var db = new TempDb();
        db.Db.Put("k", "v");

        using var iter = db.Db.NewIterator();
        iter.SeekToFirst();
        Assert.True(iter.IsValid());

        iter.Refresh();
    }

    [Fact]
    public void Iterator_ColumnFamily()
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

        db.Put("cf_key", "cf_val", cf1);

        using var iter = db.NewIterator(cf1);
        iter.SeekToFirst();

        Assert.True(iter.IsValid());
        Assert.Equal("cf_key", iter.KeyAsString());
        Assert.Equal("cf_val", iter.ValueAsString());
    }
}
