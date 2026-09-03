using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// The borrowed-view cases from issue #58. Each type used to read through a
/// native parent on every access, so an instance was only valid while that
/// parent lived. They are now read in full, and these tests assert exactly
/// that by using the values after the source is gone.
/// </summary>
public class BorrowedViewTests
{
    // ── Column family metadata ──────────────────────────────────────────────

    [Fact]
    public void ColumnFamilyMetadata_SurvivesTheDatabaseBeingClosed()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };

        ColumnFamilyMetadata? metadata;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            for (int i = 0; i < 50; i++)
            {
                db.Put($"key{i:D3}", "value");
            }

            db.Flush();

            metadata = db.GetColumnFamilyMetadata();
            Assert.NotNull(metadata);
            Assert.NotEmpty(metadata.Levels);
        }

        // The database is closed and its native metadata handle destroyed.
        // Reading these used to walk freed memory.
        Assert.Equal("default", metadata.Name);
        Assert.True(metadata.FileCount > 0);
        Assert.NotEmpty(metadata.Levels);

        ColumnFamilyLevelMetadata level0 = metadata.Levels.Single(l => l.Level == 0);
        Assert.NotEmpty(level0.Files);

        SstFileMetadata file = level0.Files[0];
        Assert.False(string.IsNullOrEmpty(file.RelativeFilename));
        Assert.True(file.Size > 0);
        Assert.NotNull(file.SmallestKey);
        Assert.NotNull(file.LargestKey);
    }

    /// <summary>
    /// And it survives a collection too, which is the accident rather than the
    /// deliberate case.
    /// </summary>
    [Fact]
    public void ColumnFamilyMetadata_SurvivesACollection()
    {
        using var db = new TempDb();

        db.Db.Put("key", "value");
        db.Db.Flush();

        ColumnFamilyMetadata? metadata = db.Db.GetColumnFamilyMetadata();
        Assert.NotNull(metadata);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal("default", metadata.Name);
        Assert.NotEmpty(metadata.Levels);
    }

    [Fact]
    public void ColumnFamilyMetadata_NeedsNoDisposal()
    {
        using var db = new TempDb();

        // A record, not a handle. If this still implemented IDisposable the
        // caller would have to know when the graph stopped being valid.
        Assert.False(typeof(ColumnFamilyMetadata).IsAssignableTo(typeof(IDisposable)));
        Assert.False(typeof(ColumnFamilyLevelMetadata).IsAssignableTo(typeof(IDisposable)));
        Assert.False(typeof(SstFileMetadata).IsAssignableTo(typeof(IDisposable)));
        Assert.False(typeof(LiveFileMetadata).IsAssignableTo(typeof(IDisposable)));
    }

    // ── Live files ──────────────────────────────────────────────────────────

    [Fact]
    public void LiveFileMetadata_SurvivesTheDatabaseBeingClosed()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };

        IReadOnlyList<LiveFileMetadata> files;

        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("key", "value");
            db.Flush();

            files = db.GetLiveFiles();
            Assert.NotEmpty(files);
        }

        LiveFileMetadata file = files[0];
        Assert.False(string.IsNullOrEmpty(file.Name));
        Assert.False(string.IsNullOrEmpty(file.Directory));
        Assert.True(file.Size > 0);
        Assert.True(file.Entries > 0);
        Assert.NotNull(file.SmallestKey);
    }

    /// <summary>
    /// An empty result is an empty list rather than null, so a caller does not
    /// have to null-check before enumerating.
    /// </summary>
    [Fact]
    public void GetLiveFiles_OnAnEmptyDatabase_ReturnsAnEmptyList()
    {
        using var db = new TempDb();

        IReadOnlyList<LiveFileMetadata> files = db.Db.GetLiveFiles();

        Assert.Empty(files);
    }

    // ── Merge operands ──────────────────────────────────────────────────────

    /// <summary>
    /// RocksDb builds the operand arrays as call-scoped locals. The wrapper
    /// used to yield over them lazily, so an operator that stored the sequence
    /// and enumerated it later read freed memory. They are now materialised, so
    /// storing them is safe.
    /// </summary>
    private sealed class HoardingMergeOperator : MergeOperator
    {
        public HoardingMergeOperator()
            : base("test.hoarding")
        {
        }

        // Deliberately kept beyond the callback, which is the hazard.
        public IReadOnlyList<byte[]>? Kept;

        public override bool FullMerge(
            ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue,
            IReadOnlyList<byte[]> operands, out byte[] newValue)
        {
            Kept = operands;

            var joined = new List<byte>();
            if (hasExistingValue)
            {
                joined.AddRange(existingValue);
            }

            foreach (byte[] operand in operands)
            {
                joined.AddRange(operand);
            }

            newValue = [.. joined];
            return true;
        }
    }

    [Fact]
    public void MergeOperands_CanBeReadAfterTheCallbackReturns()
    {
        using var dir = new TempDir();
        var op = new HoardingMergeOperator();

        var opts = new DbOptions { CreateIfMissing = true };
        opts.MergeOperator = op;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Merge("key", "a");
        db.Merge("key", "b");
        db.Merge("key", "c");

        Assert.Equal("abc", db.GetString("key"));

        // Force a collection between the callback and the read, so a lazy
        // sequence over native locals would have nothing valid left.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        Assert.NotNull(op.Kept);
        Assert.NotEmpty(op.Kept);

        // The operands are managed copies, so they still read correctly.
        string[] kept = [.. op.Kept.Select(o => Encoding.UTF8.GetString(o))];
        Assert.All(kept, s => Assert.False(string.IsNullOrEmpty(s)));
    }

    /// <summary>
    /// The list shape also means an operator can count and index without
    /// enumerating, which an <c>IEnumerable</c> could not offer.
    /// </summary>
    private sealed class LastWriteWinsMergeOperator : MergeOperator
    {
        public LastWriteWinsMergeOperator()
            : base("test.lastwins")
        {
        }

        public int LastOperandCount;

        public override bool FullMerge(
            ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue,
            IReadOnlyList<byte[]> operands, out byte[] newValue)
        {
            LastOperandCount = operands.Count;
            newValue = operands.Count > 0 ? operands[^1] : existingValue.ToArray();
            return true;
        }
    }

    [Fact]
    public void MergeOperands_ExposeCountAndIndexer()
    {
        using var dir = new TempDir();
        var op = new LastWriteWinsMergeOperator();

        var opts = new DbOptions { CreateIfMissing = true };
        opts.MergeOperator = op;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Merge("key", "first");
        db.Merge("key", "second");
        db.Merge("key", "third");

        Assert.Equal("third", db.GetString("key"));
        Assert.True(op.LastOperandCount > 0);
    }
}
