using System.Text;

namespace RocksDbNet.Tests;

public class RocksDbBasicTests
{
    [Fact]
    public void Open_CreateIfMissing_CreatesDatabase()
    {
        using var dir = new TempDir();
        using var options = new DbOptions { CreateIfMissing = true };
        using var db = RocksDb.Open(options, dir.Path);

        Assert.NotNull(db);
        Assert.False(db.IsDisposed);
    }

    [Fact]
    public void Open_WithoutCreateIfMissing_Throws()
    {
        using var dir = new TempDir();
        using var options = new DbOptions { CreateIfMissing = false };

        Assert.Throws<RocksDbException>(() => RocksDb.Open(options, dir.Path));
    }

    [Fact]
    public void Put_Get_String_RoundTrips()
    {
        using var db = new TempDb();

        db.Db.Put("hello", "world");
        var result = db.Db.GetString("hello");

        Assert.Equal("world", result);
    }

    [Fact]
    public void Put_Get_Bytes_RoundTrips()
    {
        using var db = new TempDb();
        byte[] key = [1, 2, 3];
        byte[] value = [4, 5, 6];

        db.Db.Put(key, value);
        var result = db.Db.Get(key.AsSpan());

        Assert.NotNull(result);
        Assert.Equal(value, result);
    }

    [Fact]
    public void Get_NonExistentKey_ReturnsNull()
    {
        using var db = new TempDb();

        var result = db.Db.GetString("missing");

        Assert.Null(result);
    }

    [Fact]
    public void TryGet_ExistingKey_ReturnsTrue()
    {
        using var db = new TempDb();
        db.Db.Put("key1", "value1");

        bool found = db.Db.TryGet(Encoding.UTF8.GetBytes("key1"), out byte[]? value);

        Assert.True(found);
        Assert.NotNull(value);
        Assert.Equal("value1", Encoding.UTF8.GetString(value));
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        using var db = new TempDb();

        bool found = db.Db.TryGet(Encoding.UTF8.GetBytes("missing"), out byte[]? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Delete_RemovesKey()
    {
        using var db = new TempDb();

        db.Db.Put("key1", "value1");
        Assert.Equal("value1", db.Db.GetString("key1"));

        db.Db.Delete("key1");
        Assert.Null(db.Db.GetString("key1"));
    }

    [Fact]
    public void Delete_Bytes_RemovesKey()
    {
        using var db = new TempDb();
        byte[] key = Encoding.UTF8.GetBytes("key1");

        db.Db.Put(key, Encoding.UTF8.GetBytes("value1"));
        Assert.NotNull(db.Db.Get(key.AsSpan()));

        db.Db.Delete(key);
        Assert.Null(db.Db.Get(key.AsSpan()));
    }

    [Fact]
    public void Put_Overwrite_ReturnsNewValue()
    {
        using var db = new TempDb();

        db.Db.Put("key", "v1");
        db.Db.Put("key", "v2");

        Assert.Equal("v2", db.Db.GetString("key"));
    }

    [Fact]
    public void MultiGet_ReturnsCorrectResults()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");

        var keys = new List<byte[]>
        {
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("missing"),
            Encoding.UTF8.GetBytes("c"),
        };

        byte[]?[] results = db.Db.MultiGet(keys);

        Assert.Equal(3, results.Length);
        Assert.Equal("1", Encoding.UTF8.GetString(results[0]!));
        Assert.Null(results[1]);
        Assert.Equal("3", Encoding.UTF8.GetString(results[2]!));
    }

    [Fact]
    public void MultiGet_EmptyList_ReturnsEmpty()
    {
        using var db = new TempDb();

        var results = db.Db.MultiGet(new List<byte[]>());

        Assert.Empty(results);
    }

    [Fact]
    public void GetProperty_Stats()
    {
        using var db = new TempDb(o => o.EnableStatistics());

        db.Db.Put("key", "value");
        string? stats = db.Db.GetProperty("rocksdb.stats");

        Assert.NotNull(stats);
        Assert.NotEmpty(stats);
    }

    [Fact]
    public void GetPropertyInt_EstimateNumKeys()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Flush();

        ulong? numKeys = db.Db.GetPropertyInt("rocksdb.estimate-num-keys");

        Assert.NotNull(numKeys);
    }

