namespace RocksDbNet.Tests;

/// <summary>
/// Claims in the API documentation that were wrong, pinned so they cannot drift
/// back. See issue #124.
/// </summary>
/// <remarks>
/// The documentation pass that produced these was checked by measurement rather
/// than by reading, because the previous pass got two of its own corrections
/// wrong. What is asserted here is what was measured.
/// </remarks>
public class DocumentedClaimTests
{
    private sealed record Written(string PolicyName, ulong FileSize, int ReadableCount, int UserCount);

    private static Written WriteWith(FilterPolicy policy)
    {
        using var dir = new TempDir();

        var listener = new RecordingListener();

        using var tableOptions = new BlockBasedTableOptions();
        tableOptions.SetFilterPolicy(policy);

        var opts = new DbOptions { CreateIfMissing = true };
        opts.BlockBasedTableFactory = tableOptions;
        opts.AddEventListener(listener);

        using var db = RocksDb.Open(opts, dir.Path);

        for (int i = 0; i < 500; i++)
        {
            db.Put($"key{i:D5}", $"value{i}");
        }

        db.Flush();

        Assert.True(Wait.Until(() => listener.FlushCompleted.Count > 0), "no flush completed");

        LiveFileMetadata file = Assert.Single(db.GetLiveFiles());
        TableProperties props = Assert.IsType<TableProperties>(listener.FlushCompleted[0].TableProperties);

        return new Written(
            props.FilterPolicyName ?? string.Empty,
            file.Size,
            props.ReadableProperties.Count,
            props.UserCollectedProperties.Count);
    }

    /// <summary>
    /// The one Bloom factory writes a Bloom filter, and a different policy
    /// writes a different file.
    /// </summary>
    /// <remarks>
    /// There were two Bloom factories, documented as differing in on-disk record
    /// format, one legacy and one current. They did not differ at all: RocksDb
    /// stopped honouring the parameter that chose between them in version 7.0,
    /// and this test used to assert the two produced byte-identical files. That
    /// is why only one of them is left. Ribbon stays as the control, since
    /// asserting a policy name proves little unless a different policy comes out
    /// different.
    /// </remarks>
    [Fact]
    public void BloomAndRibbonPoliciesAreDistinguishable()
    {
        Written bloom = WriteWith(FilterPolicy.CreateBloomFull(10));
        Written ribbon = WriteWith(FilterPolicy.CreateRibbon(10));

        Assert.Equal("bloomfilter", bloom.PolicyName);
        Assert.Equal("ribbonfilter", ribbon.PolicyName);
        Assert.NotEqual(bloom.FileSize, ribbon.FileSize);
    }

    /// <summary>
    /// <see cref="TableProperties.ReadableProperties"/> is always empty.
    /// </summary>
    /// <remarks>
    /// It was documented as the human-readable rendering of the user properties.
    /// RocksDb fills it from collectors registered by the application, and the C
    /// API cannot create a collector factory, so nothing ever registers one.
    /// </remarks>
    [Fact]
    public void ReadableProperties_IsEmptyWhileUserPropertiesIsNot()
    {
        Written written = WriteWith(FilterPolicy.CreateBloomFull(10));

        Assert.Equal(0, written.ReadableCount);

        // Not vacuous: RocksDb does contribute entries of its own, and they
        // arrive on the other dictionary.
        Assert.True(written.UserCount > 0, "no user-collected properties either, so this proves nothing");
    }

    /// <summary>
    /// A per-transaction lock timeout replaces the database-wide one rather than
    /// being clamped by it.
    /// </summary>
    /// <remarks>
    /// Both <see cref="TransactionDbOptions.TransactionLockTimeout"/> and
    /// <see cref="TransactionOptions.LockTimeout"/> described the database value
    /// as a ceiling a transaction could shorten but not exceed. This asks for
    /// three seconds against a database that fails immediately: a ceiling would
    /// return at once, and it waits the full three.
    /// </remarks>
    [Fact]
    public void LockTimeout_ReplacesTheDatabaseValueRatherThanBeingCappedByIt()
    {
        using var dir = new TempDir();

        var dbOptions = new DbOptions { CreateIfMissing = true };

        // Fail immediately, database-wide.
        using var txnDbOptions = new TransactionDbOptions { TransactionLockTimeout = 0 };
        using var db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);

        using Transaction holder = db.BeginTransaction();
        holder.Put("key", "held");

        using var patient = new TransactionOptions { LockTimeout = 3_000 };
        using Transaction waiter = db.BeginTransaction(transactionOptions: patient);

        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<RocksDbException>(() => waiter.Put("key", "blocked"));

        elapsed.Stop();

