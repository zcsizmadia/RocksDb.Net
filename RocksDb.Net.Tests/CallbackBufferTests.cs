using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// The span forms of the merge operator and compaction filter, and the
/// per-thread buffer both now return their results through. See issue #188.
/// </summary>
/// <remarks>
/// The array forms are covered by the existing merge and filter tests, which
/// now run through the same buffer by way of the default bridge, so these
/// concentrate on what is new: reading operands in place, writing results of
/// every size, an empty result, a merge nested inside another on the same
/// thread, and an operator that overrides neither form.
/// </remarks>
[Collection(nameof(CallbackExceptionTests))]
public class CallbackBufferTests
{
    /// <summary>A little-endian 64-bit counter, merged without allocating.</summary>
    private sealed class SpanCounter() : MergeOperator("span-counter")
    {
        public int FullMerges;
        public int PartialMerges;

        public override bool FullMerge(
            ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue,
            MergeOperands operands, IBufferWriter<byte> newValue)
        {
            Interlocked.Increment(ref FullMerges);

            long sum = hasExistingValue ? BinaryPrimitives.ReadInt64LittleEndian(existingValue) : 0;
            for (int i = 0; i < operands.Count; i++)
            {
                sum += BinaryPrimitives.ReadInt64LittleEndian(operands[i]);
            }

            BinaryPrimitives.WriteInt64LittleEndian(newValue.GetSpan(8), sum);
            newValue.Advance(8);
            return true;
        }

        public override bool PartialMerge(ReadOnlySpan<byte> key, MergeOperands operands, IBufferWriter<byte> newValue)
        {
            Interlocked.Increment(ref PartialMerges);

            long sum = 0;
            for (int i = 0; i < operands.Count; i++)
            {
                sum += BinaryPrimitives.ReadInt64LittleEndian(operands[i]);
            }

            BinaryPrimitives.WriteInt64LittleEndian(newValue.GetSpan(8), sum);
            newValue.Advance(8);
            return true;
        }
    }

    private static byte[] Int64(long value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return bytes;
    }

    [Fact]
    public void SpanMergeOperator_MergesOnReadFlushAndCompaction()
    {
        var counter = new SpanCounter();
        using var db = new TempDb(o => o.MergeOperator = counter);

        db.Db.Put("k"u8, Int64(10));
        for (int i = 1; i <= 100; i++)
        {
            db.Db.Merge("k"u8, Int64(i));

            // Flushing part-way leaves operands in several files, which is
            // what makes RocksDb combine them with a partial merge.
            if (i % 25 == 0)
            {
                db.Db.Flush();
            }
        }

        Assert.Equal(10 + 5050, BinaryPrimitives.ReadInt64LittleEndian(db.Db.Get("k"u8)));

        db.Db.CompactRange();
        Assert.Equal(10 + 5050, BinaryPrimitives.ReadInt64LittleEndian(db.Db.Get("k"u8)));
        Assert.True(counter.FullMerges > 0);
    }

    /// <summary>Concatenates operands, so results grow past the buffer's first size.</summary>
    private sealed class SpanAppend() : MergeOperator("span-append")
    {
        public override bool FullMerge(
            ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue,
            MergeOperands operands, IBufferWriter<byte> newValue)
        {
            if (hasExistingValue)
            {
                newValue.Write(existingValue);
            }

            for (int i = 0; i < operands.Count; i++)
            {
                newValue.Write(operands[i]);
            }

            return true;
        }
    }

    /// <summary>
    /// Results from empty to well past the buffer's initial size, back to back
    /// on the same threads, so a buffer that grew and was then reused for a
    /// shorter result is checked to report the shorter length.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(300)]
    [InlineData(100_000)]
    public void SpanMergeOperator_WritesResultsOfAnySize(int operandSize)
    {
        using var db = new TempDb(o => o.MergeOperator = new SpanAppend());

        byte[] operand = Enumerable.Range(0, operandSize).Select(i => (byte)i).ToArray();

        db.Db.Merge("big"u8, operand);
        db.Db.Merge("big"u8, operand);
        db.Db.Merge("small"u8, "x"u8);

        Assert.Equal(operand.Concat(operand).ToArray(), db.Db.Get("big"u8));
        Assert.Equal("x"u8.ToArray(), db.Db.Get("small"u8));
    }

