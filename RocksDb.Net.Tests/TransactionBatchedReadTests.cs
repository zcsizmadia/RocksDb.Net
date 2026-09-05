using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Batched and copy-free reads on <see cref="Transaction"/>. See issue #163.
/// </summary>
/// <remarks>
/// These mirror the members of the same name on <see cref="RocksDb"/>, so the
/// cases that matter are the ones where a transaction differs from a plain
/// database: seeing its own pending writes, and taking locks.
/// </remarks>
public class TransactionBatchedReadTests
{
    private static DbOptions NewDbOptions() => new() { CreateIfMissing = true };

    private sealed class Db : IDisposable
    {
        private readonly TempDir _dir = new();
        private readonly DbOptions _dbOptions = NewDbOptions();
        private readonly TransactionDbOptions _txnOptions = new();

        public Db() => Value = TransactionDb.Open(_dbOptions, _txnOptions, _dir.Path);

        public TransactionDb Value { get; }

        public void Dispose()
        {
            Value.Dispose();
            _txnOptions.Dispose();
            _dbOptions.Dispose();
            _dir.Dispose();
        }
    }

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private static string? S(byte[]? value) => value is null ? null : Encoding.UTF8.GetString(value);

    // ── MultiGet ────────────────────────────────────────────────────────────

    [Fact]
    public void MultiGet_ReadsCommittedValuesAndReportsMissingKeysAsNull()
    {
        using var db = new Db();
        db.Value.Put("a", "1");
        db.Value.Put("c", "3");

        using Transaction txn = db.Value.BeginTransaction();

        byte[]?[] values = txn.MultiGet([B("a"), B("b"), B("c")]);

        Assert.Equal(3, values.Length);
        Assert.Equal("1", S(values[0]));
        Assert.Null(values[1]);
        Assert.Equal("3", S(values[2]));
    }

    /// <summary>
    /// The case a plain database cannot cover: a batched read inside a
    /// transaction sees that transaction's own uncommitted writes.
    /// </summary>
    [Fact]
    public void MultiGet_SeesTheTransactionsOwnPendingWrites()
    {
        using var db = new Db();
        db.Value.Put("committed", "old");

        using Transaction txn = db.Value.BeginTransaction();
        txn.Put("committed", "new");
        txn.Put("pending", "only-here");

        byte[]?[] values = txn.MultiGet([B("committed"), B("pending")]);

        Assert.Equal("new", S(values[0]));
        Assert.Equal("only-here", S(values[1]));

        // And none of it is visible outside until commit.
        Assert.Equal("old", db.Value.GetString("committed"));
        Assert.Null(db.Value.GetString("pending"));
    }

    /// <summary>A delete queued in the transaction reads back as absent.</summary>
    [Fact]
    public void MultiGet_SeesAPendingDelete()
    {
        using var db = new Db();
        db.Value.Put("gone", "value");

        using Transaction txn = db.Value.BeginTransaction();
        txn.Delete("gone");

        Assert.Null(Assert.Single(txn.MultiGet([B("gone")])));
    }

    [Fact]
    public void MultiGet_WithNoKeys_ReturnsEmpty()
    {
        using var db = new Db();
        using Transaction txn = db.Value.BeginTransaction();

        Assert.Empty(txn.MultiGet([]));
    }

    [Fact]
    public void MultiGet_AgreesWithReadingEachKeySeparately()
    {
        using var db = new Db();
        for (int i = 0; i < 25; i++)
        {
            db.Value.Put($"key{i:D2}", $"value{i}");
        }

        using Transaction txn = db.Value.BeginTransaction();

        // A mix of present and absent keys, out of order.
        byte[][] keys = [B("key07"), B("nope"), B("key00"), B("key24"), B("also-nope")];

        byte[]?[] batched = txn.MultiGet(keys);
        byte[]?[] individually = [.. keys.Select(k => txn.Get(k))];

        Assert.Equal(individually.Select(S), batched.Select(S));
    }

    // ── Column families ─────────────────────────────────────────────────────

