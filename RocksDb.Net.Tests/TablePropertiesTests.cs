namespace RocksDbNet.Tests;

/// <summary>
/// Covers <see cref="TableProperties"/>, <see cref="CompactionJobStats"/> and the
/// blob-file info records. See issue #27.
/// </summary>
/// <remarks>
/// These types wrap borrowed native views that are only valid during a listener
/// callback, so every test reads them after the callback has returned. That is
/// the point: if the copy were not eager, these assertions would read freed
/// memory.
/// </remarks>
public class TablePropertiesTests
{
    private sealed class CapturingListener : EventListener
    {
        private readonly Lock _gate = new();
        private readonly List<FlushJobInfo> _flushes = [];
        private readonly List<CompactionJobInfo> _compactions = [];

        public override void OnFlushCompleted(FlushJobInfo info)
        {
            lock (_gate)
            {
                _flushes.Add(info);
            }
        }

        public override void OnCompactionCompleted(CompactionJobInfo info)
        {
            lock (_gate)
            {
                _compactions.Add(info);
            }
        }

        public IReadOnlyList<FlushJobInfo> Flushes
        {
            get { lock (_gate) { return [.. _flushes]; } }
        }

        public IReadOnlyList<CompactionJobInfo> Compactions
        {
            get { lock (_gate) { return [.. _compactions]; } }
        }
    }

    [Fact]
    public void FlushJobInfo_TableProperties_DescribeTheFlushedFile()
    {
        using var dir = new TempDir();
        var listener = new CapturingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            for (int i = 0; i < 10; i++)
            {
                db.Put($"key{i:D3}", $"value{i}");
            }

            db.Flush();
        }

        FlushJobInfo flush = Assert.Single(listener.Flushes);
        TableProperties props = Assert.IsType<TableProperties>(flush.TableProperties);

        Assert.Equal(10UL, props.NumEntries);
        Assert.Equal(0UL, props.NumDeletions);
        Assert.Equal(0UL, props.NumMergeOperands);
        Assert.True(props.RawKeySize > 0);
        Assert.True(props.RawValueSize > 0);
        Assert.True(props.DataSize > 0);
        Assert.True(props.NumDataBlocks > 0);
        Assert.Equal("default", props.ColumnFamilyName);
        Assert.Equal("leveldb.BytewiseComparator", props.ComparatorName);
        Assert.False(string.IsNullOrEmpty(props.CompressionName));

        // RocksDb records some internal properties of its own, so this map is
        // populated even with no user collector registered.
        Assert.NotEmpty(props.UserCollectedProperties);
        Assert.All(props.UserCollectedProperties.Keys, key => Assert.False(string.IsNullOrEmpty(key)));

