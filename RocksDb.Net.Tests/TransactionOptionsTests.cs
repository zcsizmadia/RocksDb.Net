using System.Diagnostics;

namespace RocksDbNet.Tests;

/// <summary>
/// Round trips for every transaction option, and the members of
/// <see cref="TransactionDb"/> and <see cref="Transaction"/> not exercised by
/// the behavioural tests. See issue #69.
/// </summary>
public class TransactionOptionsTests
{
    [Fact]
    public void TransactionDbOptions_WritePolicy_RoundTripsEveryValue()
    {
        using var opts = new TransactionDbOptions();

        // The default RocksDb documents, and the only value it calls mature.
        Assert.Equal(TransactionDbWritePolicy.WriteCommitted, opts.WritePolicy);

        foreach (TransactionDbWritePolicy policy in Enum.GetValues<TransactionDbWritePolicy>())
        {
            opts.WritePolicy = policy;
            Assert.Equal(policy, opts.WritePolicy);
        }
    }

    [Fact]
    public void TransactionDbOptions_ScalarsRoundTrip()
    {
        using var opts = new TransactionDbOptions
        {
            TransactionLockTimeout = 1234,
            DefaultLockTimeout = 5678,
            MaxNumLocks = 999,
            NumStripes = 32,
            MaxNumDeadlocks = 7,
            DefaultWriteBatchFlushThreshold = 4096,
            CommitBypassMemtableThreshold = 8192,
        };

        Assert.Equal(1234, opts.TransactionLockTimeout);
        Assert.Equal(5678, opts.DefaultLockTimeout);
        Assert.Equal(999, opts.MaxNumLocks);
        Assert.Equal((nuint)32, opts.NumStripes);
        Assert.Equal(7u, opts.MaxNumDeadlocks);
        Assert.Equal(4096, opts.DefaultWriteBatchFlushThreshold);
        Assert.Equal(8192u, opts.CommitBypassMemtableThreshold);
    }

    [Fact]
    public void TransactionDbOptions_FlagsRoundTrip()
    {
        using var opts = new TransactionDbOptions();

        foreach (bool value in new[] { true, false })
        {
            opts.RollbackMergeOperands = value;
            opts.SkipConcurrencyControl = value;
            opts.EnableUdtValidation = value;
            opts.UsePerKeyPointLockManager = value;

            Assert.Equal(value, opts.RollbackMergeOperands);
            Assert.Equal(value, opts.SkipConcurrencyControl);
            Assert.Equal(value, opts.EnableUdtValidation);
            Assert.Equal(value, opts.UsePerKeyPointLockManager);
        }
    }

    [Fact]
    public void TransactionOptions_ScalarsRoundTrip()
    {
        using var opts = new TransactionOptions
        {
            DeadlockDetectDepth = 64,
            LockTimeout = 250,
            Expiration = 30_000,
            MaxWriteBatchSize = 1 << 20,
            DeadlockTimeoutMicros = 1500,
            WriteBatchFlushThreshold = 2048,
            LargeTransactionCommitOptimizeThreshold = 100,
            LargeTransactionCommitOptimizeByteThreshold = 1 << 24,
        };

        Assert.Equal(64, opts.DeadlockDetectDepth);
        Assert.Equal(250, opts.LockTimeout);
        Assert.Equal(30_000, opts.Expiration);
        Assert.Equal((nuint)(1 << 20), opts.MaxWriteBatchSize);
        Assert.Equal(1500, opts.DeadlockTimeoutMicros);
        Assert.Equal(2048, opts.WriteBatchFlushThreshold);
        Assert.Equal(100u, opts.LargeTransactionCommitOptimizeThreshold);
        Assert.Equal(1UL << 24, opts.LargeTransactionCommitOptimizeByteThreshold);
    }

    [Fact]
    public void TransactionOptions_FlagsRoundTrip()
    {
        using var opts = new TransactionOptions();

        foreach (bool value in new[] { true, false })
        {
            opts.SetSnapshot = value;
            opts.DeadlockDetect = value;
            opts.SkipConcurrencyControl = value;
            opts.SkipPrepare = value;
            opts.UseOnlyTheLastCommitTimeBatchForRecovery = value;
            opts.WriteBatchTrackTimestampSize = value;
            opts.CommitBypassMemtable = value;

            Assert.Equal(value, opts.SetSnapshot);
            Assert.Equal(value, opts.DeadlockDetect);
            Assert.Equal(value, opts.SkipConcurrencyControl);
            Assert.Equal(value, opts.SkipPrepare);
            Assert.Equal(value, opts.UseOnlyTheLastCommitTimeBatchForRecovery);
            Assert.Equal(value, opts.WriteBatchTrackTimestampSize);
            Assert.Equal(value, opts.CommitBypassMemtable);
        }
    }