    [Fact]
    public void MultiGet_WithOneColumnFamily_ReadsFromIt()
    {
        using var db = new Db();
        using var cfOpts = new DbOptions();
        using ColumnFamilyHandle cf = db.Value.CreateColumnFamily(cfOpts, "other");

        db.Value.Put("k", "default-value");
        db.Value.Put("k", "cf-value", cf);

        using Transaction txn = db.Value.BeginTransaction();

        Assert.Equal("cf-value", S(Assert.Single(txn.MultiGet([B("k")], cf))));
        Assert.Equal("default-value", S(Assert.Single(txn.MultiGet([B("k")]))));
    }

    /// <summary>
    /// One column family per key, which is the reason the overload exists: two
    /// families are read in a single call.
    /// </summary>
    /// <remarks>
    /// Opened with an explicit family list rather than creating one on a
    /// default-only database, because <c>TransactionDb.GetColumnFamily</c>
    /// cannot resolve the default family unless it was named at open — see
    /// issue #165. Naming both here sidesteps that and is a fair test of the
    /// overload either way.
    /// </remarks>
    [Fact]
    public void MultiGet_WithAColumnFamilyPerKey_ReadsAcrossFamilies()
    {
        using var dir = new TempDir();
        using var dbOpts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using var txnOpts = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(
            dbOpts, txnOpts, dir.Path, [new("default"), new("other")]);

        ColumnFamilyHandle def = db.GetColumnFamily("default");
        ColumnFamilyHandle other = db.GetColumnFamily("other");

        db.Put("k", "from-default", def);
        db.Put("k", "from-other", other);

        using Transaction txn = db.BeginTransaction();

        byte[]?[] values = txn.MultiGet([B("k"), B("k")], [def, other]);

        Assert.Equal("from-default", S(values[0]));
        Assert.Equal("from-other", S(values[1]));
    }

    [Fact]
    public void MultiGet_WithMismatchedListLengths_Throws()
    {
        using var db = new Db();
        using var cfOpts = new DbOptions();
        using ColumnFamilyHandle cf = db.Value.CreateColumnFamily(cfOpts, "other");

        using Transaction txn = db.Value.BeginTransaction();

        Assert.Throws<ArgumentException>(() => txn.MultiGet([B("a"), B("b")], [cf]));
    }

    [Fact]
    public void MultiGet_WithANullKey_Throws()
    {
        using var db = new Db();
        using Transaction txn = db.Value.BeginTransaction();

        Assert.Throws<ArgumentNullException>(() => txn.MultiGet([B("a"), null!]));
    }

    // ── MultiGetForUpdate ───────────────────────────────────────────────────

    [Fact]
    public void MultiGetForUpdate_ReadsTheSameValues()
    {
        using var db = new Db();
        db.Value.Put("a", "1");

        using Transaction txn = db.Value.BeginTransaction();

        byte[]?[] values = txn.MultiGetForUpdate([B("a"), B("missing")]);

        Assert.Equal("1", S(values[0]));
        Assert.Null(values[1]);
    }

    /// <summary>
    /// The point of the call: every key it read is locked afterwards, including
    /// one that was absent. A second transaction cannot write any of them.
    /// </summary>
    [Fact]
    public void MultiGetForUpdate_LocksEveryKeyItRead()
    {
        using var db = new Db();
        db.Value.Put("present", "1");

        using Transaction holder = db.Value.BeginTransaction();
        holder.MultiGetForUpdate([B("present"), B("absent")]);

        using var writeOpts = new WriteOptions();
        using var shortWait = new TransactionOptions { LockTimeout = 100 };
        using Transaction other = db.Value.BeginTransaction(writeOpts, shortWait);

        Assert.Throws<RocksDbException>(() => other.Put("present", "stolen"));
        Assert.Throws<RocksDbException>(() => other.Put("absent", "stolen"));

        // A key it did not read is still free.
        other.Put("untouched", "fine");
    }

    /// <summary>
    /// The plain batched read locks nothing, which is what distinguishes the
    /// two and is the reason both exist.
    /// </summary>
    [Fact]
    public void MultiGet_LocksNothing()
    {
        using var db = new Db();
        db.Value.Put("k", "1");

        using Transaction reader = db.Value.BeginTransaction();
        reader.MultiGet([B("k")]);

        using var writeOpts = new WriteOptions();
        using var shortWait = new TransactionOptions { LockTimeout = 100 };
        using Transaction other = db.Value.BeginTransaction(writeOpts, shortWait);

        other.Put("k", "taken");
        other.Commit();
    }

