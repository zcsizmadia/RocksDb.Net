using System.Text;

namespace RocksDbNet.Tests;

public class CompactionFilterFactoryTests
{
    private sealed class PrefixRemovalFilter : CompactionFilter
    {
        private readonly string _prefix;

        public PrefixRemovalFilter(string prefix) : base("PrefixRemoval")
        {
            _prefix = prefix;
        }

        protected override FilterDecision Filter(int level, ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            newValue = null;
            string keyStr = Encoding.UTF8.GetString(key);
            return keyStr.StartsWith(_prefix) ? FilterDecision.Remove : FilterDecision.Keep;
        }
    }

    private sealed class TestFilterFactory : CompactionFilterFactory
    {
        private readonly string _prefix;

        public TestFilterFactory(string prefix) : base("TestFilterFactory")
        {
            _prefix = prefix;
        }

        protected override CompactionFilter CreateFilter(CompactionFilterContext context)
        {
            return new PrefixRemovalFilter(_prefix);
        }
    }

    [Fact]
    public void CompactionFilterFactory_CreatesFilterPerCompaction()
    {
        using var dir = new TempDir();
        var factory = new TestFilterFactory("tmp_");

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.CompactionFilterFactory = factory;

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
}
