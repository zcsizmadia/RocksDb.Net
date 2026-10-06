namespace RocksDbNet.Tests;

/// <summary>
/// Runs its tests with nothing else running, after the parallel collections
/// have finished.
/// </summary>
/// <remarks>
/// For tests that measure the whole process. Every other test class shares the
/// process and runs several at once, and their native allocations move the
/// number a process-wide measurement reads. With a budget wide enough to absorb
/// that, a leak has to be sized in gigabytes to stand out; and even then a
/// neighbour heavy enough could cross the budget on its own. That happened: a
/// test creating two hundred column families, each reserving memtable arena
/// space, pushed a bound-leak test that leaked nothing to 754 MB of growth on a
/// Linux agent.
/// </remarks>
[CollectionDefinition(nameof(ProcessMemoryCollection), DisableParallelization = true)]
public sealed class ProcessMemoryCollection;

/// <summary>Leak tests that measure the process rather than the managed heap.</summary>
[Collection(nameof(ProcessMemoryCollection))]
public class ProcessMemoryTests
{
    /// <summary>
    /// Each set copies the bound into unmanaged memory and must free the
    /// previous copy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to assert nothing at all: it set ten thousand bounds and ended,
    /// so leaking every one of them would have passed. The bounds are large
    /// enough that a leak is a gigabyte rather than the hundred kilobytes ten
    /// thousand short keys came to, and it measures the process rather than the
    /// managed heap, since these copies are unmanaged.
    /// </para>
    /// <para>
    /// The budget is still wide. Running alone removes the other tests'
    /// allocations from the measurement, but not the runtime's or the
    /// allocator's own, and a leak this size stays far clear of either.
    /// </para>
    /// </remarks>
    [Fact]
    public void SetIterateBounds_RepeatedSets_DoNotLeak()
    {
        const int Sets = 4_000;
        const int BoundBytes = 128 * 1024;

        using var readOpts = new ReadOptions();

        byte[] bound = new byte[BoundBytes];

        // One of each first, so the initial allocation is not counted as growth.
        readOpts.SetIterateUpperBound(bound);
        readOpts.SetIterateLowerBound(bound);

        long before = CurrentProcessBytes();

        for (int i = 0; i < Sets; i++)
        {
            readOpts.SetIterateUpperBound(bound);
            readOpts.SetIterateLowerBound(bound);
        }

        long grew = CurrentProcessBytes() - before;

        // A gigabyte held if nothing was freed, against 256 KB if everything
        // was. See the remarks for why the budget sits where it does.
        const long Budget = 512L * 1024 * 1024;

        Assert.True(
            grew < Budget,
            $"the process grew by {grew / (1024 * 1024)} MB over {Sets * 2} bound copies, so the old ones were not freed");
    }

    private static long CurrentProcessBytes()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();

        using var self = System.Diagnostics.Process.GetCurrentProcess();
        self.Refresh();

        return self.PrivateMemorySize64;
    }
}
