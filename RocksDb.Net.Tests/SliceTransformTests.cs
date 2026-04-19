namespace RocksDbNet.Tests;

public class SliceTransformTests
{
    [Fact]
    public void CreateFixedPrefix_Works()
    {
        using var st = SliceTransform.CreateFixedPrefix(4);
        Assert.False(st.IsDisposed);
    }

    [Fact]
    public void CreateNoop_Works()
    {
        using var st = SliceTransform.CreateNoop();
        Assert.False(st.IsDisposed);
    }

    [Fact]
    public void FixedPrefix_WithDatabase()
    {
        using var dir = new TempDir();
        using var st = SliceTransform.CreateFixedPrefix(3);
        using var bbto = new BlockBasedTableOptions();
        bbto.WholeKeyFiltering = false;

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.PrefixExtractor = st;
        opts.BlockBasedTableFactory = bbto;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Put("abc_1", "v1");
        db.Put("abc_2", "v2");
        db.Put("xyz_1", "v3");

        Assert.Equal("v1", db.GetString("abc_1"));
        Assert.Equal("v3", db.GetString("xyz_1"));
    }
}