        // ReadableProperties is fed only by a collector's GetReadableProperties,
        // which the built-in internal properties do not implement, so it is
        // legitimately empty here. Reading it must still be safe.
        Assert.Empty(props.ReadableProperties);
    }

    [Fact]
    public void FlushJobInfo_TableProperties_CountsDeletionsAndMergeOperands()
    {
        using var dir = new TempDir();
        var listener = new CapturingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;
        opts.SetUInt64AddMergeOperator();

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("a", "1");
            db.Delete("b");
            db.Merge("c"u8, BitConverter.GetBytes(1UL));
            db.Flush();
        }

        FlushJobInfo flush = Assert.Single(listener.Flushes);
        TableProperties props = Assert.IsType<TableProperties>(flush.TableProperties);

        Assert.Equal(3UL, props.NumEntries);
        Assert.Equal(1UL, props.NumDeletions);
        Assert.Equal(1UL, props.NumMergeOperands);
    }

    [Fact]
    public void CompactionJobInfo_Stats_DescribeTheCompaction()
    {
        using var dir = new TempDir();
        var listener = new CapturingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            // Two flushes produce two L0 files covering the SAME keys, so the
            // compaction has to merge them. Disjoint files would be trivially
            // moved instead, and RocksDb populates almost no stats for a move.
            db.Put("a", "1");
            db.Put("b", "2");
            db.Flush();
            db.Put("a", "1-updated");
            db.Put("b", "2-updated");
            db.Flush();

            db.CompactRange();
        }

        Assert.NotEmpty(listener.Compactions);
        CompactionJobInfo compaction = listener.Compactions[0];
        CompactionJobStats stats = Assert.IsType<CompactionJobStats>(compaction.Stats);

        Assert.Equal(2UL, stats.NumInputFiles);
        Assert.Equal(1UL, stats.NumOutputFiles);
        Assert.Equal(4UL, stats.NumInputRecords);
        Assert.Equal(2UL, stats.NumOutputRecords);
        Assert.Equal(2UL, stats.NumRecordsReplaced);
        Assert.True(stats.IsManualCompaction);
        Assert.False(stats.IsRemoteCompaction);
        Assert.True(stats.TotalInputBytes > 0);
        Assert.True(stats.TotalOutputBytes > 0);
        Assert.Equal(0UL, stats.NumCorruptKeys);
    }

    [Fact]
    public void FlushJobInfo_BlobFileAdditions_ReportedWhenBlobFilesEnabled()
    {
        using var dir = new TempDir();
        var listener = new CapturingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;
        opts.EnableBlobFiles = true;
        opts.MinBlobSize = 0; // Send every value to a blob file.

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("a", "a-reasonably-sized-value");
            db.Put("b", "another-reasonably-sized-value");
            db.Flush();
        }

        FlushJobInfo flush = Assert.Single(listener.Flushes);
        BlobFileAdditionInfo blob = Assert.Single(flush.BlobFileAdditions);

        Assert.False(string.IsNullOrEmpty(blob.BlobFilePath));
        Assert.True(blob.BlobFileNumber > 0);
        Assert.Equal(2UL, blob.TotalBlobCount);
        Assert.True(blob.TotalBlobBytes > 0);
    }

    [Fact]
    public void FlushJobInfo_BlobFileAdditions_EmptyWhenBlobFilesDisabled()
    {
        using var dir = new TempDir();
        var listener = new CapturingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("a", "1");
            db.Flush();
        }

        FlushJobInfo flush = Assert.Single(listener.Flushes);
        Assert.Empty(flush.BlobFileAdditions);
        Assert.Empty(flush.BlobFileAdditions);
    }

    [Fact]
    public void CompactionJobInfo_BlobFileGarbage_ReportedWhenBlobsAreOverwritten()
    {
        using var dir = new TempDir();
        var listener = new CapturingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;
        opts.EnableBlobFiles = true;
        opts.MinBlobSize = 0;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("a", "first-value-written-to-a-blob-file");
            db.Put("b", "another-first-value-in-a-blob-file");
            db.Flush();

            // Overwriting both keys makes the blobs in the first file
            // unreachable, and the overlapping key range forces a real merge
            // rather than a trivial move.
            db.Put("a", "second-value-written-to-a-blob-file");
            db.Put("b", "another-second-value-in-a-blob-file");
            db.Flush();

            db.CompactRange();
        }

        Assert.NotEmpty(listener.Compactions);
        Assert.Contains(listener.Compactions, c => c.BlobFileGarbage.Count > 0);

        BlobFileGarbageInfo garbage = listener.Compactions
            .SelectMany(c => c.BlobFileGarbage)
            .First();

        Assert.False(string.IsNullOrEmpty(garbage.BlobFilePath));
        Assert.True(garbage.BlobFileNumber > 0);
        Assert.True(garbage.GarbageBlobCount > 0);
        Assert.True(garbage.GarbageBlobBytes > 0);
    }

    [Fact]
    public void TableProperties_SurviveTheCallbackReturning()
    {
        // The native struct is a borrowed view into the flush-info object, so it
        // is dead by the time the database is closed. If the copy were lazy this
        // would read freed memory.
        using var dir = new TempDir();
        var listener = new CapturingListener();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.EventListener = listener;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("a", "1");
            db.Flush();
        }

        // Database and options are gone; the snapshot must still read correctly.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        TableProperties props = Assert.IsType<TableProperties>(Assert.Single(listener.Flushes).TableProperties);
        Assert.Equal(1UL, props.NumEntries);
        Assert.Equal("default", props.ColumnFamilyName);
    }
}