    [Fact]
    public void LatestSequenceNumber_IncrementsOnWrite()
    {
        using var db = new TempDb();

        ulong seq1 = db.Db.LatestSequenceNumber;
        db.Db.Put("key", "value");
        ulong seq2 = db.Db.LatestSequenceNumber;

        Assert.True(seq2 > seq1);
    }

    [Fact]
    public void Destroy_RemovesDatabase()
    {
        using var dir = new TempDir();
        using var options = new DbOptions { CreateIfMissing = true };

        // Create and close database
        using (var db = RocksDb.Open(options, dir.Path))
        {
            db.Put("key", "value");
        }

        // Destroy
        RocksDb.Destroy(options, dir.Path);

        // Should fail to open without CreateIfMissing
        using var opts2 = new DbOptions { CreateIfMissing = false };
        Assert.Throws<RocksDbException>(() => RocksDb.Open(opts2, dir.Path));
    }

    [Fact]
    public void OpenReadOnly_CanRead()
    {
        using var dir = new TempDir();
        using var options = new DbOptions { CreateIfMissing = true };

        // Create and populate
        using (var db = RocksDb.Open(options, dir.Path))
        {
            db.Put("key", "value");
        }

        // Open read-only
        using var roOpts = new DbOptions();
        using var rodb = RocksDb.OpenReadOnly(roOpts, dir.Path);

        Assert.Equal("value", rodb.GetString("key"));
    }

    [Fact]
    public void OpenWithTtl_Works()
    {
        using var dir = new TempDir();
        using var options = new DbOptions { CreateIfMissing = true };
        using var db = RocksDb.OpenWithTtl(options, dir.Path, ttlSeconds: 3600);

        db.Put("key", "value");
        Assert.Equal("value", db.GetString("key"));
    }

    [Fact]
    public void ListColumnFamilies_ReturnsDefault()
    {
        using var dir = new TempDir();
        using var options = new DbOptions { CreateIfMissing = true };

        using (var db = RocksDb.Open(options, dir.Path)) { }

        var families = RocksDb.ListColumnFamilies(options, dir.Path);
        Assert.Contains("default", families);
    }

    [Fact]
    public void GetDbIdentity_ReturnsNonEmpty()
    {
        using var db = new TempDb();

        string id = db.Db.GetDbIdentity();

        Assert.NotEmpty(id);
    }

    [Fact]
    public void Flush_DoesNotThrow()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");

        db.Db.Flush();
    }

    [Fact]
    public void FlushWithOptions_DoesNotThrow()
    {
        using var db = new TempDb();
        using var flushOpts = new FlushOptions { Wait = true };

        db.Db.Put("key", "value");
        db.Db.Flush(flushOpts);
    }

    [Fact]
    public void FlushWal_DoesNotThrow()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");

        db.Db.FlushWal(sync: false);
    }

    [Fact]
    public void CompactRange_DoesNotThrow()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("z", "2");
        db.Db.Flush();

        db.Db.CompactRange();
    }

    [Fact]
    public void CompactRangeWithOptions_DoesNotThrow()
    {
        using var db = new TempDb();
        using var opts = new CompactRangeOptions();

        db.Db.Put("a", "1");
        db.Db.Flush();

        db.Db.CompactRange(opts);
    }

    [Fact]
    public void DisableEnableFileDeletions_DoesNotThrow()
    {
        using var db = new TempDb();

        db.Db.DisableFileDeletions();
        db.Db.EnableFileDeletions();
    }

    [Fact]
    public void KeyMayExist_ReturnsTrueForExistingKey()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");
        db.Db.Flush();

        // KeyMayExist can return true for existing keys (no false negatives)
        // but may return true for non-existing keys (false positives)
        bool mayExist = db.Db.KeyMayExist(Encoding.UTF8.GetBytes("key"));
        Assert.True(mayExist);
    }
}
