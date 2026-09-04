using BenchmarkDotNet.Attributes;
using RocksDbNet;

namespace RocksDbNet.Benchmarks;

/// <summary>
/// The three ways to read a value, against each other.
/// </summary>
/// <remarks>
/// The README calls this library "zero-copy" and lists zero-copy reads among
/// its features, and nothing measured it. The API offers three tiers on the
/// assumption that they differ: if they do not, the extra surface is not paying
/// for itself and the claim is overstated. Either answer is worth having.
///
/// Value size is a parameter because the copy is what varies with it: the
/// per-call overhead is fixed, so any advantage to not copying should widen as
/// values grow, and that shape is more informative than any single number.
/// </remarks>
[MemoryDiagnoser]
public class ReadBenchmarks
{
    private BenchmarkDb _db = null!;
    private byte[] _buffer = null!;

    [Params(64, 1024, 16 * 1024)]
    public int ValueSize { get; set; }

    [Params(1_000)]
    public int Reads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _db = BenchmarkDb.Create(count: 10_000, valueSize: ValueSize);
        _buffer = new byte[ValueSize];
    }

    [GlobalCleanup]
    public void Cleanup() => _db.Dispose();

    /// <summary>Two copies: one native into a buffer RocksDb allocates, one into the array.</summary>
    [Benchmark(Baseline = true, Description = "Get returning a new byte[]")]
    public long GetArray()
    {
        long bytes = 0;

        for (int i = 0; i < Reads; i++)
        {
            byte[]? value = _db.Db.Get(_db.Keys[i]);
            bytes += value?.Length ?? 0;
        }

        return bytes;
    }

    /// <summary>One copy, into a buffer the caller already owns.</summary>
    [Benchmark(Description = "TryGetInto a caller-owned buffer")]
    public long TryGetInto()
    {
        long bytes = 0;

        for (int i = 0; i < Reads; i++)
        {
            if (_db.Db.TryGetInto(_db.Keys[i], _buffer, out int length))
            {
                bytes += length;
            }
        }

        return bytes;
    }

    /// <summary>
    /// No copy at all when the value is served from the block cache: the slice
    /// is a view over memory RocksDb already holds.
    /// </summary>
    [Benchmark(Description = "GetPinned, no copy")]
    public long GetPinned()
    {
        long bytes = 0;

        for (int i = 0; i < Reads; i++)
        {
            using PinnableSlice? slice = _db.Db.GetPinned(_db.Keys[i]);
            bytes += slice?.Value.Length ?? 0;
        }

        return bytes;
    }
}

/// <summary>
/// Batched reads against each other, and against not batching.
/// </summary>
/// <remarks>
/// The comparison that tells someone whether batching is worth restructuring
/// their code for. A loop of single gets is the honest baseline, because it is
/// what the code being replaced looks like.
/// </remarks>
[MemoryDiagnoser]
public class MultiGetBenchmarks
{
    private BenchmarkDb _db = null!;
    private byte[][] _batch = null!;
    private ColumnFamilyHandle _defaultCf = null!;

    [Params(128)]
    public int BatchSize { get; set; }

    [Params(256)]
    public int ValueSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _db = BenchmarkDb.Create(count: 10_000, valueSize: ValueSize);
        _batch = _db.Keys.Take(BatchSize).ToArray();
        _defaultCf = _db.Db.GetDefaultColumnFamily();
    }

    [GlobalCleanup]
    public void Cleanup() => _db.Dispose();

    [Benchmark(Baseline = true, Description = "A loop of single gets")]
    public long Loop()
    {
        long bytes = 0;

        foreach (byte[] key in _batch)
        {
            byte[]? value = _db.Db.Get(key);
            bytes += value?.Length ?? 0;
        }

        return bytes;
    }

    [Benchmark(Description = "MultiGet")]
    public long MultiGet()
    {
        long bytes = 0;

        foreach (byte[]? value in _db.Db.MultiGet(_batch))
        {
            bytes += value?.Length ?? 0;
        }

        return bytes;
    }

    [Benchmark(Description = "MultiGetPinned")]
    public long MultiGetPinned()
    {
        long bytes = 0;
        PinnableSlice?[] slices = _db.Db.MultiGetPinned(_batch, _defaultCf);

        foreach (PinnableSlice? slice in slices)
        {
            bytes += slice?.Value.Length ?? 0;
            slice?.Dispose();
        }

        return bytes;
    }
}

/// <summary>
/// Iterating a whole database, with allocations counted.
/// </summary>
/// <remarks>
/// The README advertises <c>ref struct</c> iterators, and that claim lives or
/// dies on the allocation column rather than the time column — an enumerator
/// that allocates per entry is not what the phrase promises.
/// </remarks>
[MemoryDiagnoser]
public class IteratorBenchmarks
{
    private BenchmarkDb _db = null!;

    [Params(10_000)]
    public int Keys { get; set; }

    [GlobalSetup]
    public void Setup() => _db = BenchmarkDb.Create(Keys, valueSize: 128);

    [GlobalCleanup]
    public void Cleanup() => _db.Dispose();

    /// <summary>Spans over native memory, nothing copied.</summary>
    [Benchmark(Baseline = true, Description = "Full scan reading spans")]
    public long ScanSpans()
    {
        long bytes = 0;

        using Iterator it = _db.Db.NewIterator();

        for (it.SeekToFirst(); it.IsValid(); it.Next())
        {
            bytes += it.Key().Length + it.Value().Length;
        }

        return bytes;
    }

    /// <summary>Two arrays per entry, which is what the span version avoids.</summary>
    [Benchmark(Description = "Full scan copying to arrays")]
    public long ScanArrays()
    {
        long bytes = 0;

        using Iterator it = _db.Db.NewIterator();

        for (it.SeekToFirst(); it.IsValid(); it.Next())
        {
            bytes += it.KeyToArray().Length + it.ValueToArray().Length;
        }

        return bytes;
    }
}