    /// <summary>
    /// Buffers taken while another is still held are separate, and a
    /// released buffer keeps its contents until it is next taken.
    /// </summary>
    /// <remarks>
    /// Exercised directly rather than through RocksDb. The way to nest two
    /// callbacks on one thread through the database is a merge operator that
    /// reads the database, and RocksDb does not support re-entering itself
    /// from inside a merge: it faults in the nested read, with or without this
    /// buffer.
    /// </remarks>
    [Fact]
    public unsafe void NestedScratchBuffers_AreSeparate_AndKeepTheirContents()
    {
        CallbackScratch outer = CallbackScratch.Acquire();
        try
        {
            outer.Write("outer"u8);

            CallbackScratch inner = CallbackScratch.Acquire();
            try
            {
                Assert.NotSame(outer, inner);
                inner.Write(new byte[10_000]);
            }
            finally
            {
                CallbackScratch.Release();
            }

            Assert.Equal("outer", Encoding.UTF8.GetString(new ReadOnlySpan<byte>(outer.Pointer, outer.WrittenCount)));
        }
        finally
        {
            CallbackScratch.Release();
        }

        // Released, but intact until the next Acquire, which is the window
        // RocksDb copies the result in.
        Assert.Equal("outer", Encoding.UTF8.GetString(new ReadOnlySpan<byte>(outer.Pointer, outer.WrittenCount)));

        CallbackScratch again = CallbackScratch.Acquire();
        try
        {
            Assert.Same(outer, again);
            Assert.Equal(0, again.WrittenCount);
        }
        finally
        {
            CallbackScratch.Release();
        }
    }

    /// <summary>
    /// A buffer that grew for one very large result gives the memory back at
    /// the next callback, while ordinary sizes keep reusing their allocation.
    /// </summary>
    /// <remarks>
    /// The buffers live as long as their thread, and RocksDb's compaction
    /// threads live as long as the process, so without this a single large
    /// merge result stayed allocated on every thread that ever produced one.
    /// </remarks>
    [Fact]
    public void ALargeScratchBuffer_IsGivenBackAtTheNextCallback()
    {
        CallbackScratch scratch = CallbackScratch.Acquire();
        try
        {
            scratch.Write(new byte[4 * CallbackScratch.RetainedCapacityLimit]);
            Assert.True(scratch.Capacity > CallbackScratch.RetainedCapacityLimit);
        }
        finally
        {
            CallbackScratch.Release();
        }

        // Still held after release: RocksDb copies the result after the
        // callback returns, so it cannot go any earlier than this.
        Assert.True(scratch.Capacity > CallbackScratch.RetainedCapacityLimit);

        int small;
        CallbackScratch again = CallbackScratch.Acquire();
        try
        {
            Assert.Same(scratch, again);
            Assert.Equal(0, again.Capacity);

            again.Write(new byte[1000]);
            small = again.Capacity;
        }
        finally
        {
            CallbackScratch.Release();
        }

        CallbackScratch third = CallbackScratch.Acquire();
        try
        {
            Assert.Equal(small, third.Capacity);
        }
        finally
        {
            CallbackScratch.Release();
        }
    }

    private sealed class OverridesNothing() : MergeOperator("overrides-nothing");

    /// <summary>
    /// An operator overriding neither form fails its merges, reported, rather
    /// than recursing between the two defaults or crashing.
    /// </summary>
    [Fact]
    public void AMergeOperatorOverridingNeitherForm_FailsTheMerge()
    {
        var op = new OverridesNothing();
        using var recorder = new CallbackExceptionRecorder(op);
        using var db = new TempDb(o => o.MergeOperator = op);

        db.Db.Merge("k"u8, "v"u8);

        Assert.ThrowsAny<RocksDbException>(() => db.Db.Get("k"u8));
        Assert.Contains(recorder.Reported, r => r.Exception is NotSupportedException);
    }

