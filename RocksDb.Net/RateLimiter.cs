namespace RocksDbNet;

/// <summary>
/// Limits the rate of I/O operations (bytes per second).
/// Maps to <c>rocksdb_ratelimiter_t</c>.
/// </summary>
public sealed class RateLimiter : RocksDbHandle
{
    /// <param name="rateBytesPerSec">Target I/O rate in bytes per second.</param>
    /// <param name="refillPeriodMicros">Refill period in microseconds (default: 100 ms).</param>
    /// <param name="fairness">Fairness factor between high-priority and low-priority requests (default: 10).</param>
    public RateLimiter(long rateBytesPerSec, long refillPeriodMicros = 100_000, int fairness = 10)
    {
        Handle = NativeMethods.rocksdb_ratelimiter_create(rateBytesPerSec, refillPeriodMicros, fairness);
    }

    private RateLimiter(nint handle)
        : base(handle)
    {
    }

    /// <summary>
    /// Creates a rate limiter that adjusts its own rate to the load, between
    /// 1/20th of <paramref name="rateBytesPerSec"/> and the full figure.
    /// </summary>
    /// <param name="rateBytesPerSec">The upper bound, in bytes per second.</param>
    /// <param name="refillPeriodMicros">Refill period in microseconds.</param>
    /// <param name="fairness">Fairness factor between high- and low-priority requests.</param>
    /// <remarks>
    /// What RocksDb recommends when the right fixed rate is not known: it
    /// raises the rate while background work is falling behind and lowers it
    /// when there is slack, so compaction does not starve foreground I/O.
    /// </remarks>
    public static RateLimiter CreateAutoTuned(long rateBytesPerSec, long refillPeriodMicros = 100_000, int fairness = 10)
        => new(NativeMethods.rocksdb_ratelimiter_create_auto_tuned(rateBytesPerSec, refillPeriodMicros, fairness));

    /// <summary>Creates a rate limiter that limits only the I/O <paramref name="mode"/> names.</summary>
    /// <param name="rateBytesPerSec">The rate, or the upper bound when <paramref name="autoTuned"/> is set.</param>
    /// <param name="refillPeriodMicros">Refill period in microseconds.</param>
    /// <param name="fairness">Fairness factor between high- and low-priority requests.</param>
    /// <param name="mode">
    /// Which I/O counts against the limit. The other constructors limit writes
    /// only, which is RocksDb's default.
    /// </param>
    /// <param name="autoTuned">Whether to adjust the rate to the load, as <see cref="CreateAutoTuned"/> does.</param>
    public static RateLimiter Create(
        long rateBytesPerSec, RateLimiterMode mode, bool autoTuned = false,
        long refillPeriodMicros = 100_000, int fairness = 10)
        => new(NativeMethods.rocksdb_ratelimiter_create_with_mode(
            rateBytesPerSec, refillPeriodMicros, fairness, (int)mode, autoTuned ? (byte)1 : (byte)0));

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_ratelimiter_destroy(Handle);
    }
}
/// <summary>Which I/O a <see cref="RateLimiter"/> counts against its limit.</summary>
/// <remarks>Mirrors <c>RateLimiter::Mode</c>; the values are what the C API casts to it.</remarks>
public enum RateLimiterMode
{
    /// <summary>Reads only.</summary>
    ReadsOnly = 0,

    /// <summary>Writes only, which is RocksDb's default.</summary>
    WritesOnly = 1,

    /// <summary>Reads and writes.</summary>
    AllIo = 2,
}
