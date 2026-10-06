using System.Runtime.CompilerServices;

namespace RocksDbNet.Tests;

/// <summary>
/// Settings applied to every database this test suite opens, before any test runs.
/// </summary>
internal static class TestEnvironment
{
    /// <summary>
    /// Turns off RocksDb's periodic statistics dump for every <see cref="DbOptions"/>
    /// the suite creates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A workaround, not a fix.</b> A Linux ARM64 CI run crashed with SIGSEGV
    /// on RocksDb's timer thread, reading address <c>0xad</c> inside
    /// <c>rocksdb::InternalStats::CollectCacheEntryStats</c>, called from
    /// <c>DBImpl::DumpStats</c> through <c>PeriodicTaskScheduler</c>. The
    /// faulting frame was recovered from the signal frame in the crash dump;
    /// none of this library's code was on that thread. The cause is not known:
    /// it may be a race in RocksDb between a periodic dump and a database
    /// closing, or a block cache lifetime problem in this library that only the
    /// dump happened to reach. A mini dump does not keep enough of the heap to
    /// tell which.
    /// </para>
    /// <para>
    /// Why the dump runs at all in a suite that takes about a minute: RocksDb
    /// schedules each database's first dump after
    /// <c>(process-wide counter % stats_dump_period_sec)</c> seconds with
    /// <c>run_immediately</c> set, and the counter advances for every database
    /// opened in the process. Across the thousand-odd databases this suite
    /// opens, some are dumped within seconds of opening, on a background thread,
    /// while other tests open and close databases around it.
    /// </para>
    /// <para>
    /// No test needs those dumps, so turning them off takes the race out of the
    /// suite without losing coverage of anything the suite checks. It does not
    /// make the crash impossible for applications, which keep the default of
    /// 600 seconds. Remove this once the cause is found; a stress test that
    /// opens and closes databases with a short dump period, under a full core
    /// dump, is the way to find it.
    /// </para>
    /// <para>
    /// Applied to options created with the public constructor, which is how
    /// every test builds them, and inherited by their clones. Options loaded
    /// from an existing database keep what its OPTIONS file says. A test that
    /// needs the dump can set <see cref="DbOptions.StatsDumpPeriodSec"/> itself.
    /// </para>
    /// </remarks>
    [ModuleInitializer]
    internal static void DisablePeriodicStatisticsDump()
        => DbOptions.ConfigureForTests = options => options.StatsDumpPeriodSec = 0;
}
