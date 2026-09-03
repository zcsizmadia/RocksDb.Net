using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// How much detail RocksDb collects into the per-thread
/// <see cref="PerfContext"/>, mapped from <c>rocksdb::PerfLevel</c> in
/// <c>perf_level.h</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each level includes the ones before it, and costs more. Counting is on by
/// default; the timing levels add real overhead and should be enabled around
/// the operation being investigated rather than left on.
/// </para>
/// <para>
/// These values come from RocksDb's C++ header, not from the constants in its C
/// header. Those are stale: the C header's value for "time except for mutex" is
/// 3, which the C++ enum now uses for <see cref="EnableWait"/>, and the setter
/// casts the value without checking it. Following the C header would silently
/// select the wrong level.
/// </para>
/// </remarks>
public enum PerfLevel
{
    /// <summary>Collect nothing.</summary>
    Disable = 1,

    /// <summary>
    /// Collect counters only, such as key comparisons, blocks read and bytes
    /// read. This is RocksDb's default.
    /// </summary>
    EnableCount = 2,

    /// <summary>
    /// Also measure time spent waiting for RocksDb itself, as opposed to
    /// waiting on mutexes or I/O.
    /// </summary>
    EnableWait = 3,

    /// <summary>
    /// Also measure end-to-end time of operations, excluding mutex waits.
    /// </summary>
    EnableTimeExceptForMutex = 4,

    /// <summary>
    /// Also measure CPU time of operations, excluding mutex waits.
    /// </summary>
    EnableTimeAndCpuTimeExceptForMutex = 5,

    /// <summary>Measure everything, including time spent on mutexes.</summary>
    EnableTime = 6,
}

/// <summary>
/// Per-operation profiling counters for the current thread. Maps to
/// <c>rocksdb_perfcontext_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Answers a question the database-wide statistics cannot: where the time and
/// I/O went inside <em>one</em> operation. <see cref="DbOptions.EnableStatistics"/>
/// aggregates across every thread for the life of the database; this measures a
/// single thread's work since the last <see cref="Reset"/>.
/// </para>
/// <para>
/// The usual shape is to set a level, reset, perform one operation, then read
/// the counters:
/// </para>
/// <code>
/// PerfContext.SetLevel(PerfLevel.EnableCount);
/// using var perf = PerfContext.CreateForCurrentThread();
/// perf.Reset();
/// _ = db.GetString("key");
/// long comparisons = (long)perf.GetMetric(PerfMetric.UserKeyComparisonCount);
/// </code>
/// <para>
/// <b>This is bound to the thread that created it.</b> RocksDb keeps the
/// counters in thread-local storage, so an instance created on one thread
/// reports nothing useful about another. Every member therefore throws if used
/// from a different thread, which turns undefined behaviour into a diagnosable
/// exception. In practice that means it cannot survive an <c>await</c>, because
/// the continuation may resume on another thread. <see cref="SetLevel"/> is
/// thread-local for the same reason.
/// </para>
/// </remarks>
public sealed class PerfContext : RocksDbHandle
{
    private readonly int _threadId;

    private PerfContext(nint handle)
        : base(handle)
    {
        _threadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// Returns the calling thread's perf context.
    /// </summary>
    /// <remarks>
    /// Dispose it on the same thread. Disposal only releases a small wrapper;
    /// the counters themselves belong to the thread, not to this object, so
    /// disposing does not reset them.
    /// </remarks>
    public static PerfContext CreateForCurrentThread()
        => new(NativeMethods.rocksdb_perfcontext_create());

    /// <summary>
    /// Sets how much detail RocksDb collects, for the calling thread only.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not a defined <see cref="PerfLevel"/>. RocksDb casts it
    /// without checking, so an out-of-range value would reach native code as a
    /// garbage enum.
    /// </exception>
    public static void SetLevel(PerfLevel level)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(
                nameof(level), level, "Not a defined RocksDb performance level.");
        }

        NativeMethods.rocksdb_set_perf_level((int)level);
    }

    /// <summary>Zeroes every counter for this thread.</summary>
    public void Reset()
    {
        ThrowIfWrongThread();
        NativeMethods.rocksdb_perfcontext_reset(Handle);
    }

    /// <summary>Reads one counter.</summary>
    /// <param name="metric">The counter to read.</param>
    /// <returns>
    /// The counter's value, or zero if the current <see cref="PerfLevel"/> does
    /// not populate it.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not a defined <see cref="PerfMetric"/>.
    /// </exception>
    public ulong GetMetric(PerfMetric metric)
    {
        ThrowIfWrongThread();

        if (!Enum.IsDefined(metric))
        {
            throw new ArgumentOutOfRangeException(
                nameof(metric), metric, "Not a defined RocksDb performance metric.");
        }

        return NativeMethods.rocksdb_perfcontext_metric(Handle, (int)metric);
    }

    /// <summary>Renders every counter as text, for logging or a dump.</summary>
    /// <param name="excludeZeroCounters">
    /// When true, the default, counters still at zero are left out.
    /// </param>
    public string Report(bool excludeZeroCounters = true)
    {
        ThrowIfWrongThread();

        nint ptr = NativeMethods.rocksdb_perfcontext_report(Handle, excludeZeroCounters ? (byte)1 : (byte)0);
        if (ptr == nint.Zero)
        {
            return string.Empty;
        }

        string report = Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        NativeMethods.rocksdb_free(ptr);
        return report;
    }

    private void ThrowIfWrongThread()
    {
        ThrowIfDisposed();

        if (Environment.CurrentManagedThreadId != _threadId)
        {
            throw new InvalidOperationException(
                $"This {nameof(PerfContext)} belongs to thread {_threadId} and was used from thread " +
                $"{Environment.CurrentManagedThreadId}. RocksDb keeps these counters in thread-local " +
                $"storage, so it would report the wrong thread's work. Call " +
                $"{nameof(CreateForCurrentThread)} on the thread doing the work.");
        }
    }

    protected override void DisposeHandle()
    {
        // Frees only the wrapper. The counters live in thread-local storage and
        // outlive this object.
        NativeMethods.rocksdb_perfcontext_destroy(Handle);
    }
}
