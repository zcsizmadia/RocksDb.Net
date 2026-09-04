using System.Text;

namespace RocksDbNet.Tests;

public class CompactionFilterTests
{
    private sealed class NameValidatingFilter : CompactionFilter
    {
        public NameValidatingFilter(string name) : base(name)
        {
        }

        protected override FilterDecision Filter(int level, ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            newValue = null;
            return FilterDecision.Keep;
        }
    }

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

    private sealed class AlwaysChangeValueFilter : CompactionFilter
    {
        public AlwaysChangeValueFilter() : base("AlwaysChangeValueFilter") { }

        protected override FilterDecision Filter(int level, ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            newValue = Encoding.UTF8.GetBytes($"changed_{Encoding.UTF8.GetString(key)}");
            return FilterDecision.ChangeValue;
        }
    }

    [Fact]
    public void CompactionFilter_RemovesKeys()
    {
        var filter = new PrefixFilter();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.CompactionFilter = filter;

        using var db = TestDb.OpenInMemory(opts);

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
        var filter = new ChangeValueFilter();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.CompactionFilter = filter;

        using var db = TestDb.OpenInMemory(opts);

        db.Put("transform", "original");
        db.Put("normal", "value");

        db.Flush();
        db.CompactRange();

        Assert.Equal("TRANSFORMED", db.GetString("transform"));
        Assert.Equal("value", db.GetString("normal"));
    }

    [Fact]
    public void CompactionFilter_Ctor_ThrowsOnNullOrEmptyName()
    {
        Assert.Throws<ArgumentException>(() => new NameValidatingFilter(""));
        Assert.Throws<ArgumentNullException>(() => new NameValidatingFilter(null!));
    }

    // The IgnoreSnapshots setter is gone, and with it the test that it refused
    // false. RocksDb deprecated the setting, always ignores snapshots for a
    // compaction filter, and fails table file creation if a filter reports
    // false, so the property could only accept the value it already had or
    // throw. A caller reaching for it now gets a compile error instead.

    /// <summary>
    /// And with the setting left alone, which is the same as true, the filter
    /// does apply. This is the behaviour the old false-path test appeared to be
    /// asserting.
    /// </summary>
    [Fact]
    public void CompactionFilter_ByDefault_TheFilterApplies()
    {
        var filter = new PrefixFilter();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.CompactionFilter = filter;

        using var db = TestDb.OpenInMemory(opts);

        db.Put("tmp_1", "v1");
        db.Flush();
        db.CompactRange();

        Assert.Null(db.GetString("tmp_1"));
    }

    [Fact]
    public void CompactionFilter_ChangeValue_RepeatedKeys_WorksStably()
    {
        var filter = new AlwaysChangeValueFilter();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.CompactionFilter = filter;

        using var db = TestDb.OpenInMemory(opts);

        for (int i = 0; i < 40; i++)
        {
            db.Put($"k{i:D3}", "v");
        }

        db.Flush();
        db.CompactRange();

        Assert.Equal("changed_k000", db.GetString("k000"));
        Assert.Equal("changed_k039", db.GetString("k039"));
    }
}
