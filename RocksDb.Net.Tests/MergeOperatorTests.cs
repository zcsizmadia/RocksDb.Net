using System.Text;


namespace RocksDbNet.Tests;

public class MergeOperatorTests
{
    private sealed class NameValidatingMergeOperator(string name) : MergeOperator(name)
    {
        public override bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue,
            ReadOnlySpan<byte> existingValue, IReadOnlyList<byte[]> operands, out byte[]? newValue)
        {
            newValue = [];
            return true;
        }
    }

    private sealed class NoPartialOverrideMergeOperator() : MergeOperator("NoPartialOverride")
    {
        public override bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue,
            ReadOnlySpan<byte> existingValue, IReadOnlyList<byte[]> operands, out byte[]? newValue)
        {
            newValue = [];
            return true;
        }
    }

    [Fact]
    public void UInt64AddMergeOperator_Works()
    {
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetUInt64AddMergeOperator();

        using var db = TestDb.OpenInMemory(opts);

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
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetUInt64AddMergeOperator();

        using var db = TestDb.OpenInMemory(opts);

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
            ReadOnlySpan<byte> existingValue, IReadOnlyList<byte[]> operands, out byte[]? newValue)
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
        var mergeOp = new AppendMergeOperator();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.MergeOperator = mergeOp;

        using var db = TestDb.OpenInMemory(opts);

        db.Merge("list", "a");
        db.Merge("list", "b");
        db.Merge("list", "c");

        string? result = db.GetString("list");
        Assert.Equal("a,b,c", result);
    }

    [Fact]
    public void CustomMergeOperator_WithExisting()
    {
        var mergeOp = new AppendMergeOperator();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.MergeOperator = mergeOp;

        using var db = TestDb.OpenInMemory(opts);

        db.Put("list", "existing");
        db.Merge("list", "new");

        string? result = db.GetString("list");
        Assert.Equal("existing,new", result);
    }

    [Fact]
    public void Merge_String_Convenience()
    {
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetUInt64AddMergeOperator();

        using var db = TestDb.OpenInMemory(opts);

        db.Merge(Encoding.UTF8.GetBytes("k"), BitConverter.GetBytes(1UL));

        byte[]? result = db.Get("k");
        Assert.NotNull(result);
    }

    private sealed class PartialMergeOperator : MergeOperator
    {
        public PartialMergeOperator() : base("PartialAppendMerge") { }

        public override bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue,
            ReadOnlySpan<byte> existingValue, IReadOnlyList<byte[]> operands, out byte[]? newValue)
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

        private int _partialMergeCalls;

        /// <summary>How many times RocksDb asked for a partial merge.</summary>
        public int PartialMergeCalls => Volatile.Read(ref _partialMergeCalls);

        public override bool PartialMerge(ReadOnlySpan<byte> key, IReadOnlyList<byte[]> operands, out byte[]? newValue)
        {
            // Compaction runs on a background thread.
            Interlocked.Increment(ref _partialMergeCalls);

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
    public void PartialMerge_OverrideIsCalledRatherThanTheBase()
    {
        using var mergeOp = new PartialMergeOperator();

        // Was a reflection check that the method had been overridden, using a
        // helper that has now gone: nothing in the library detects overrides any
        // more, and MergeOperator never did — it always installs the slot and
        // lets the base return false. So assert the override is reached, which
        // is the part that mattered.
        Assert.True(
            mergeOp.PartialMerge("k"u8, [[1], [2]], out byte[]? value),
            "the override should combine the operands");

        Assert.NotNull(value);
    }

    [Fact]
    public void PartialMerge_IsCalledDuringCompaction()
    {
        var mergeOp = new PartialMergeOperator();

        using var opts = new DbOptions { CreateIfMissing = true };
        opts.MergeOperator = mergeOp;

        using var db = TestDb.OpenInMemory(opts);

        db.Merge("list", "a");
        db.Merge("list", "b");
        db.Merge("list", "c");

        // Force compaction to trigger partial merge
        db.Flush();
        db.CompactRange();

        // The name of this test. It used to assert only that the merged value
        // came back correct, which the full merge alone produces, so it passed
        // whether or not PartialMerge was ever reached.
        Assert.True(
            mergeOp.PartialMergeCalls > 0,
            "PartialMerge was never called, so only the full merge was exercised");

        Assert.Equal("a,b,c", db.GetString("list"));
    }

    [Fact]
    public void MergeOperator_Ctor_ThrowsOnNullOrEmptyName()
    {
        Assert.Throws<ArgumentException>(() => new NameValidatingMergeOperator(""));
        Assert.Throws<ArgumentNullException>(() => new NameValidatingMergeOperator(null!));
    }

    /// <summary>
    /// The base implementation declines, and gives no value when it does.
    /// </summary>
    /// <remarks>
    /// It used to hand back an empty array, which existed only to satisfy a
    /// non-nullable out parameter and was indistinguishable from a merge that
    /// genuinely produced no bytes. The parameter is nullable now, matching the
    /// compaction filter callback beside it, so declining can say so.
    /// </remarks>
    [Fact]
    public void MergeOperator_DefaultPartialMerge_ReturnsFalseAndNoValue()
    {
        using var mergeOp = new NoPartialOverrideMergeOperator();

        bool ok = mergeOp.PartialMerge(
            key: "k"u8,
            operands: [Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("b")],
            out byte[]? value);

        Assert.False(ok);
        Assert.Null(value);
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