        Assert.True(
            elapsed.Elapsed > TimeSpan.FromSeconds(2),
            $"gave up after {elapsed.Elapsed.TotalSeconds:F1}s, so the database value capped the transaction's");
    }

    // ── Which thread a callback runs on ─────────────────────────────────────

    private sealed class ThreadRecordingMerge() : MergeOperator("thread-merge")
    {
        public int FullMergeThread = -1;

        public override bool FullMerge(
            ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue,
            IReadOnlyList<byte[]> operands, out byte[] newValue)
        {
            FullMergeThread = Environment.CurrentManagedThreadId;
            newValue = operands[^1];
            return true;
        }
    }

    private sealed class ThreadRecordingListener : EventListener
    {
        public int MemTableSealedThread = -1;
        public int FlushCompletedThread = -1;

        public override void OnMemTableSealed(MemTableInfo info)
            => MemTableSealedThread = Environment.CurrentManagedThreadId;

        public override void OnFlushCompleted(FlushJobInfo info)
            => FlushCompletedThread = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// Not every callback arrives on a background thread, whatever the guides
    /// used to say.
    /// </summary>
    /// <remarks>
    /// <c>callbacks.md</c> said listener, compaction filter and merge operator
    /// callbacks all run on RocksDb background threads. Measured, two of them do
    /// not: a full merge runs on the thread doing the <c>Get</c> that needed it,
    /// and a sealed memtable is reported on the thread whose write sealed it.
    /// The distinction matters to anyone deciding whether their callback may
    /// touch thread-local or request-scoped state.
    /// </remarks>
    [Fact]
    public void SomeCallbacksRunOnTheCallersThread()
    {
        var merge = new ThreadRecordingMerge();
        var listener = new ThreadRecordingListener();

        var opts = new DbOptions { CreateIfMissing = true };
        opts.MergeOperator = merge;
        opts.AddEventListener(listener);

        string path = TestDb.InMemory(opts);
        using var db = RocksDb.Open(opts, path);

        int caller = Environment.CurrentManagedThreadId;

        db.Merge("k", "a");
        db.Merge("k", "b");

        // The read is what asks for the full merge.
        Assert.Equal("b", db.GetString("k"));
        Assert.Equal(caller, merge.FullMergeThread);

        db.Flush();

        // Sealing happens on the writing thread, which here is this one.
        Assert.Equal(caller, listener.MemTableSealedThread);

        // And the flush itself is the background half, which is what the guides
        // described for everything.
        Assert.True(Wait.Until(() => listener.FlushCompletedThread != -1), "no flush completed");
        Assert.NotEqual(caller, listener.FlushCompletedThread);
    }

    // ── Where a transaction conflict is actually detected ───────────────────

    private static TransactionDb OpenTransactional(TempDir dir)
    {
        var dbOptions = new DbOptions { CreateIfMissing = true };

        // Fail immediately rather than wait, so a conflict is a fast exception.
        using var txnDbOptions = new TransactionDbOptions { TransactionLockTimeout = 0 };

        return TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);
    }

    /// <summary>
    /// A plain <c>Get</c> is not tracked, so reading a key and then committing
    /// succeeds even after someone else changed that key.
    /// </summary>
    /// <remarks>
    /// <c>Commit</c> was documented as throwing when "a key it read has changed
    /// since", for a transaction created with a snapshot. It does not. This is
    /// the difference between what the documentation promised and what the
    /// wrapper delivers, and it is the kind of thing a caller builds a
    /// read-modify-write on top of.
    /// </remarks>
    [Fact]
    public void PlainGet_DoesNotConflictAtCommit()
    {
        using var dir = new TempDir();
        using TransactionDb db = OpenTransactional(dir);

        db.Put("k", "original");

        using var snapshotted = new TransactionOptions { SetSnapshot = true };
        using Transaction reader = db.BeginTransaction(transactionOptions: snapshotted);

        Assert.Equal("original", reader.GetString("k"));

        using (Transaction writer = db.BeginTransaction())
        {
            writer.Put("k", "changed");
            writer.Commit();
        }

        reader.Put("other", "1");

        // No conflict, no exception. The value it read is stale and it does not
        // find out.
        reader.Commit();

        Assert.Equal("changed", db.GetString("k"));
        Assert.Equal("1", db.GetString("other"));
    }

    /// <summary>
    /// Conflicts surface when the key is locked, which is at the write or the
    /// read-for-update, not at the commit.
    /// </summary>
    [Fact]
    public void ConflictsSurfaceAtLockTimeRatherThanAtCommit()
    {
        using var dir = new TempDir();
        using TransactionDb db = OpenTransactional(dir);

        db.Put("k", "original");

        using var snapshotted = new TransactionOptions { SetSnapshot = true };

        // Writing the changed key fails at the Put.
        using (Transaction writerSide = db.BeginTransaction(transactionOptions: snapshotted))
        {
            using (Transaction other = db.BeginTransaction())
            {
                other.Put("k", "changed");
                other.Commit();
            }

            Assert.Throws<RocksDbException>(() => writerSide.Put("k", "mine"));
        }

        db.Put("k2", "original");

        // And reading it for update fails at the read.
        using Transaction readerSide = db.BeginTransaction(transactionOptions: snapshotted);

        using (Transaction other = db.BeginTransaction())
        {
            other.Put("k2", "changed");
            other.Commit();
        }

        Assert.Throws<RocksDbException>(() => readerSide.GetStringForUpdate("k2"));
    }
}
