using System.Runtime.CompilerServices;
using System.Text;

namespace RocksDbNet.Tests;

public class ReadOptionsTests
{
    [Fact]
    public void VerifyChecksums_GetSet()
    {
        using var opts = new ReadOptions();

        opts.VerifyChecksums = true;
        Assert.True(opts.VerifyChecksums);

        opts.VerifyChecksums = false;
        Assert.False(opts.VerifyChecksums);
    }

    [Fact]
    public void FillCache_GetSet()
    {
        using var opts = new ReadOptions();

        opts.FillCache = false;
        Assert.False(opts.FillCache);
    }

    [Fact]
    public void ReadTier_GetSet()
    {
        using var opts = new ReadOptions();

        opts.ReadTier = ReadTier.BlockCache;
        Assert.Equal(ReadTier.BlockCache, opts.ReadTier);
    }

    [Fact]
    public void Tailing_GetSet()
    {
        using var opts = new ReadOptions();

        opts.Tailing = true;
        Assert.True(opts.Tailing);
    }

    [Fact]
    public void ReadaheadSize_GetSet()
    {
        using var opts = new ReadOptions();

        opts.ReadaheadSize = 2 * 1024 * 1024;
        Assert.Equal(2UL * 1024 * 1024, opts.ReadaheadSize);
    }

    [Fact]
    public void PrefixSameAsStart_GetSet()
    {
        using var opts = new ReadOptions();

        opts.PrefixSameAsStart = true;
        Assert.True(opts.PrefixSameAsStart);
    }

    [Fact]
    public void PinData_GetSet()
    {
        using var opts = new ReadOptions();

        opts.PinData = true;
        Assert.True(opts.PinData);
    }

    [Fact]
    public void TotalOrderSeek_GetSet()
    {
        using var opts = new ReadOptions();

        opts.TotalOrderSeek = true;
        Assert.True(opts.TotalOrderSeek);
    }

    [Fact]
    public void AsyncIo_GetSet()
    {
        using var opts = new ReadOptions();

        opts.AsyncIo = true;
        Assert.True(opts.AsyncIo);
    }

    [Fact]
    public void IgnoreRangeDeletions_GetSet()
    {
        using var opts = new ReadOptions();

        opts.IgnoreRangeDeletions = true;
        Assert.True(opts.IgnoreRangeDeletions);
    }

    [Fact]
    public void SetSnapshot_DoesNotThrow()
    {
        using var opts = new ReadOptions();

        opts.SetSnapshot(null);
    }

    [Fact]
    public void SetIterateUpperBound_DoesNotThrow()
    {
        using var opts = new ReadOptions();

        opts.SetIterateUpperBound("z"u8);
    }

    [Fact]
    public void SetIterateLowerBound_DoesNotThrow()
    {
        using var opts = new ReadOptions();

        opts.SetIterateLowerBound("a"u8);
    }

    [Fact]
    public void SetIterateBounds_WithIterator()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");
        db.Db.Put("d", "4");

        byte[] lowerBound = [.. "b"u8];
        byte[] upperBound = [.. "d"u8];

        using var readOpts = new ReadOptions();
        readOpts.SetIterateLowerBound(lowerBound);
        readOpts.SetIterateUpperBound(upperBound);

        using var iter = db.Db.NewIterator(readOpts);
        iter.SeekToFirst();

        var keys = new List<string>();
        while (iter.IsValid())
        {
            keys.Add(iter.KeyAsString());
            iter.Next();
        }

        Assert.Equal(["b", "c"], keys);
    }

    /// <summary>
    /// RocksDb keeps a Slice pointing at the bound buffer and dereferences it on
    /// every Seek/Next. The bound must therefore survive the caller's buffer going
    /// out of scope and a compacting collection. See issue #28.
    /// </summary>
    [Fact]
    public void SetIterateBounds_SurviveGarbageCollection()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");
        db.Db.Put("d", "4");

        using var readOpts = new ReadOptions();
        SetBoundsFromTemporaryBuffers(readOpts);

        // Drop the buffers and provoke a compacting collection so that anything
        // still pointing into managed memory reads freed or relocated bytes.
        for (int i = 0; i < 4; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            _ = new byte[2 * 1024 * 1024];
        }

        using var iter = db.Db.NewIterator(readOpts);
        iter.SeekToFirst();

        var keys = new List<string>();
        while (iter.IsValid())
        {
            keys.Add(iter.KeyAsString());
            iter.Next();
        }

        Assert.Equal(["b", "c"], keys);
    }

    /// <summary>
    /// Sets both bounds from buffers that are unreachable once this returns, so the
    /// options are the only thing that could still be keeping them alive.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetBoundsFromTemporaryBuffers(ReadOptions readOpts)
    {
        // Freshly allocated arrays, not UTF-8 literals: literals live in
        // non-moving static data and would mask the bug.
        readOpts.SetIterateLowerBound(Encoding.UTF8.GetBytes("b"));
        readOpts.SetIterateUpperBound(Encoding.UTF8.GetBytes("d"));
    }

    [Fact]
    public void SetIterateBounds_Replaced_UsesLatestBound()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");

        using var readOpts = new ReadOptions();
        readOpts.SetIterateUpperBound(Encoding.UTF8.GetBytes("b"));
        readOpts.SetIterateUpperBound(Encoding.UTF8.GetBytes("c"));

        using var iter = db.Db.NewIterator(readOpts);
        iter.SeekToFirst();

        var keys = new List<string>();
        while (iter.IsValid())
        {
            keys.Add(iter.KeyAsString());
            iter.Next();
        }

        Assert.Equal(["a", "b"], keys);
    }

    [Fact]
    public void SetIterateBounds_EmptySpan_ClearsBound()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");

        using var readOpts = new ReadOptions();
        readOpts.SetIterateUpperBound(Encoding.UTF8.GetBytes("b"));
        readOpts.SetIterateUpperBound([]);

        using var iter = db.Db.NewIterator(readOpts);
        iter.SeekToFirst();

        var keys = new List<string>();
        while (iter.IsValid())
        {
            keys.Add(iter.KeyAsString());
            iter.Next();
        }

        Assert.Equal(["a", "b", "c"], keys);
    }

    /// <summary>
    /// Each set copies the bound into unmanaged memory and must free the
    /// previous copy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to assert nothing at all: it set ten thousand bounds and ended,
    /// so leaking every one of them would have passed. The bounds are now large
    /// enough that a leak is a gigabyte rather than the hundred kilobytes ten
    /// thousand short keys came to, and it measures the process rather than the
    /// managed heap, since these copies are unmanaged.
    /// </para>
    /// <para>
    /// The budget is wide, and deliberately so. Every test class runs in this
    /// same process, several at once, and their native allocations move the
    /// number this reads: a run on a Linux agent measured 179 MB of growth with
    /// nothing leaking at all. The signal has to stay well clear of that, which
    /// is why the leak is sized in gigabytes.
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

    [Fact]
    public void SetIterateBounds_AfterDispose_Throws()
    {
        var readOpts = new ReadOptions();
        readOpts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => readOpts.SetIterateUpperBound("z"u8));
        Assert.Throws<ObjectDisposedException>(() => readOpts.SetIterateLowerBound("a"u8));
    }
}
