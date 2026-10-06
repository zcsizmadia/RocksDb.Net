using BenchmarkDotNet.Attributes;

namespace RocksDbNet.Benchmarks;

/// <summary>
/// The string overloads, which used to allocate a fresh UTF-8 array for every
/// key and value they encoded.
/// </summary>
/// <remarks>
/// The allocation column is the answer here: the bytes are encoded either way,
/// and what changed is whether they land in a new array per call or in a
/// buffer borrowed from the pool and returned straight after.
/// </remarks>
[MemoryDiagnoser]
public class StringOverloadBenchmarks
{
    private BenchmarkDb _db = null!;
    private string[] _keys = null!;
    private string _value = null!;

    [Params(1_000)]
    public int Operations { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _db = BenchmarkDb.Create(count: 0, valueSize: 0);
        _keys = [.. Enumerable.Range(0, Operations).Select(i => $"user:{i:D8}")];
        _value = new string('v', 100);

        foreach (string key in _keys)
        {
            _db.Db.Put(key, _value);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _db.Dispose();

    [Benchmark(Description = "Put(string, string)")]
    public void Put()
    {
        foreach (string key in _keys)
        {
            _db.Db.Put(key, _value);
        }
    }

    [Benchmark(Description = "GetString(string)")]
    public long GetString()
    {
        long chars = 0;

        foreach (string key in _keys)
        {
            chars += _db.Db.GetString(key)?.Length ?? 0;
        }

        return chars;
    }
}

/// <summary>
/// Many pinned values open against one database at once, which is where the
/// bookkeeping every child handle does on creation and disposal shows.
/// </summary>
/// <remarks>
/// Each pinned value registers with its database so the database can release
/// it on close. That registration was a list, so disposing a large batch in
/// the order it was read scanned and shifted the list once per value.
/// </remarks>
[MemoryDiagnoser]
public class PinnedBatchBenchmarks
{
    private BenchmarkDb _db = null!;
    private byte[][] _batch = null!;
    private ColumnFamilyHandle _defaultCf = null!;

    [Params(2_000)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _db = BenchmarkDb.Create(count: BatchSize, valueSize: 64);
        _batch = _db.Keys.Take(BatchSize).ToArray();
        _defaultCf = _db.Db.GetDefaultColumnFamily();
    }

    [GlobalCleanup]
    public void Cleanup() => _db.Dispose();

    [Benchmark(Description = "MultiGetPinned, disposed in read order")]
    public long DisposeInReadOrder()
    {
        long bytes = 0;
        PinnableSlice?[] slices = _db.Db.MultiGetPinned(_batch, _defaultCf);

        foreach (PinnableSlice? slice in slices)
        {
            bytes += slice?.Length ?? 0;
            slice?.Dispose();
        }

        return bytes;
    }
}
