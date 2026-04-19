using RocksDbNet.Native;

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

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_ratelimiter_destroy(Handle);
    }
}