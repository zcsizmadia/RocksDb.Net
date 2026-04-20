using System.Text;

namespace RocksDbNet.Tests;

public class ComparatorTests
{
    private sealed class ReverseBytewiseComparator : Comparator
    {
        public ReverseBytewiseComparator() : base("ReverseBytewise") { }

        public override int Compare(ReadOnlySpan<byte> keyA, ReadOnlySpan<byte> keyB)
        {
            return keyB.SequenceCompareTo(keyA);
        }
    }

    [Fact]
    public void Comparator_ReversesKeyOrder()
    {
        using var dir = new TempDir();
        var comparator = new ReverseBytewiseComparator();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.Comparator = comparator;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Put("a", "1");
        db.Put("b", "2");
        db.Put("c", "3");

        using var iter = db.NewIterator();
        iter.SeekToFirst();

        // With reverse comparator, "c" should come first
        Assert.True(iter.IsValid());
        Assert.Equal("c", iter.KeyAsString());

        iter.Next();
        Assert.True(iter.IsValid());
        Assert.Equal("b", iter.KeyAsString());

        iter.Next();
        Assert.True(iter.IsValid());
        Assert.Equal("a", iter.KeyAsString());

        iter.Next();
        Assert.False(iter.IsValid());
    }

    [Fact]
    public void Comparator_GetAndPut_Works()
    {
        using var dir = new TempDir();
        var comparator = new ReverseBytewiseComparator();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.Comparator = comparator;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Put("key1", "value1");
        db.Put("key2", "value2");

        Assert.Equal("value1", db.GetString("key1"));
        Assert.Equal("value2", db.GetString("key2"));
    }
}
