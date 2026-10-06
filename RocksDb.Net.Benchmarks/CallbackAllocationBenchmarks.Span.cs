using System.Buffers;
using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;

namespace RocksDbNet.Benchmarks;

/// <summary>The same merges, with the operator written against the span API.</summary>
public partial class MergeBenchmarks
{
    private BenchmarkDb _spanDb = null!;

    partial void SetupSpan() => _spanDb = CreateMergeDb(new SpanCounter());

    partial void CleanupSpan() => _spanDb.Dispose();

    [Benchmark(Description = "Reads resolving merges, span-form operator")]
    public long SpanForm()
    {
        long sum = 0;

        foreach (byte[] key in _keys)
        {
            sum += BinaryPrimitives.ReadInt64LittleEndian(_spanDb.Db.Get(key));
        }

        return sum;
    }

    private sealed class SpanCounter() : MergeOperator("bench-span-counter")
    {
        public override bool FullMerge(
            ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue,
            MergeOperands operands, IBufferWriter<byte> newValue)
        {
            long sum = hasExistingValue ? BinaryPrimitives.ReadInt64LittleEndian(existingValue) : 0;
            for (int i = 0; i < operands.Count; i++)
            {
                sum += BinaryPrimitives.ReadInt64LittleEndian(operands[i]);
            }

            BinaryPrimitives.WriteInt64LittleEndian(newValue.GetSpan(8), sum);
            newValue.Advance(8);
            return true;
        }
    }
}
