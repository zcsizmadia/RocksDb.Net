using BenchmarkDotNet.Attributes;
using RocksDbNet;

namespace RocksDbNet.Benchmarks;

/// <summary>
/// What a managed callback costs, measured against RocksDb doing the same work
/// natively.
/// </summary>
/// <remarks>
/// The number two open decisions are waiting on. Converting the callbacks to
/// <c>[UnmanagedCallersOnly]</c> function pointers (#148) was argued from first
/// principles rather than measurement; this says what it bought. And #153 asks
/// whether an <see cref="EventListener"/> could simply be notified of
/// everything, dropping the reflection that decides which events a subclass
/// wants — which turns on whether an unwanted notification is cheap.
/// </remarks>
[MemoryDiagnoser]
public class CallbackBenchmarks
{
    /// <summary>Bytewise ordering, in managed code, identical to RocksDb's own.</summary>
    /// <remarks>
    /// Identical on purpose: the difference between this and the baseline is
    /// then the cost of crossing into managed code and back, not the cost of a
    /// different algorithm.
    /// </remarks>
    private sealed class BytewiseComparator : Comparator
    {
        public BytewiseComparator()
            : base("benchmark.bytewise")
        {
        }

        public override int Compare(ReadOnlySpan<byte> keyA, ReadOnlySpan<byte> keyB)
            => keyA.SequenceCompareTo(keyB);
    }

    private BenchmarkDb _builtIn = null!;
    private BenchmarkDb _managed = null!;
    private Comparator _comparator = null!;

    /// <summary>Keys in the database, which sets the depth of each search.</summary>
    [Params(100_000)]
    public int Keys { get; set; }

    /// <summary>Lookups per invocation, so one measurement is not one call.</summary>
    [Params(1_000)]
    public int Lookups { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _builtIn = BenchmarkDb.Create(Keys, valueSize: 128);

        _comparator = new BytewiseComparator();
        _managed = BenchmarkDb.Create(Keys, valueSize: 128, o => o.Comparator = _comparator);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _builtIn.Dispose();
        _managed.Dispose();

        // After the databases, because the options own the comparator and
        // release it when they close.
        _comparator.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Point lookups, RocksDb's own comparator")]
    public long BuiltInComparator() => Lookups100(_builtIn);

    [Benchmark(Description = "Point lookups, managed comparator")]
    public long ManagedComparator() => Lookups100(_managed);

    private long Lookups100(BenchmarkDb db)
    {
        long bytes = 0;
        int stride = Math.Max(1, db.Keys.Length / Lookups);

        for (int i = 0; i < db.Keys.Length; i += stride)
        {
            using PinnableSlice? slice = db.Db.GetPinned(db.Keys[i]);
            bytes += slice?.Value.Length ?? 0;
        }

        return bytes;
    }
}

/// <summary>
/// What an event listener costs when it does not want the event.
/// </summary>
/// <remarks>
/// Directly the question in #153. Today a reflection check at construction
/// decides which of the ten slots a subclass overrode, and the alternative is
/// to notify unconditionally and let an un-overridden virtual return. That is
/// simpler and needs no reflection, and it is only wrong if the notification
/// itself is expensive. Flushes are the event this can produce on demand.
/// </remarks>
[MemoryDiagnoser]
public class EventListenerBenchmarks
{
    /// <summary>Overrides nothing, so every notification is wasted work.</summary>
    private sealed class SilentListener : EventListener
    {
    }

    /// <summary>Overrides everything, so every notification is marshalled in full.</summary>
    private sealed class LoudListener : EventListener
    {
        public long Seen;

        public override void OnFlushBegin(FlushJobInfo info) => Seen++;

        public override void OnFlushCompleted(FlushJobInfo info) => Seen++;

        public override void OnCompactionBegin(CompactionJobInfo info) => Seen++;

        public override void OnCompactionCompleted(CompactionJobInfo info) => Seen++;

        public override void OnMemTableSealed(MemTableInfo info) => Seen++;
    }

    /// <summary>Flushes per invocation, since one flush is one event.</summary>
    [Params(20)]
    public int Flushes { get; set; }

    [Benchmark(Baseline = true, Description = "Flushes with no listener attached")]
    public void NoListener() => Flush(null);

    [Benchmark(Description = "Flushes with a listener that overrides nothing")]
    public void ListenerOverridingNothing() => Flush(new SilentListener());

    [Benchmark(Description = "Flushes with a listener that overrides five events")]
    public void ListenerOverridingEvents() => Flush(new LoudListener());

    private void Flush(EventListener? listener)
    {
        // A database per invocation, because a flush is only a flush the first
        // time: the memtable has to be refilled. That makes the open and the
        // writes part of every measurement, which is why the baseline above
        // matters more than any absolute number here.
        using BenchmarkDb db = BenchmarkDb.Create(
            count: 0,
            valueSize: 0,
            o =>
            {
                if (listener is not null)
                {
                    o.AddEventListener(listener);
                }
            });

        byte[] value = new byte[64];

        for (int f = 0; f < Flushes; f++)
        {
            for (int i = 0; i < 100; i++)
            {
                db.Db.Put(BenchmarkDb.KeyFor((f * 100) + i), value);
            }

            db.Db.Flush();
        }

        listener?.Dispose();
    }
}
