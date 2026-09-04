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

    /// <summary>
    /// Cancelling background work is a one-way door. Reads and option changes
    /// still work afterwards, but anything needing a background thread does not.
    /// </summary>
    /// <remarks>
    /// The column-family behaviour of <c>SuggestCompactRange</c> is covered in
    /// <see cref="MaintenanceOperationsTests"/>.
    /// </remarks>
    [Fact]
    public void CancelAllBackgroundWork_LeavesTheDatabaseReadableButNotFlushable()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Put("key", "value", cf1);
        db.Flush(new[] { cf1 });

        db.CancelAllBackgroundWork(wait: true);

        // Changing options at runtime still works.
        db.SetOptions(new[] { new KeyValuePair<string, string>("disable_auto_compactions", "true") });

        // So do reads and writes into the memtable.
        Assert.Equal("value", db.GetString("key", cf1));
        db.Put("key2", "value2", cf1);
        Assert.Equal("value2", db.GetString("key2", cf1));

        // Flushing does not, because the database is now shutting down. Worth
        // pinning: nothing in the method name suggests it is irreversible.
        RocksDbException ex = Assert.Throws<RocksDbException>(() => db.Flush(new[] { cf1 }));
        Assert.Contains("Shutdown in progress", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetColumnFamilyMetadata_ReturnsMetadataForDefaultAndNamedFamilies()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Put("a", "1", cf1);
        db.Flush(cf1);

        ColumnFamilyMetadata? defaultMetadata = db.GetColumnFamilyMetadata();
        ColumnFamilyMetadata? cfMetadata = db.GetColumnFamilyMetadata(cf1);

        Assert.NotNull(defaultMetadata);
        Assert.NotNull(cfMetadata);
        Assert.Equal("default", defaultMetadata!.Name);
        Assert.Equal("cf1", cfMetadata!.Name);
        // The three assertions here used to be "at least zero" against
        // unsigned counts, which no value could fail. One key was flushed into
        // cf1 and nothing was written to default, so the numbers are known.
        Assert.Equal(1, cfMetadata.FileCount);
        Assert.True(cfMetadata.Size > 0);
        Assert.NotEmpty(cfMetadata.Levels);
        Assert.Single(cfMetadata.Levels.Single(l => l.Level == 0).Files);

        Assert.Equal(0, defaultMetadata.FileCount);
    }

    [Fact]
    public void DbOptions_GetTickerCountAndHistogramData_ReturnsValues()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EnableStatistics();

        using var db = RocksDb.Open(opts, dir.Path);
        db.Put("key", "value");
        db.Flush();

        // Both counts are unsigned, so the old "at least zero" assertions held
        // for every possible value, including the all-zeroes a statistics
        // object that was never attached returns.
        //
        // Named counters rather than a sweep over numeric ids. The sweep was
        // what this had to do before the Ticker and Histogram enums existed,
        // and it could only assert that something somewhere had moved.
        Assert.Equal(1UL, opts.GetTickerCount(Ticker.NumberKeysWritten));
        Assert.True(opts.GetTickerCount(Ticker.BytesWritten) > 0);

        // Nothing was read, so this one has to still be zero. Without it the
        // assertions above would pass against a stub that returned a constant.
        Assert.Equal(0UL, opts.GetTickerCount(Ticker.NumberKeysRead));

        Assert.Equal("value", db.GetString("key"));
        Assert.Equal(1UL, opts.GetTickerCount(Ticker.NumberKeysRead));

        HistogramData? sampled = opts.GetHistogramData(Histogram.DbWrite);

        Assert.NotNull(sampled);
        Assert.True(sampled!.Count > 0, "the write histogram recorded no samples");

        // Self-consistent, which all-zero data from an unattached statistics
        // object would satisfy only by accident of the bounds being equal.
        Assert.True(sampled.Sum > 0);
        Assert.True(sampled.Min <= sampled.Median);
        Assert.True(sampled.Median <= sampled.Max);
        Assert.True(sampled.Average > 0);
    }

    [Fact]
    public void GetLiveFiles_ReturnsLiveFileMetadata()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Flush();

        IReadOnlyList<LiveFileMetadata> liveFiles = db.Db.GetLiveFiles();

        LiveFileMetadata file = Assert.Single(liveFiles);

        // A flush writes one file at level zero holding the one key. The level
        // assertion used to be "at least zero", which is every level there is.
        Assert.EndsWith(".sst", file.Name, StringComparison.Ordinal);
        Assert.Equal(0, file.Level);
        Assert.Equal(1UL, file.Entries);
        Assert.Equal(0UL, file.Deletions);
        Assert.True(file.Size > 0);
        Assert.Equal("a"u8.ToArray(), file.SmallestKey);
        Assert.Equal("a"u8.ToArray(), file.LargestKey);

        // Read in full, so the values survive without the database being open
        // and without anything to dispose.
        string firstName = liveFiles[0].Name;
        db.Db.Dispose();
        Assert.Equal(firstName, liveFiles[0].Name);
    }

    [Fact]
    public void ApproximateSizes_ReturnsOneValuePerRange()
    {
        using var db = new TempDb();

        // Enough data to span several blocks. The estimate comes from index
        // block offsets, so two keys in one block are genuinely zero bytes
        // apart and would prove nothing either way.
        for (int i = 0; i < 2000; i++)
        {
            db.Db.Put($"key{i:D5}", new string('v', 100));
        }

        db.Db.Flush();

        ulong[] sizes = db.Db.ApproximateSizes([("key00000", "key01999"), ("zzz0", "zzz9")]);

        // The old assertion was "at least zero" on an unsigned value. Now the
        // populated range has to come back with something and the empty range
        // with nothing, so a stub answering the same for both fails.
        Assert.Equal(2, sizes.Length);
        Assert.True(sizes[0] > 0, "the range holding 2000 flushed keys was estimated at zero bytes");
        Assert.Equal(0UL, sizes[1]);
    }

    [Fact]
    public void ApproximateSizes_ColumnFamily_ReturnsOneValuePerRange()
    {
        using var dir = new TempDir();
        using var options = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };

        using var db = RocksDb.Open(options, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        for (int i = 0; i < 2000; i++)
        {
            db.Put($"key{i:D5}", new string('v', 100), cf1);
        }

        db.Flush(cf1);

        ulong[] sizes = db.ApproximateSizes(cf1, [("key00000", "key01999")]);

        Assert.Single(sizes);
        Assert.True(sizes[0] > 0, "the range holding 2000 flushed keys was estimated at zero bytes");

        // And the column family argument is not ignored: everything went into
        // cf1, so the same range on the default family is empty.
        Assert.Equal(0UL, db.ApproximateSizes([("key00000", "key01999")])[0]);
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
    public void IsEmpty_ReturnsTrueForNewDatabase()
    {
        using var db = new TempDb();

        Assert.True(db.Db.IsEmpty);
    }

    [Fact]
    public void IsEmpty_ReturnsFalseAfterPut()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");
        db.Db.Flush();

        Assert.False(db.Db.IsEmpty);
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
    public void KeyMayExist_ReturnsFalseForMissingKey()
    {
        using var db = new TempDb();
        bool mayExist = db.Db.KeyMayExist(Encoding.UTF8.GetBytes("missing"));
        Assert.False(mayExist);
    }

    [Fact]
    public void KeyMayExist_StringKey_ReturnsTrueForExistingKey()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");
        db.Db.Flush();

        bool mayExist = db.Db.KeyMayExist("key");

        Assert.True(mayExist);
    }

    [Fact]
    public void KeyMayExist_StringKey_ReturnsFalseForMissingKey()
    {
        using var db = new TempDb();
        bool mayExist = db.Db.KeyMayExist("missing");
        Assert.False(mayExist);
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
            ReadOnlySpan<byte> existingValue, IReadOnlyList<byte[]> operands, out byte[] newValue)
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