    /// <summary>
    /// A per-transaction lock timeout must actually take effect, not merely
    /// round trip.
    /// </summary>
    /// <remarks>
    /// The timing is the whole test. Both settings end in the same exception,
    /// so asserting only that it throws passes just as well when the
    /// per-transaction value is ignored and the ten-second database-wide
    /// ceiling is what fires. The difference between the two is how long the
    /// caller waited.
    /// </remarks>
    [Fact]
    public void TransactionOptions_LockTimeout_IsHonoured()
    {
        using var dir = new TempDir();
        var dbOptions = new DbOptions { CreateIfMissing = true };
        using var txnDbOptions = new TransactionDbOptions { TransactionLockTimeout = 10_000 };
        using var db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);

        using Transaction holder = db.BeginTransaction();
        holder.Put("key", "held");

        // Zero means fail at once rather than wait out the database-wide ceiling.
        using var impatient = new TransactionOptions { LockTimeout = 0 };
        using Transaction waiter = db.BeginTransaction(transactionOptions: impatient);

        var elapsed = Stopwatch.StartNew();

        Assert.Throws<RocksDbException>(() => waiter.Put("key", "blocked"));

        elapsed.Stop();

        // Generous against a loaded build agent, and still nowhere near the ten
        // seconds the database-wide ceiling would have cost.
        Assert.True(
            elapsed.Elapsed < TimeSpan.FromSeconds(2),
            $"waited {elapsed.Elapsed.TotalSeconds:F1}s, so the per-transaction timeout was ignored");
    }

    /// <summary>
    /// Skipping concurrency control removes the conflict detection, which is
    /// worth pinning because it is the one setting that silently discards the
    /// guarantee the type exists to provide.
    /// </summary>
    [Fact]
    public void TransactionOptions_SkipConcurrencyControl_AllowsAConflictingWrite()
    {
        using var dir = new TempDir();
        var dbOptions = new DbOptions { CreateIfMissing = true };
        using var txnDbOptions = new TransactionDbOptions { TransactionLockTimeout = 0 };
        using var db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);

        using Transaction holder = db.BeginTransaction();
        holder.Put("key", "held");

        using var unchecked_ = new TransactionOptions { SkipConcurrencyControl = true };
        using Transaction bypass = db.BeginTransaction(transactionOptions: unchecked_);

        // No lock is taken, so the write that would normally fail goes through.
        bypass.Put("key", "unchecked");
        bypass.Commit();

        Assert.Equal("unchecked", db.GetString("key"));
    }

    [Fact]
    public void TransactionOptions_DeadlockDetect_StillCommitsNormally()
    {
        using var dir = new TempDir();
        var dbOptions = new DbOptions { CreateIfMissing = true };
        using var txnDbOptions = new TransactionDbOptions();
        using var db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);

        using var opts = new TransactionOptions { DeadlockDetect = true, DeadlockDetectDepth = 10 };
        using Transaction txn = db.BeginTransaction(transactionOptions: opts);

        txn.Put("key", "value");
        txn.Commit();

        Assert.Equal("value", db.GetString("key"));
    }

    // ── Members not covered by the behavioural tests ─────────────────────────

    [Fact]
    public void Transaction_MergeAndSpanOverloads_Work()
    {
        using var dir = new TempDir();
        var dbOptions = new DbOptions { CreateIfMissing = true };
        dbOptions.SetUInt64AddMergeOperator();

        using var txnDbOptions = new TransactionDbOptions();
        using var db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);

        db.Put("counter"u8, BitConverter.GetBytes(5UL));

        using (Transaction txn = db.BeginTransaction())
        {
            txn.Merge("counter"u8, BitConverter.GetBytes(3UL));
            txn.Commit();
        }

        byte[]? total = db.Get("counter"u8);
        Assert.NotNull(total);
        Assert.Equal(8UL, BitConverter.ToUInt64(total));
    }

    [Fact]
    public void TransactionDb_MergeAndDeleteSpanOverloads_Work()
    {
        using var dir = new TempDir();
        var dbOptions = new DbOptions { CreateIfMissing = true };
        dbOptions.SetUInt64AddMergeOperator();

        using var txnDbOptions = new TransactionDbOptions();
        using var db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);

        db.Put("counter"u8, BitConverter.GetBytes(10UL));
        db.Merge("counter"u8, BitConverter.GetBytes(5UL));

        byte[]? total = db.Get("counter"u8);
        Assert.NotNull(total);
        Assert.Equal(15UL, BitConverter.ToUInt64(total));

        db.Delete("counter"u8);
        Assert.Null(db.Get("counter"u8));
    }

    [Fact]
    public void TransactionDb_ColumnFamilyOperations_Work()
    {
        using var dir = new TempDir();
        var dbOptions = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        dbOptions.SetUInt64AddMergeOperator();

        // The merge operator has to be on the column family's own options, not
        // just the database's. RocksDb rejects a merge into a family whose
        // options carry no operator, whatever the database options say.
        var cf1Descriptor = new ColumnFamilyDescriptor("cf1");
        cf1Descriptor.Options.SetUInt64AddMergeOperator();

        using var txnDbOptions = new TransactionDbOptions();
        using var db = TransactionDb.Open(
            dbOptions, txnDbOptions, dir.Path, [new("default"), cf1Descriptor]);
        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");

        db.Put("a", "1", cf1);
        db.Merge("counter"u8, BitConverter.GetBytes(4UL), cf1);
        db.Delete("a"u8, cf1);
        db.Flush(cf1);

        Assert.Null(db.GetString("a", cf1));
        Assert.Equal(4UL, BitConverter.ToUInt64(db.Get("counter"u8, cf1)!));

        using (PinnableSlice? pinned = db.GetPinned("counter"u8, cf1))
        {
            Assert.NotNull(pinned);
        }

        var keys = new List<string>();
        using (Iterator iter = db.NewIterator(cf1))
        {
            for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
            {
                keys.Add(iter.KeyAsString());
            }
        }

        Assert.Equal(["counter"], keys);
    }

    [Fact]
    public void Transaction_ColumnFamilyOverloads_Work()
    {
        using var dir = new TempDir();
        var dbOptions = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using var txnDbOptions = new TransactionDbOptions();
        using var db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path, [new("default"), new("cf1")]);
        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");

        using Transaction txn = db.BeginTransaction();

        txn.Put("a"u8, "1"u8, cf1);
        Assert.Equal("1", txn.GetString("a", cf1));
        Assert.Equal("1"u8.ToArray(), txn.GetForUpdate("a"u8, cf1));

        txn.Delete("a"u8, cf1);
        Assert.Null(txn.Get("a"u8, cf1));

        var keys = new List<string>();
        using (Iterator iter = txn.NewIterator(cf1))
        {
            for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
            {
                keys.Add(iter.KeyAsString());
            }
        }

        Assert.Empty(keys);

        txn.Commit();
    }

    [Fact]
    public void Transaction_GetForUpdate_SharedLock_AllowsAnotherReader()
    {
        using var dir = new TempDir();
        var dbOptions = new DbOptions { CreateIfMissing = true };
        using var txnDbOptions = new TransactionDbOptions { TransactionLockTimeout = 0 };
        using var db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);

        db.Put("key", "value");

        using Transaction first = db.BeginTransaction();
        using Transaction second = db.BeginTransaction();

        // Shared locks coexist; exclusive ones would not.
        Assert.Equal("value"u8.ToArray(), first.GetForUpdate("key"u8, exclusive: false));
        Assert.Equal("value"u8.ToArray(), second.GetForUpdate("key"u8, exclusive: false));
    }

    [Fact]
    public void TransactionDb_FlushWal_Works()
    {
        using var dir = new TempDir();
        var dbOptions = new DbOptions { CreateIfMissing = true };
        using var txnDbOptions = new TransactionDbOptions();
        using var db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);

        db.Put("key", "value");
        db.FlushWal();
        db.FlushWal(sync: false);

        Assert.Equal("value", db.GetString("key"));
    }

    [Fact]
    public void TransactionDb_TryGetColumnFamily_RejectsEmptyNames()
    {
        using var dir = new TempDir();
        var dbOptions = new DbOptions { CreateIfMissing = true };
        using var txnDbOptions = new TransactionDbOptions();
        using var db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);

        Assert.Throws<ArgumentException>(() => db.TryGetColumnFamily(string.Empty, out _));
        Assert.Throws<ArgumentNullException>(() => db.GetColumnFamily(null!));
        Assert.Throws<ArgumentNullException>(() => db.CreateColumnFamily(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => db.GetProperty(null!));
        Assert.Throws<ArgumentNullException>(() => db.GetPropertyInt(null!));

        // No named families were opened, so the default is reported.
        Assert.Equal(["default"], db.ColumnFamilyNames);
    }
}
