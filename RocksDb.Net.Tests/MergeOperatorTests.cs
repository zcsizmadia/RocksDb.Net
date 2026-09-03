using System.Text;

using RocksDbNet.Extensions;

namespace RocksDbNet.Tests;

public class MergeOperatorTests
{
    private sealed class NameValidatingMergeOperator(string name) : MergeOperator(name)
    {
        public override bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue,
            ReadOnlySpan<byte> existingValue, IReadOnlyList<byte[]> operands, out byte[] newValue)
        {
            newValue = [];
            return true;
        }
    }

    private sealed class NoPartialOverrideMergeOperator() : MergeOperator("NoPartialOverride")
    {
        public override bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue,
            ReadOnlySpan<byte> existingValue, IReadOnlyList<byte[]> operands, out byte[] newValue)
        {
            newValue = [];
            return true;
        }
    }

    [Fact]
    public void UInt64AddMergeOperator_Works()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetUInt64AddMergeOperator();

        using var db = RocksDb.Open(opts, dir.Path);

        byte[] key = Encoding.UTF8.GetBytes("counter");
        byte[] one = BitConverter.GetBytes(1UL);
        byte[] two = BitConverter.GetBytes(2UL);
        byte[] three = BitConverter.GetBytes(3UL);

        db.Merge(key, one);
        db.Merge(key, two);
        db.Merge(key, three);

        byte[]? result = db.Get(key.AsSpan());
        Assert.NotNull(result);

        ulong merged = BitConverter.ToUInt64(result);
        Assert.Equal(6UL, merged);
    }

    [Fact]
    public void UInt64AddMergeOperator_WithExistingValue()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetUInt64AddMergeOperator();

        using var db = RocksDb.Open(opts, dir.Path);

        byte[] key = Encoding.UTF8.GetBytes("counter");
        byte[] initial = BitConverter.GetBytes(10UL);

        db.Put(key, initial);
        db.Merge(key, BitConverter.GetBytes(5UL));

        byte[]? result = db.Get(key.AsSpan());
        Assert.NotNull(result);
        Assert.Equal(15UL, BitConverter.ToUInt64(result));
    }

    private sealed class AppendMergeOperator : MergeOperator
    {
        public AppendMergeOperator() : base("AppendMerge") { }

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
    public void CustomMergeOperator_Works()
    {
        using var dir = new TempDir();
        var mergeOp = new AppendMergeOperator();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.MergeOperator = mergeOp;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Merge("list", "a");
        db.Merge("list", "b");
        db.Merge("list", "c");

        string? result = db.GetString("list");
        Assert.Equal("a,b,c", result);
    }

    [Fact]
    public void CustomMergeOperator_WithExisting()
    {
        using var dir = new TempDir();
        var mergeOp = new AppendMergeOperator();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.MergeOperator = mergeOp;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Put("list", "existing");
        db.Merge("list", "new");

        string? result = db.GetString("list");
        Assert.Equal("existing,new", result);
    }

    [Fact]
    public void Merge_String_Convenience()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetUInt64AddMergeOperator();

        using var db = RocksDb.Open(opts, dir.Path);

        db.Merge(Encoding.UTF8.GetBytes("k"), BitConverter.GetBytes(1UL));

        byte[]? result = db.Get("k");
        Assert.NotNull(result);
    }

    private sealed class PartialMergeOperator : MergeOperator
    {
        public PartialMergeOperator() : base("PartialAppendMerge") { }

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

        public override bool PartialMerge(ReadOnlySpan<byte> key, IReadOnlyList<byte[]> operands, out byte[] newValue)
        {
            var sb = new StringBuilder();
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
    public void PartialMerge_OverrideDetected()
    {
        var mergeOp = new PartialMergeOperator();
        Assert.True(mergeOp.CheckIfMethodOverridden<MergeOperator>(nameof(MergeOperator.PartialMerge)));
    }

    [Fact]
    public void PartialMerge_IsCalledDuringCompaction()
    {
        using var dir = new TempDir();
        var mergeOp = new PartialMergeOperator();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.MergeOperator = mergeOp;

        using var db = RocksDb.Open(opts, dir.Path);

        db.Merge("list", "a");
        db.Merge("list", "b");
        db.Merge("list", "c");

        // Force compaction to trigger partial merge
        db.Flush();
        db.CompactRange();

        string? result = db.GetString("list");
        Assert.NotNull(result);
        // The result should contain all operands
        Assert.Contains("a", result);
        Assert.Contains("b", result);
        Assert.Contains("c", result);
    }

    [Fact]
    public void MergeOperator_Ctor_ThrowsOnNullOrEmptyName()
    {
        Assert.Throws<ArgumentException>(() => new NameValidatingMergeOperator(""));
        Assert.Throws<ArgumentNullException>(() => new NameValidatingMergeOperator(null!));
    }

    [Fact]
    public void MergeOperator_DefaultPartialMerge_ReturnsFalseAndEmpty()
    {
        using var mergeOp = new NoPartialOverrideMergeOperator();

        bool ok = mergeOp.PartialMerge(
            key: "k"u8,
            operands: [Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("b")],
            out byte[] value);

        Assert.False(ok);
        Assert.Empty(value);
    }

    [Fact]
    public void MergeOperator_NoPartialOverrideDetected()
    {
        var mergeOp = new NoPartialOverrideMergeOperator();
        Assert.False(mergeOp.CheckIfMethodOverridden<MergeOperator>(nameof(MergeOperator.PartialMerge)));
    }

    /// <summary>
    /// A merge operator that does not override <see cref="MergeOperator.PartialMerge"/>
    /// must still survive a flush.
    /// </summary>
    /// <remarks>
    /// RocksDb calls the partial-merge slot whenever it reaches the end of a
    /// key's operand stack without a Put or Delete, which a flush of two or more
    /// operands for one key always does. It invokes that slot without a null
    /// check, unlike the delete-value slot beside it, so leaving it unset
    /// terminated the process rather than falling back to a full merge.
    /// </remarks>
    [Fact]
    public void MergeOperator_WithoutPartialMergeOverride_SurvivesFlushAndCompaction()
    {
        using var mergeOp = new AppendMergeOperator();
        using var db = new TempDb(o => o.MergeOperator = mergeOp);

        // Two operands for one key, so the flush has a stack to collapse.
        db.Db.Merge("k", "a");
        db.Db.Merge("k", "b");
        db.Db.Flush();

        db.Db.Merge("k", "c");
        db.Db.Merge("k", "d");
        db.Db.Flush();
        db.Db.CompactRange();

        Assert.Equal("a,b,c,d", db.Db.GetString("k"));
    }
}
