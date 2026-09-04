namespace RocksDbNet.Tests;

/// <summary>
/// Shared ownership of callback objects, from issues #59 and #64.
/// </summary>
public class SharedOwnershipTests
{
    private sealed class ReverseComparator : Comparator
    {
        public ReverseComparator()
            : base("test.reverse")
        {
        }

        public override int Compare(ReadOnlySpan<byte> keyA, ReadOnlySpan<byte> keyB)
            => keyB.SequenceCompareTo(keyA);
    }

    private sealed class CountingLogger : Logger
    {
        public CountingLogger()
            : base(InfoLogLevel.Info)
        {
        }

        public int Messages;

        public override void Log(InfoLogLevel logLevel, string message)
            => Interlocked.Increment(ref Messages);
    }

    private sealed class NoopMergeOperator : MergeOperator
    {
        public NoopMergeOperator()
            : base("test.noop")
        {
        }

        public override bool FullMerge(
            ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue,
            IReadOnlyList<byte[]> operands, out byte[]? newValue)
        {
            byte[]? last = operands.LastOrDefault();
            newValue = last ?? (hasExistingValue ? existingValue.ToArray() : []);
            return true;
        }
    }

    // ── Exclusive attachments are rejected the second time ──────────────────

    /// <summary>
    /// RocksDb wraps the raw pointer in a fresh <c>shared_ptr</c> for these, so
    /// two options objects given the same instance become two independent
    /// owners and both delete it. That corrupted the heap at teardown, far from
    /// the assignment responsible, so the second assignment now throws.
    /// </summary>
    [Fact]
    public void MergeOperator_CannotBeAttachedTwice()
    {
        var op = new NoopMergeOperator();

        using var first = new DbOptions();
        first.MergeOperator = op;

        using var second = new DbOptions();
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => second.MergeOperator = op);

