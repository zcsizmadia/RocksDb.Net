using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;

namespace RocksDbNet.Benchmarks;

/// <summary>
/// What a merge costs on the managed side, with the operator written against
/// the original array-based API.
/// </summary>
/// <remarks>
/// Every key carries several operands, and each read resolves them through
/// FullMerge, so the managed callback runs once per key read. What changes
/// between versions is the wrapper around the callback: how the operands are
/// handed over and how the result is handed back.
/// </remarks>
[MemoryDiagnoser]
public partial class MergeBenchmarks
{
    private const int OperandsPerKey = 10;

    private BenchmarkDb _arrayDb = null!;
    private byte[][] _keys = null!;

    [Params(1_000)]
    public int Keys { get; set; }

    private static byte[] Int64(long value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return bytes;
    }

    private BenchmarkDb CreateMergeDb(MergeOperator op)
    {
        BenchmarkDb db = BenchmarkDb.Create(count: 0, valueSize: 0, o => o.MergeOperator = op);

        foreach (byte[] key in _keys)
        {
            for (int i = 0; i < OperandsPerKey; i++)
            {
                db.Db.Merge(key, Int64(i));
            }
        }

        return db;
    }

    [GlobalSetup]
    public void Setup()
    {
        _keys = [.. Enumerable.Range(0, Keys).Select(i => BenchmarkDb.KeyFor(i))];
        _arrayDb = CreateMergeDb(new ArrayCounter());
        SetupSpan();
    }

    partial void SetupSpan();

    partial void CleanupSpan();

    [GlobalCleanup]
    public void Cleanup()
    {
        _arrayDb.Dispose();
        CleanupSpan();
    }

    [Benchmark(Baseline = true, Description = "Reads resolving merges, array-form operator")]
    public long ArrayForm()
    {
        long sum = 0;

        foreach (byte[] key in _keys)
        {
            sum += BinaryPrimitives.ReadInt64LittleEndian(_arrayDb.Db.Get(key));
        }

        return sum;
    }

    private sealed class ArrayCounter() : MergeOperator("bench-array-counter")
    {
        public override bool FullMerge(
            ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue,
            IReadOnlyList<byte[]> operands, out byte[]? newValue)
        {
            long sum = hasExistingValue ? BinaryPrimitives.ReadInt64LittleEndian(existingValue) : 0;
            foreach (byte[] operand in operands)
            {
                sum += BinaryPrimitives.ReadInt64LittleEndian(operand);
            }

            newValue = Int64(sum);
            return true;
        }
    }
}

/// <summary>
/// What a compaction filter costs per key on the managed side, for a filter
/// that keeps everything.
/// </summary>
/// <remarks>
/// Keeping is the common answer, so this is the overhead every key pays. It
/// used to include two ConcurrentDictionary operations on each key whatever
/// the filter decided.
/// </remarks>
[MemoryDiagnoser]
public class CompactionFilterBenchmarks
{
    [Params(100_000)]
    public int Keys { get; set; }

    private KeepEverything _filter = null!;
    private BenchmarkDb _db = null!;

    // A fresh database each iteration, so every one compacts the same data
    // through the filter, and only the compaction is timed.
    [IterationSetup]
    public void Setup()
    {
        _filter = new KeepEverything();
        _db = BenchmarkDb.Create(Keys, valueSize: 32, o => o.CompactionFilter = _filter);
    }

    [IterationCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        _filter.Dispose();
    }

    [Benchmark(Description = "Full compaction through a keep-everything filter")]
    public void CompactThroughFilter() => _db.Db.CompactRange();

    private sealed class KeepEverything() : CompactionFilter("bench-keep-everything")
    {
        protected override FilterDecision Filter(
            int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            newValue = null;
            return FilterDecision.Keep;
        }
    }
}
