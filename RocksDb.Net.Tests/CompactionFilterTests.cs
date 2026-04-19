using System.Text;

namespace RocksDbNet.Tests;

public class CompactionFilterTests
{
    private sealed class RemoveKeyFilter : CompactionFilter
    {
        private readonly string _keyToRemove;

        public RemoveKeyFilter(string keyToRemove) : base("RemoveKeyFilter")
        {
            _keyToRemove = keyToRemove;
        }

        protected override FilterDecision Filter(int level, ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            newValue = null;
            string keyStr = Encoding.UTF8.GetString(key);
            return keyStr == _keyToRemove ? FilterDecision.Remove : FilterDecision.Keep;
        }
    }

    private sealed class PrefixFilter : CompactionFilter
    {
        public PrefixFilter() : base("PrefixFilter") { }

        protected override FilterDecision Filter(int level, ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            newValue = null;
            string keyStr = Encoding.UTF8.GetString(key);
            return keyStr.StartsWith("tmp_") ? FilterDecision.Remove : FilterDecision.Keep;
        }
    }

    private sealed class ChangeValueFilter : CompactionFilter
    {
        public ChangeValueFilter() : base("ChangeValueFilter") { }

        protected override FilterDecision Filter(int level, ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            string keyStr = Encoding.UTF8.GetString(key);
            if (keyStr == "transform")
            {
                newValue = Encoding.UTF8.GetBytes("TRANSFORMED");
                return FilterDecision.ChangeValue;
            }
            newValue = null;
            return FilterDecision.Keep;
        }
    }

    [Fact]
    public void CompactionFilter_RemovesKeys()
    {
        using var dir = new TempDir();
        var filter = new PrefixFilter();
        filter.IgnoreSnapshots = true;

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.CompactionFilter = filter;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Put("tmp_1", "v1");
        db.Put("tmp_2", "v2");
        db.Put("keep_1", "v3");

        db.Flush();
        db.CompactRange();

        // tmp_ keys should be removed by compaction
        Assert.Null(db.GetString("tmp_1"));
        Assert.Null(db.GetString("tmp_2"));
        Assert.Equal("v3", db.GetString("keep_1"));
    }

    [Fact]
    public void CompactionFilter_ChangesValue()
    {
        using var dir = new TempDir();
        var filter = new ChangeValueFilter();
        filter.IgnoreSnapshots = true;

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.CompactionFilter = filter;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Put("transform", "original");
        db.Put("normal", "value");

        db.Flush();
        db.CompactRange();

        Assert.Equal("TRANSFORMED", db.GetString("transform"));
        Assert.Equal("value", db.GetString("normal"));
    }
}
