using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Preparing a transaction, and recovering the prepared ones after the database
/// is reopened. See issue #162.
/// </summary>
/// <remarks>
/// The recovery cases all reopen a real on-disk database, because that is the
/// whole claim: a prepared transaction is durable, and one that was never
/// prepared is not. An in-memory environment would prove nothing here.
/// </remarks>
public class TwoPhaseCommitTests
{
    private static DbOptions NewDbOptions() => new() { CreateIfMissing = true };

    /// <summary>
    /// The one that says why any of this exists: a prepared transaction is
    /// still there after the database is closed and reopened, and the write it
    /// was holding can still be applied.
    /// </summary>
    [Fact]
    public void APreparedTransaction_SurvivesReopeningAndCanBeCommitted()
    {
        using var dir = new TempDir();

        using (var dbOpts = NewDbOptions())
        using (var txnOpts = new TransactionDbOptions())
        using (TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path))
        {
            using Transaction txn = db.BeginTransaction();
            txn.Put("k", "prepared");
            txn.Name = "order-4711";
            txn.Prepare();

            // Deliberately neither committed nor rolled back.
        }

        using (var dbOpts = NewDbOptions())
        using (var txnOpts = new TransactionDbOptions())
        using (TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path))
        {
            // Not visible yet: prepared is durable, not committed.
            Assert.Null(db.GetString("k"));

            IReadOnlyList<Transaction> recovered = db.GetPreparedTransactions();
            Transaction txn = Assert.Single(recovered);

            try
            {
                Assert.Equal("order-4711", txn.Name);
                txn.Commit();
            }
            finally
            {
                txn.Dispose();
            }

            Assert.Equal("prepared", db.GetString("k"));
        }
    }

    /// <summary>
    /// The same shape, resolved the other way. A recovered transaction that is
    /// rolled back leaves nothing behind.
    /// </summary>
    [Fact]
    public void ARecoveredTransaction_CanBeRolledBackInstead()
    {
        using var dir = new TempDir();

        using (var dbOpts = NewDbOptions())
        using (var txnOpts = new TransactionDbOptions())
        using (TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path))
        {
            using Transaction txn = db.BeginTransaction();
            txn.Put("k", "abandoned");
            txn.Name = "order-4712";
            txn.Prepare();
        }

        using (var dbOpts = NewDbOptions())
        using (var txnOpts = new TransactionDbOptions())
        using (TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path))
        {
            using Transaction txn = Assert.Single(db.GetPreparedTransactions());
            txn.Rollback();

            Assert.Null(db.GetString("k"));

            // And it is no longer outstanding.
            Assert.Empty(db.GetPreparedTransactions());
        }
    }

    /// <summary>
    /// The contrast that makes the test above mean something: without
    /// preparing, an interrupted transaction leaves nothing to recover.
    /// </summary>
    [Fact]
    public void AnUnpreparedTransaction_LeavesNothingToRecover()
    {
        using var dir = new TempDir();

        using (var dbOpts = NewDbOptions())
        using (var txnOpts = new TransactionDbOptions())
        using (TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path))
        {
            using Transaction txn = db.BeginTransaction();
            txn.Put("k", "never-prepared");
            txn.Name = "order-4713";

            // No Prepare.
        }

        using (var dbOpts = NewDbOptions())
        using (var txnOpts = new TransactionDbOptions())
        using (TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path))
        {
            Assert.Empty(db.GetPreparedTransactions());
            Assert.Null(db.GetString("k"));
        }
    }

    [Fact]
    public void SeveralPreparedTransactions_AreAllRecovered()
    {
        using var dir = new TempDir();

        using (var dbOpts = NewDbOptions())
        using (var txnOpts = new TransactionDbOptions())
        using (TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path))
        {
            for (int i = 0; i < 3; i++)
            {
                using Transaction txn = db.BeginTransaction();
                txn.Put($"k{i}", $"v{i}");
                txn.Name = $"order-{i}";
                txn.Prepare();
            }
        }

        using (var dbOpts = NewDbOptions())
        using (var txnOpts = new TransactionDbOptions())
        using (TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path))
        {
            IReadOnlyList<Transaction> recovered = db.GetPreparedTransactions();

            try
            {
                Assert.Equal(3, recovered.Count);
                Assert.Equal(
                    ["order-0", "order-1", "order-2"],
                    recovered.Select(t => t.Name).Order());

                foreach (Transaction txn in recovered)
                {
                    txn.Commit();
                }
            }
            finally
            {
                foreach (Transaction txn in recovered)
                {
                    txn.Dispose();
                }
            }

            Assert.Equal("v0", db.GetString("k0"));
            Assert.Equal("v1", db.GetString("k1"));
            Assert.Equal("v2", db.GetString("k2"));
        }
    }

    /// <summary>A clean database has nothing outstanding.</summary>
    [Fact]
    public void GetPreparedTransactions_OnACleanDatabase_IsEmpty()
    {
        using var dir = new TempDir();
        using var dbOpts = NewDbOptions();
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        Assert.Empty(db.GetPreparedTransactions());

        // Also empty while a transaction is merely open, and after it commits.
        using (Transaction txn = db.BeginTransaction())
        {
            txn.Put("a", "1");
            Assert.Empty(db.GetPreparedTransactions());
            txn.Commit();
        }

        Assert.Empty(db.GetPreparedTransactions());
    }

    // ── Name ────────────────────────────────────────────────────────────────

    [Fact]
    public void Name_IsEmptyUntilSet_AndRoundTrips()
    {
        using var dir = new TempDir();
        using var dbOpts = NewDbOptions();
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        using Transaction txn = db.BeginTransaction();

        Assert.Equal(string.Empty, txn.Name);

        txn.Name = "settlement-99";
        Assert.Equal("settlement-99", txn.Name);
    }

    /// <summary>
    /// A non-ASCII name survives, which is the part a byte-length mistake would
    /// break: the marshaller writes UTF-8, so a length counted in characters
    /// would truncate the name and register it under something else.
    /// </summary>
    [Fact]
    public void Name_WithMultiByteCharacters_RoundTripsWhole()
    {
        using var dir = new TempDir();
        using var dbOpts = NewDbOptions();
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        using Transaction txn = db.BeginTransaction();

        const string name = "átutalás-üñí-✓";
        txn.Name = name;

        Assert.Equal(name, txn.Name);
        Assert.True(Encoding.UTF8.GetByteCount(name) > name.Length, "the name must actually be multi-byte");
    }

    [Fact]
    public void Name_RejectsNullAndEmpty()
    {
        using var dir = new TempDir();
        using var dbOpts = NewDbOptions();
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        using Transaction txn = db.BeginTransaction();

        Assert.Throws<ArgumentNullException>(() => txn.Name = null!);
        Assert.Throws<ArgumentException>(() => txn.Name = string.Empty);
    }

    /// <summary>Preparing without a name is an error rather than a silent no-op.</summary>
    [Fact]
    public void Prepare_WithoutAName_Throws()
    {
        using var dir = new TempDir();
        using var dbOpts = NewDbOptions();
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        using Transaction txn = db.BeginTransaction();
        txn.Put("k", "v");

        Assert.Throws<RocksDbException>(txn.Prepare);
    }

    /// <summary>
    /// A prepared transaction still holds its locks, so a second writer cannot
    /// take the same key. This is what recovery must resolve, and the reason a
    /// recovered transaction has to be committed or rolled back rather than
    /// merely disposed.
    /// </summary>
    [Fact]
    public void APreparedTransaction_StillHoldsItsLocks()
    {
        using var dir = new TempDir();
        using var dbOpts = NewDbOptions();
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOpts, txnOpts, dir.Path);

        using Transaction held = db.BeginTransaction();
        held.Put("contended", "first");
        held.Name = "holder";
        held.Prepare();

        using var writeOpts = new WriteOptions();
        using var shortWait = new TransactionOptions { LockTimeout = 100 };
        using Transaction other = db.BeginTransaction(writeOpts, shortWait);

        Assert.Throws<RocksDbException>(() => other.Put("contended", "second"));

        held.Rollback();
    }
}