    // ── Pinned reads ────────────────────────────────────────────────────────

    [Fact]
    public void GetPinned_ReadsTheValueWithoutCopying()
    {
        using var db = new Db();
        db.Value.Put("k", "pinned-value");

        using Transaction txn = db.Value.BeginTransaction();

        using PinnableSlice? slice = txn.GetPinned(B("k"));

        Assert.NotNull(slice);
        Assert.Equal("pinned-value", slice.ToUtf8String());
        Assert.Equal("pinned-value"u8.ToArray(), slice.Value.ToArray());
    }

    [Fact]
    public void GetPinned_SeesPendingWrites()
    {
        using var db = new Db();
        db.Value.Put("k", "old");

        using Transaction txn = db.Value.BeginTransaction();
        txn.Put("k", "new");

        using PinnableSlice? slice = txn.GetPinned(B("k"));

        Assert.NotNull(slice);
        Assert.Equal("new", slice.ToUtf8String());
    }

    [Fact]
    public void GetPinned_OnAMissingKey_ReturnsNull()
    {
        using var db = new Db();
        using Transaction txn = db.Value.BeginTransaction();

        Assert.Null(txn.GetPinned(B("nope")));
    }

    [Fact]
    public void GetPinned_FromAColumnFamily_ReadsFromIt()
    {
        using var db = new Db();
        using var cfOpts = new DbOptions();
        using ColumnFamilyHandle cf = db.Value.CreateColumnFamily(cfOpts, "other");

        db.Value.Put("k", "default-value");
        db.Value.Put("k", "cf-value", cf);

        using Transaction txn = db.Value.BeginTransaction();

        using PinnableSlice? slice = txn.GetPinned(B("k"), cf);

        Assert.NotNull(slice);
        Assert.Equal("cf-value", slice.ToUtf8String());
    }

    [Fact]
    public void GetPinnedForUpdate_LocksTheKey()
    {
        using var db = new Db();
        db.Value.Put("k", "1");

        using Transaction holder = db.Value.BeginTransaction();
        using (PinnableSlice? slice = holder.GetPinnedForUpdate(B("k")))
        {
            Assert.NotNull(slice);
            Assert.Equal("1", slice.ToUtf8String());
        }

        using var writeOpts = new WriteOptions();
        using var shortWait = new TransactionOptions { LockTimeout = 100 };
        using Transaction other = db.Value.BeginTransaction(writeOpts, shortWait);

        Assert.Throws<RocksDbException>(() => other.Put("k", "stolen"));
    }

    /// <summary>
    /// A shared lock lets another reader take the same key for update, which an
    /// exclusive one does not. This is the parameter doing something.
    /// </summary>
    [Fact]
    public void GetPinnedForUpdate_Shared_DoesNotExcludeAnotherReader()
    {
        using var db = new Db();
        db.Value.Put("k", "1");

        using Transaction first = db.Value.BeginTransaction();
        first.GetPinnedForUpdate(B("k"), exclusive: false)?.Dispose();

        using var writeOpts = new WriteOptions();
        using var shortWait = new TransactionOptions { LockTimeout = 100 };
        using Transaction second = db.Value.BeginTransaction(writeOpts, shortWait);

        second.GetPinnedForUpdate(B("k"), exclusive: false)?.Dispose();
    }

    /// <summary>
    /// The slice keeps the transaction alive, so releasing them out of order is
    /// an ObjectDisposedException rather than a read of freed memory.
    /// </summary>
    [Fact]
    public void APinnedSlice_OutlivingItsTransaction_DoesNotReadFreedMemory()
    {
        using var db = new Db();
        db.Value.Put("k", "v");

        Transaction txn = db.Value.BeginTransaction();
        PinnableSlice? slice = txn.GetPinned(B("k"));
        Assert.NotNull(slice);

        txn.Dispose();

        Assert.Throws<ObjectDisposedException>(() => slice.ToUtf8String());
        slice.Dispose();
    }
}