        Assert.Contains("already attached", ex.Message);
        Assert.Contains(nameof(DbOptions.MergeOperator), ex.Message);
    }

    [Fact]
    public void FilterPolicy_CannotBeAttachedTwice()
    {
        FilterPolicy policy = FilterPolicy.CreateBloomFull(10);

        using var first = new BlockBasedTableOptions();
        first.SetFilterPolicy(policy);

        using var second = new BlockBasedTableOptions();
        Assert.Throws<InvalidOperationException>(() => second.SetFilterPolicy(policy));
    }

    /// <summary>
    /// And a fresh instance each is still fine, which is the supported way to
    /// give two column families the same behaviour.
    /// </summary>
    [Fact]
    public void MergeOperator_OneInstanceEach_Works()
    {
        using var first = new DbOptions();
        using var second = new DbOptions();

        first.MergeOperator = new NoopMergeOperator();
        second.MergeOperator = new NoopMergeOperator();
    }

    // ── Shared attachments survive one holder going away ────────────────────

    /// <summary>
    /// A comparator is a raw pointer as far as RocksDb is concerned, so the
    /// wrapper must release it, and must not do so while another options
    /// object still points at it.
    /// </summary>
    [Fact]
    public void Comparator_AttachedToTwoOptions_SurvivesTheFirstDisposal()
    {
        var cmp = new ReverseComparator();

        var first = new DbOptions { CreateIfMissing = true };
        first.Comparator = cmp;

        var second = new DbOptions { CreateIfMissing = true };
        second.Comparator = cmp;

        // Disposing the first must not destroy the comparator the second still
        // needs. Before this, it did, and the database below compared keys
        // through freed memory.
        first.Dispose();

        using var db = TestDb.OpenInMemory(second);
        db.Put("a", "1");
        db.Put("b", "2");

        // Reverse ordering proves the comparator is still the live one.
        using Iterator iter = db.NewIterator();
        iter.SeekToFirst();
        Assert.Equal("b", iter.KeyAsString());
    }

    /// <summary>
    /// Disposing the object itself while an options object still holds it is
    /// deferred rather than obeyed, since the common shape is a
    /// <c>using</c> block that ends long before the options do.
    /// </summary>
    [Fact]
    public void Comparator_DisposedWhileAttached_IsDeferred()
    {
        var cmp = new ReverseComparator();

        var opts = new DbOptions { CreateIfMissing = true };
        opts.Comparator = cmp;

        cmp.Dispose();
        Assert.False(cmp.IsDisposed);

        using (var db = TestDb.OpenInMemory(opts))
        {
            db.Put("a", "1");
            Assert.Equal("1", db.GetString("a"));
        }

        // The database disposed the options, which released the last hold.
        Assert.True(cmp.IsDisposed);
    }

    // ── Clone shares rather than orphans ───────────────────────────────────

    /// <summary>
    /// The README recommends Clone for exactly this, which made the old
    /// behaviour an active trap: the clone's owned-handle set was empty, so
    /// disposing the original destroyed the comparator the clone's database was
    /// still using.
    /// </summary>
    [Fact]
    public void Clone_SharesAttachedObjects_SoDisposingTheOriginalIsSafe()
    {
        using var firstDir = new TempDir();
        using var secondDir = new TempDir();

        var cmp = new ReverseComparator();
        var original = new DbOptions { CreateIfMissing = true };
        original.Comparator = cmp;

        DbOptions copy = original.Clone();

        using var db1 = RocksDb.Open(original, firstDir.Path);
        using var db2 = RocksDb.Open(copy, secondDir.Path);

        db1.Put("a", "1");
        db1.Put("b", "2");
        db2.Put("a", "1");
        db2.Put("b", "2");

        db1.Dispose();

        // The comparator is still alive for db2.
        Assert.False(cmp.IsDisposed);

        using Iterator iter = db2.NewIterator();
        iter.SeekToFirst();
        Assert.Equal("b", iter.KeyAsString());
    }

    // ── The logger, which is #64 ───────────────────────────────────────────

    /// <summary>
    /// RocksDb copies the logger's <c>shared_ptr</c>, so the native callback
    /// logger outlives the options that were given it. The C API offers no
    /// destructor callback, so the wrapper used to unpin its
    /// <c>GCHandle</c> in Dispose, leaving RocksDb able to log through a freed
    /// handle. The hold count now keeps it pinned until the last holder lets
    /// go.
    /// </summary>
    [Fact]
    public void Logger_StaysUsableWhileTheDatabaseIsOpen()
    {
        var logger = new CountingLogger();

        var opts = new DbOptions { CreateIfMissing = true };
        opts.InfoLog = logger;

        // Disposing here is what used to unpin it.
        logger.Dispose();
        Assert.False(logger.IsDisposed);

        using (var db = TestDb.OpenInMemory(opts))
        {
            for (int i = 0; i < 50; i++)
            {
                db.Put($"key{i}", "value");
            }

            db.Flush();
            db.CompactRange();
        }

        Assert.True(logger.IsDisposed);

        // RocksDb logs plenty during open, flush and close, so this also shows
        // the callback was reaching a live object throughout.
        Assert.True(logger.Messages > 0, "the logger should have received messages");
    }

    [Fact]
    public void Logger_SharedByTwoOptions_SurvivesTheFirstDisposal()
    {
        var logger = new CountingLogger();

        var first = new DbOptions { CreateIfMissing = true };
        first.InfoLog = logger;

        var second = new DbOptions { CreateIfMissing = true };
        second.InfoLog = logger;

        first.Dispose();
        Assert.False(logger.IsDisposed);

        using (var db = TestDb.OpenInMemory(second))
        {
            db.Put("key", "value");
            db.Flush();
        }

        Assert.True(logger.IsDisposed);
        Assert.True(logger.Messages > 0);
    }

    // ── Column family descriptors are not finalized out from under a db ────

    /// <summary>
    /// After Open returned, the descriptor list was unreachable, so its
    /// finalizer disposed the per-column-family options at the next collection
    /// while the database was still using them. The database now holds the
    /// descriptors, so that cannot happen.
    /// </summary>
    [Fact]
    public void ColumnFamilyDescriptors_AreNotFinalizedWhileTheDatabaseIsOpen()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        using var db = OpenWithAbandonedDescriptors(opts, dir.Path);

        // Force the collection that used to destroy the per-family options.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        // The database still works, including the column family whose options
        // would have been destroyed.
        ColumnFamilyHandle cf = db.GetColumnFamily("cf1");
        db.Put("key", "value", cf);
        db.Flush(cf);
        db.CompactRange(cf);

        Assert.Equal("value", db.GetString("key", cf));
    }

    // Separated so the descriptors cannot stay alive in a local of the test.
    private static RocksDb OpenWithAbandonedDescriptors(DbOptions opts, string path)
        => RocksDb.Open(opts, path, [new("default"), new("cf1")]);

    // ── Opening with disposed options fails cleanly ─────────────────────────

    /// <summary>
    /// A disposed <see cref="DbOptions"/> reports a null handle, and RocksDb
    /// requires every pointer argument to be non-null, so passing one through
    /// dereferenced null inside the native open and took the process down with
    /// an access violation that named nothing useful.
    /// </summary>
    [Fact]
    public void Open_WithDisposedOptions_Throws()
    {
        var opts = new DbOptions { CreateIfMissing = true };
        opts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => TestDb.OpenInMemory(opts));
    }

    /// <summary>
    /// And the route that actually bit: a descriptor whose options have been
    /// disposed. This is how a caller reaches the null handle without touching
    /// a <see cref="DbOptions"/> directly.
    /// </summary>
    [Fact]
    public void Open_WithADisposedDescriptorOptions_Throws()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var descriptors = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };
        descriptors[1].Options.Dispose();

        Assert.Throws<ObjectDisposedException>(() => RocksDb.Open(opts, dir.Path, descriptors));
    }

    /// <summary>
    /// Reusing one descriptor list across two databases is supported, and is
    /// exactly why closing a database must not dispose the options its
    /// descriptors own.
    /// </summary>
    [Fact]
    public void ADescriptorList_CanBeReusedForASecondDatabase()
    {
        using var dir = new TempDir();
        var descriptors = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };

        using (var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true })
        using (var db = RocksDb.Open(opts, dir.Path, descriptors))
        {
            db.Put("key", "value", db.GetColumnFamily("cf1"));
        }

        // The same descriptors again. Had closing the first database disposed
        // their options, this would hand RocksDb a null pointer.
        using var roOpts = new DbOptions();
        using var reopened = RocksDb.OpenReadOnly(roOpts, dir.Path, descriptors);

        Assert.Equal("value", reopened.GetString("key", reopened.GetColumnFamily("cf1")));
    }
}