    [Fact]
    public void MergeOperands_RejectsAnIndexOutOfRange()
    {
        var op = new BoundsCheckingMerge();
        using var db = new TempDb(o => o.MergeOperator = op);

        db.Db.Merge("k"u8, "v"u8);
        _ = db.Db.Get("k"u8);

        Assert.True(op.Checked);
    }

    private sealed class BoundsCheckingMerge() : MergeOperator("bounds-checking")
    {
        public bool Checked;

        public override bool FullMerge(
            ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue,
            MergeOperands operands, IBufferWriter<byte> newValue)
        {
            int count = operands.Count;
            bool threwLow, threwHigh;

            try { _ = operands[-1]; threwLow = false; } catch (ArgumentOutOfRangeException) { threwLow = true; }
            try { _ = operands[count]; threwHigh = false; } catch (ArgumentOutOfRangeException) { threwHigh = true; }

            Checked = threwLow && threwHigh;
            newValue.Write(operands[0]);
            return true;
        }
    }

    // ── Compaction filter ────────────────────────────────────────────────────

    /// <summary>Upper-cases values, blanks "blank", removes "drop", keeps the rest.</summary>
    private sealed class SpanFilter() : CompactionFilter("span-filter")
    {
        protected override FilterDecision Filter(
            int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> existingValue, IBufferWriter<byte> newValue)
        {
            if (key.SequenceEqual("drop"u8))
            {
                return FilterDecision.Remove;
            }

            if (key.SequenceEqual("blank"u8))
            {
                // Nothing written: an empty replacement.
                return FilterDecision.ChangeValue;
            }

            if (key.StartsWith("upper"u8))
            {
                Span<byte> span = newValue.GetSpan(existingValue.Length);
                for (int i = 0; i < existingValue.Length; i++)
                {
                    span[i] = (byte)char.ToUpperInvariant((char)existingValue[i]);
                }

                newValue.Advance(existingValue.Length);
                return FilterDecision.ChangeValue;
            }

            return FilterDecision.Keep;
        }
    }

    [Fact]
    public void SpanCompactionFilter_KeepsRemovesAndReplaces()
    {
        using var filter = new SpanFilter();
        using var db = new TempDb(o => o.CompactionFilter = filter);

        string longValue = new('a', 5_000);

        db.Db.Put("keep", "same");
        db.Db.Put("drop", "gone");
        db.Db.Put("blank", "not empty");
        db.Db.Put("upper-short", "abc");
        db.Db.Put("upper-long", longValue);
        db.Db.Put("upper-short2", "xyz");

        db.Db.Flush();
        db.Db.CompactRange();

        Assert.Equal("same", db.Db.GetString("keep"));
        Assert.Null(db.Db.GetString("drop"));
        Assert.Equal(string.Empty, db.Db.GetString("blank"));
        Assert.Equal("ABC", db.Db.GetString("upper-short"));
        Assert.Equal(longValue.ToUpperInvariant(), db.Db.GetString("upper-long"));

        // After a long result, a short one reports its own length.
        Assert.Equal("XYZ", db.Db.GetString("upper-short2"));
    }

    private sealed class FilterOverridingNothing() : CompactionFilter("filter-overriding-nothing");

    [Fact]
    public void ACompactionFilterOverridingNeitherForm_KeepsEveryEntry()
    {
        using var filter = new FilterOverridingNothing();
        using var recorder = new CallbackExceptionRecorder(filter);
        using var db = new TempDb(o => o.CompactionFilter = filter);

        db.Db.Put("k", "v");
        db.Db.Flush();
        db.Db.CompactRange();

        Assert.Equal("v", db.Db.GetString("k"));
        Assert.Contains(recorder.Reported, r => r.Exception is NotSupportedException);
    }
}
