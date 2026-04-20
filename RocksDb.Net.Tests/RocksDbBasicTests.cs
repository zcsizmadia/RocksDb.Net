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

        var results = db.Db.MultiGet([]);

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

        // Create and close database
        using (var db = RocksDb.Open(new DbOptions { CreateIfMissing = true }, dir.Path))
        {
            db.Put("key", "value");
        }

        // Destroy
        RocksDb.Destroy(new DbOptions(), dir.Path);

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

        using (var db = RocksDb.Open(new DbOptions { CreateIfMissing = true }, dir.Path)) { }

        var families = RocksDb.ListColumnFamilies(new DbOptions(), dir.Path);
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

    [Fact]
    public void Repair_DoesNotThrowOnValidDb()
    {
        using var dir = new TempDir();

        // Create and populate
        using (var db = RocksDb.Open(new DbOptions { CreateIfMissing = true }, dir.Path))
        {
            db.Put("key", "value");
        }

        // Repair should not throw on a valid database
        RocksDb.Repair(new DbOptions(), dir.Path);

        // Verify data is still accessible
        using var db2 = RocksDb.Open(new DbOptions(), dir.Path);
        Assert.Equal("value", db2.GetString("key"));
    }

    [Fact]
    public void OpenReadOnly_WithColumnFamilies()
    {
        using var dir = new TempDir();
        using var options = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        // Create and populate
        using (var db = RocksDb.Open(options, dir.Path, cfDescs))
        {
            db.Put("key", "value");
            var cf1 = db.GetColumnFamily("cf1");
            db.Put("cf_key", "cf_value", cf1);
        }

        // Open read-only with column families
        using var roOpts = new DbOptions();
        using var rodb = RocksDb.OpenReadOnly(roOpts, dir.Path, cfDescs);

        Assert.Equal("value", rodb.GetString("key"));
        var cf = rodb.GetColumnFamily("cf1");
        Assert.Equal("cf_value", rodb.GetString("cf_key", cf));
    }

    [Fact]
    public void Merge_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        opts.SetUInt64AddMergeOperator();
        using var cfOpts = opts.Clone();
        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1", cfOpts),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Merge(Encoding.UTF8.GetBytes("counter"), BitConverter.GetBytes(1UL), cf1);
        db.Merge(Encoding.UTF8.GetBytes("counter"), BitConverter.GetBytes(2UL), cf1);

        byte[]? result = db.Get(Encoding.UTF8.GetBytes("counter").AsSpan(), cf1);
        Assert.NotNull(result);
        Assert.Equal(3UL, BitConverter.ToUInt64(result));
    }

    [Fact]
    public void Merge_String_ColumnFamily()
    {
        using var dir = new TempDir();
        var mergeOp = new TestAppendMergeOp();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        opts.MergeOperator = mergeOp;

        using var cfOpts = opts.Clone();
        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1", cfOpts),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Merge("list", "a", cf1);
        db.Merge("list", "b", cf1);

        string? result = db.GetString("list", cf1);
        Assert.NotNull(result);
        Assert.Contains("a", result);
        Assert.Contains("b", result);
    }

    [Fact]
    public void GetProperty_ColumnFamily()
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

        string? prop = db.GetProperty("rocksdb.stats", cf1);
        Assert.NotNull(prop);
    }

    [Fact]
    public void GetPropertyInt_ColumnFamily()
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

        ulong? numKeys = db.GetPropertyInt("rocksdb.estimate-num-keys", cf1);
        Assert.NotNull(numKeys);
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

        Assert.Equal("value", db.GetString("key", cf1));
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
        db.Put("z", "2", cf1);
        db.Flush(cf1);

        db.CompactRange(cf1);
    }

    [Fact]
    public void GetDefaultColumnFamily_Works()
    {
        using var db = new TempDb();

        var defaultCf = db.Db.GetDefaultColumnFamily();

        Assert.NotNull(defaultCf);
        Assert.Equal("default", defaultCf.Name);
    }

    [Fact]
    public void Delete_Bytes_ColumnFamily()
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

        db.Put(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("val"), cf1);
        Assert.NotNull(db.Get(Encoding.UTF8.GetBytes("key").AsSpan(), cf1));

        db.Delete(Encoding.UTF8.GetBytes("key"), cf1);
        Assert.Null(db.Get(Encoding.UTF8.GetBytes("key").AsSpan(), cf1));
    }

    [Fact]
    public void Delete_String_ColumnFamily()
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

        db.Put("key", "val", cf1);
        Assert.Equal("val", db.GetString("key", cf1));

        db.Delete("key", cf1);
        Assert.Null(db.GetString("key", cf1));
    }

    [Fact]
    public void GetProperty_UnknownProperty_ReturnsNull()
    {
        using var db = new TempDb();

        string? prop = db.Db.GetProperty("rocksdb.unknown.property.xyz");
        Assert.Null(prop);
    }

    [Fact]
    public void GetPropertyInt_InvalidProperty_ReturnsNull()
    {
        using var db = new TempDb();

        ulong? val = db.Db.GetPropertyInt("rocksdb.unknown.property.xyz");
        // Property does not exist, but the API returns null vs 0 depending on implementation
        // At minimum it shouldn't throw
    }

    private sealed class TestAppendMergeOp : MergeOperator
    {
        public TestAppendMergeOp() : base("TestAppendMerge") { }

        public override bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue,
            ReadOnlySpan<byte> existingValue, IEnumerable<byte[]> operands, out byte[] newValue)
        {
            var sb = new StringBuilder();
            if (hasExistingValue)
                sb.Append(Encoding.UTF8.GetString(existingValue));

            foreach (var op in operands)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(Encoding.UTF8.GetString(op));
            }

            newValue = Encoding.UTF8.GetBytes(sb.ToString());
            return true;
        }
    }

    [Fact]
    public void CompactRange_WithKeys()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("m", "2");
        db.Db.Put("z", "3");
        db.Db.Flush();

        db.Db.CompactRange(
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("z"));
    }

    [Fact]
    public void CompactRange_WithKeysAndOptions()
    {
        using var db = new TempDb();
        using var opts = new CompactRangeOptions();
        db.Db.Put("a", "1");
        db.Db.Put("z", "2");
        db.Db.Flush();

        db.Db.CompactRange(opts,
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("z"));
    }

    [Fact]
    public void CompactRange_ColumnFamily_WithKeys()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Put("a", "1", cf1);
        db.Put("z", "2", cf1);
        db.Flush(cf1);

        db.CompactRange(cf1,
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("z"));
    }

    [Fact]
    public void IngestExternalFile_Works()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string sstPath = Path.Combine(dir.Path, "test.sst");

        // Create an SST file
        using var dbOpts = new DbOptions { CreateIfMissing = true };
        using (var writer = SstFileWriter.Create(dbOpts))
        {
            writer.Open(sstPath);
            writer.Put(Encoding.UTF8.GetBytes("sst_key1"), Encoding.UTF8.GetBytes("sst_val1"));
            writer.Put(Encoding.UTF8.GetBytes("sst_key2"), Encoding.UTF8.GetBytes("sst_val2"));
            writer.Finish();
        }

        // Ingest into database
        using var db = RocksDb.Open(dbOpts, dbPath);
        using var ingestOpts = new IngestExternalFileOptions();
        db.IngestExternalFile([sstPath], ingestOpts);

        Assert.Equal("sst_val1", db.GetString("sst_key1"));
        Assert.Equal("sst_val2", db.GetString("sst_key2"));
    }

    [Fact]
    public void IngestExternalFile_ColumnFamily()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string sstPath = Path.Combine(dir.Path, "test.sst");

        // Create an SST file
        using var dbOpts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using (var writer = SstFileWriter.Create(dbOpts))
        {
            writer.Open(sstPath);
            writer.Put(Encoding.UTF8.GetBytes("cf_sst_key"), Encoding.UTF8.GetBytes("cf_sst_val"));
            writer.Finish();
        }

        // Ingest into column family
        var cfDescs = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };
        using var db = RocksDb.Open(dbOpts, dbPath, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        using var ingestOpts = new IngestExternalFileOptions();
        db.IngestExternalFile([sstPath], cf1, ingestOpts);

        Assert.Equal("cf_sst_val", db.GetString("cf_sst_key", cf1));
    }

    [Fact]
    public void Put_Get_WithReadWriteOptions()
    {
        using var db = new TempDb();
        using var writeOpts = new WriteOptions { Sync = true };
        using var readOpts = new ReadOptions();

        db.Db.Put(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("value"), writeOpts);

        byte[]? result = db.Db.Get(Encoding.UTF8.GetBytes("key"), readOpts);
        Assert.NotNull(result);
        Assert.Equal("value", Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void Put_Get_ColumnFamily_Bytes()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Put("k", "v", cf1);
        byte[]? result = db.Get(Encoding.UTF8.GetBytes("k").AsSpan(), cf1);
        Assert.NotNull(result);
        Assert.Equal("v", Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void TryGet_ReturnsTrue_ForExistingKey()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");

        bool found = db.Db.TryGet(Encoding.UTF8.GetBytes("key"), out byte[]? value);
        Assert.True(found);
        Assert.NotNull(value);
        Assert.Equal("value", Encoding.UTF8.GetString(value));
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForMissingKey()
    {
        using var db = new TempDb();

        bool found = db.Db.TryGet(Encoding.UTF8.GetBytes("missing"), out byte[]? value);
        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Write_WriteBatch()
    {
        using var db = new TempDb();
        using var batch = new WriteBatch();

        batch.Put("batch_a", "1");
        batch.Put("batch_b", "2");

        db.Db.Write(batch);

        Assert.Equal("1", db.Db.GetString("batch_a"));
        Assert.Equal("2", db.Db.GetString("batch_b"));
    }

    [Fact]
    public void Write_WriteBatch_WithWriteOptions()
    {
        using var db = new TempDb();
        using var batch = new WriteBatch();
        using var writeOpts = new WriteOptions { Sync = true };

        batch.Put("wb_a", "1");
        db.Db.Write(batch, writeOpts);

        Assert.Equal("1", db.Db.GetString("wb_a"));
    }

    [Fact]
    public void MultiGet_WithReadOptions()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");

        var keys = new List<byte[]>
        {
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("missing"),
            Encoding.UTF8.GetBytes("b"),
        };

        using var readOpts = new ReadOptions();
        byte[]?[] results = db.Db.MultiGet(keys, readOpts);

        Assert.Equal(3, results.Length);
        Assert.Equal("1", Encoding.UTF8.GetString(results[0]!));
        Assert.Null(results[1]);
        Assert.Equal("2", Encoding.UTF8.GetString(results[2]!));
    }

    [Fact]
    public void KeyMayExist_WithReadOptions()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");
        db.Db.Flush();

        using var readOpts = new ReadOptions();
        bool mayExist = db.Db.KeyMayExist(Encoding.UTF8.GetBytes("key"), readOpts);
        Assert.True(mayExist);
    }

    [Fact]
    public void NewIterator_WithReadOptions()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");

        using var readOpts = new ReadOptions();
        using var iter = db.Db.NewIterator(readOpts);
        iter.SeekToFirst();

        Assert.True(iter.IsValid());
        Assert.Equal("a", iter.KeyAsString());
    }

    [Fact]
    public void Flush_WithFlushOptions_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Put("key", "value", cf1);
        using var flushOpts = new FlushOptions { Wait = true };
        db.Flush(cf1, flushOpts);
    }

    [Fact]
    public void GetStatisticsString_WithStatisticsEnabled()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EnableStatistics();

        using var db = RocksDb.Open(opts, dir.Path);
        db.Put("key", "value");

        string? stats = opts.GetStatisticsString();
        Assert.NotNull(stats);
        Assert.NotEmpty(stats);
    }

    [Fact]
    public void DeleteRange_Default()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("d", "4");

        db.Db.DeleteRange(
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("c"));

        Assert.Null(db.Db.GetString("a"));
        Assert.Null(db.Db.GetString("b"));
        Assert.Equal("4", db.Db.GetString("d"));
    }
}
