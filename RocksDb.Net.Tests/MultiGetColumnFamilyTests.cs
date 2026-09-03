using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Batch reads, including across column families and without copying. See
/// issue #76.
/// </summary>
public class MultiGetColumnFamilyTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private static string? S(byte[]? value) => value is null ? null : Encoding.UTF8.GetString(value);

    private sealed class TwoFamilies : IDisposable
    {
        public TwoFamilies()
        {
            Dir = new TempDir();
            Options = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
            Db = RocksDb.Open(Options, Dir.Path, [new("default"), new("cf1"), new("cf2")]);
            Cf1 = Db.GetColumnFamily("cf1");
            Cf2 = Db.GetColumnFamily("cf2");
        }

        public TempDir Dir { get; }

        public DbOptions Options { get; }

        public RocksDb Db { get; }

        public ColumnFamilyHandle Cf1 { get; }

        public ColumnFamilyHandle Cf2 { get; }

        public void Dispose()
        {
            Db.Dispose();
            Options.Dispose();
            Dir.Dispose();
        }
    }

    [Fact]
    public void MultiGet_SingleColumnFamily_ReadsFromThatFamilyOnly()
    {
        using var db = new TwoFamilies();

        db.Db.Put("a", "in-cf1", db.Cf1);
        db.Db.Put("b", "in-cf1", db.Cf1);
        db.Db.Put("a", "in-default");

        byte[]?[] results = db.Db.MultiGet([B("a"), B("b"), B("absent")], db.Cf1);

        Assert.Equal(3, results.Length);
        Assert.Equal("in-cf1", S(results[0]));
        Assert.Equal("in-cf1", S(results[1]));
        Assert.Null(results[2]);
    }

    /// <summary>
    /// The point of the parallel-list overload: one round trip across several
    /// families, which RocksDb supports and a single-family API would waste.
    /// </summary>
    [Fact]
    public void MultiGet_AcrossColumnFamilies_ReadsEachFromItsOwn()
    {
        using var db = new TwoFamilies();

        db.Db.Put("key", "from-default");
        db.Db.Put("key", "from-cf1", db.Cf1);
        db.Db.Put("key", "from-cf2", db.Cf2);

        ColumnFamilyHandle defaultCf = db.Db.GetDefaultColumnFamily();

        byte[]?[] results = db.Db.MultiGet(
            [B("key"), B("key"), B("key"), B("missing")],
            [defaultCf, db.Cf1, db.Cf2, db.Cf1]);

        Assert.Equal(4, results.Length);
        Assert.Equal("from-default", S(results[0]));
        Assert.Equal("from-cf1", S(results[1]));
        Assert.Equal("from-cf2", S(results[2]));
        Assert.Null(results[3]);
    }

    [Fact]
    public void MultiGet_MismatchedListLengths_Throws()
    {
        using var db = new TwoFamilies();

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => db.Db.MultiGet([B("a"), B("b")], [db.Cf1]));

        Assert.Contains("2 keys", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1 column famil", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiGet_RejectsNullArguments()
    {
        using var db = new TwoFamilies();

        Assert.Throws<ArgumentNullException>(() => db.Db.MultiGet(null!, db.Cf1));
        Assert.Throws<ArgumentNullException>(() => db.Db.MultiGet([B("a")], (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(
            () => db.Db.MultiGet([B("a")], (IReadOnlyList<ColumnFamilyHandle>)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.MultiGet([null!], db.Cf1));
        Assert.Throws<ArgumentNullException>(() => db.Db.MultiGet([B("a")], [null!]));
    }

    [Fact]
    public void MultiGet_EmptyLists_ReturnEmpty()
    {
        using var db = new TwoFamilies();

        // The single-argument form still resolves without a cast, which is why
        // the cross-family overload takes parallel lists rather than pairs.
        Assert.Empty(db.Db.MultiGet([]));
        Assert.Empty(db.Db.MultiGet([], db.Cf1));
        Assert.Empty(db.Db.MultiGet([], []));
    }

    [Fact]
    public void MultiGet_HonoursASnapshot()
    {
        using var db = new TwoFamilies();
        db.Db.Put("key", "before", db.Cf1);

        using Snapshot snapshot = db.Db.NewSnapshot();
        using var opts = new ReadOptions();
        opts.SetSnapshot(snapshot);

        db.Db.Put("key", "after", db.Cf1);

        Assert.Equal("before", S(db.Db.MultiGet([B("key")], db.Cf1, opts)[0]));
        Assert.Equal("after", S(db.Db.MultiGet([B("key")], db.Cf1)[0]));
    }

    // ── MultiGetPinned ───────────────────────────────────────────────────────

    [Fact]
    public void MultiGetPinned_ReturnsValuesWithoutCopying()
    {
        using var db = new TwoFamilies();

        db.Db.Put("a", "first", db.Cf1);
        db.Db.Put("b", "second", db.Cf1);

        PinnableSlice?[] results = db.Db.MultiGetPinned([B("a"), B("b"), B("absent")], db.Cf1);

        try
        {
            Assert.Equal(3, results.Length);
            Assert.NotNull(results[0]);
            Assert.NotNull(results[1]);
            Assert.Null(results[2]);

            Assert.Equal("first", results[0]!.ToUtf8String());
            Assert.Equal("second", results[1]!.ToUtf8String());
            Assert.True(results[0]!.Value.SequenceEqual("first"u8));
        }
        finally
        {
            foreach (PinnableSlice? slice in results)
            {
                slice?.Dispose();
            }
        }
    }

    /// <summary>
    /// Sorted input lets RocksDb skip sorting the keys. Passing keys that really
    /// are in order must give the same answer as not claiming it.
    /// </summary>
    [Fact]
    public void MultiGetPinned_SortedInput_MatchesUnsorted()
    {
        using var db = new TwoFamilies();

        for (int i = 0; i < 20; i++)
        {
            db.Db.Put($"key{i:D3}", $"value{i}", db.Cf1);
        }

        byte[][] inOrder = [.. Enumerable.Range(0, 20).Select(i => B($"key{i:D3}"))];

        PinnableSlice?[] sorted = db.Db.MultiGetPinned(inOrder, db.Cf1, sortedInput: true);
        PinnableSlice?[] unsorted = db.Db.MultiGetPinned(inOrder, db.Cf1);

        try
        {
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal($"value{i}", sorted[i]!.ToUtf8String());
                Assert.Equal($"value{i}", unsorted[i]!.ToUtf8String());
            }
        }
        finally
        {
            foreach (PinnableSlice? slice in sorted.Concat(unsorted))
            {
                slice?.Dispose();
            }
        }
    }

    [Fact]
    public void MultiGetPinned_EmptyList_ReturnsEmpty()
    {
        using var db = new TwoFamilies();

        Assert.Empty(db.Db.MultiGetPinned([], db.Cf1));
    }

    [Fact]
    public void MultiGetPinned_RejectsNullArguments()
    {
        using var db = new TwoFamilies();

        Assert.Throws<ArgumentNullException>(() => db.Db.MultiGetPinned(null!, db.Cf1));
        Assert.Throws<ArgumentNullException>(() => db.Db.MultiGetPinned([B("a")], null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.MultiGetPinned([null!], db.Cf1));
    }

    /// <summary>
    /// The slices keep the database alive and must survive collection, the same
    /// as a single pinned read.
    /// </summary>
    [Fact]
    public void MultiGetPinned_ValuesSurviveCollection()
    {
        using var db = new TwoFamilies();
        db.Db.Put("a", "kept", db.Cf1);
        db.Db.Flush(db.Cf1);

        PinnableSlice?[] results = db.Db.MultiGetPinned([B("a")], db.Cf1);

        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();

            Assert.Equal("kept", results[0]!.ToUtf8String());
        }
        finally
        {
            foreach (PinnableSlice? slice in results)
            {
                slice?.Dispose();
            }
        }
    }
}
