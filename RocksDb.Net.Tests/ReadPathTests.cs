using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// The read path after it moved onto pinned reads, pooled key encoding, batched
/// multi-gets and a linked child list. See issues #184, #185, #186 and #189.
/// </summary>
/// <remarks>
/// Each of those changes swapped the mechanism under an existing API, so these
/// pin down the edges a new mechanism is likeliest to get wrong: empty keys and
/// values, non-ASCII text, sizes past the pool's smallest buckets, missing and
/// repeated keys, and disposal in every order.
/// </remarks>
public class ReadPathTests
{
    // ── #184: reads through pinned slices ────────────────────────────────────

    [Fact]
    public void Get_DistinguishesAMissingKeyFromAnEmptyValue()
    {
        using var db = new TempDb();
        db.Db.Put("empty"u8, ReadOnlySpan<byte>.Empty);

        Assert.Null(db.Db.Get("missing"u8));
        byte[]? empty = db.Db.Get("empty"u8);
        Assert.NotNull(empty);
        Assert.Empty(empty);
        Assert.Equal(string.Empty, db.Db.GetString("empty"));
        Assert.Null(db.Db.GetString("missing"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4 * 1024)]
    [InlineData(1024 * 1024)]
    public void Get_ReturnsTheWholeValue(int size)
    {
        using var db = new TempDb();
        byte[] value = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();

        db.Db.Put("k"u8, value);
        db.Db.Flush();

        Assert.Equal(value, db.Db.Get("k"u8));

        ColumnFamilyHandle cf = db.Db.GetDefaultColumnFamily();
        Assert.Equal(value, db.Db.Get("k"u8, cf));
    }

    [Fact]
    public void Get_SeesATransactionsOwnWritesAndTheDatabase()
    {
        using var dir = new TempDir();
        using var dbOptions = new DbOptions { CreateIfMissing = true };
        using var txnOptions = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOptions, txnOptions, dir.Path);

        db.Put("committed", "db");

        using Transaction txn = db.BeginTransaction();
        txn.Put("pending", "txn");

        Assert.Equal("db", txn.GetString("committed"));
        Assert.Equal("txn", txn.GetString("pending"));
        Assert.Equal("txn", txn.GetStringForUpdate("pending"));
        Assert.Null(txn.GetString("missing"));

        Assert.Equal("db", db.GetString("committed"));
        Assert.Null(db.GetString("pending"));
    }

    // ── #189: string overloads through pooled buffers ────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("k")]
    [InlineData("ключ-キー-🔑")]
    public void StringOverloads_RoundTripKeysAndValues(string key)
    {
        using var db = new TempDb();
        string value = "значение-値-" + key;

        db.Db.Put(key, value);
        Assert.Equal(value, db.Db.GetString(key));
        Assert.Equal(Encoding.UTF8.GetBytes(value), db.Db.Get(key));

        db.Db.Delete(key);
        Assert.Null(db.Db.GetString(key));
    }

    /// <summary>
    /// Keys and values larger than any small pool bucket, and a value written
    /// straight after a long key, so a mistake in where the second string
    /// starts in the shared buffer shows up as corrupted bytes.
    /// </summary>
    [Fact]
    public void StringOverloads_HandleLongKeysAndValues()
    {
        using var db = new TempDb();
        string key = new('k', 70_000);
        string value = string.Concat(Enumerable.Repeat("v€", 50_000));

        db.Db.Put(key, value);
        Assert.Equal(value, db.Db.GetString(key));

        using var batch = new WriteBatch();
        batch.Put(key + "2", value + "2");
        db.Db.Write(batch);
        Assert.Equal(value + "2", db.Db.GetString(key + "2"));
    }

    [Fact]
    public void StringOverloads_RejectNull()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.Put(null!, "v"));
        Assert.Throws<ArgumentNullException>(() => db.Db.Put("k", null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.GetString(null!));
    }

    [Fact]
    public void IteratorSeek_WithAStringKey_DoesNotDependOnTheBufferAfterTheCall()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");

        using Iterator it = db.Db.NewIterator();
        it.Seek("b");

        // Reuse the pool heavily, so a seek that kept a pointer into the
        // returned buffer would now see different bytes.
        for (int i = 0; i < 100; i++)
        {
            db.Db.Put("zz" + i, new string('x', 64));
        }

        Assert.True(it.IsValid());
        Assert.Equal("b", it.KeyAsString());
        it.Next();
        Assert.Equal("c", it.KeyAsString());
    }

    // ── #185: batched multi-gets ─────────────────────────────────────────────

    [Fact]
    public void MultiGet_HandlesMissingRepeatedAndEmptyKeys()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put(ReadOnlySpan<byte>.Empty, "empty-key"u8);

        byte[][] keys = ["a"u8.ToArray(), "missing"u8.ToArray(), "a"u8.ToArray(), []];

        byte[]?[] values = db.Db.MultiGet(keys);

        Assert.Equal("1"u8.ToArray(), values[0]);
        Assert.Null(values[1]);
        Assert.Equal("1"u8.ToArray(), values[2]);
        Assert.Equal("empty-key"u8.ToArray(), values[3]);
    }

    /// <summary>
    /// Keys in reverse order, since the batched read sorts them internally:
    /// results have to come back in the order asked for.
    /// </summary>
    [Fact]
    public void MultiGet_ReturnsResultsInRequestOrder()
    {
        using var db = new TempDb();
        ColumnFamilyHandle cf = db.Db.GetDefaultColumnFamily();

        for (int i = 0; i < 500; i++)
        {
            db.Db.Put($"key{i:D4}", $"value{i}");
        }

        db.Db.Flush();

        byte[][] keys = [.. Enumerable.Range(0, 500).Reverse().Select(i => Encoding.UTF8.GetBytes($"key{i:D4}"))];

        byte[]?[] fromDefault = db.Db.MultiGet(keys);
        byte[]?[] fromFamily = db.Db.MultiGet(keys, cf);
        byte[]?[] perKeyFamily = db.Db.MultiGet(keys, Enumerable.Repeat(cf, keys.Length).ToArray());

        for (int i = 0; i < keys.Length; i++)
        {
            string expected = $"value{499 - i}";
            Assert.Equal(expected, Encoding.UTF8.GetString(fromDefault[i]!));
            Assert.Equal(expected, Encoding.UTF8.GetString(fromFamily[i]!));
            Assert.Equal(expected, Encoding.UTF8.GetString(perKeyFamily[i]!));
        }
    }

    [Fact]
    public void MultiGet_RejectsANullKeyBeforeReadingAnything()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.MultiGet(["a"u8.ToArray(), null!]));
    }

    [Fact]
    public void TransactionMultiGet_SeesPendingWrites()
    {
        using var dir = new TempDir();
        using var dbOptions = new DbOptions { CreateIfMissing = true };
        using var txnOptions = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOptions, txnOptions, dir.Path);

        db.Put("a", "db");

        using Transaction txn = db.BeginTransaction();
        txn.Put("b", "txn");

        byte[]?[] values = txn.MultiGet(["a"u8.ToArray(), "b"u8.ToArray(), "c"u8.ToArray()]);

        Assert.Equal("db"u8.ToArray(), values[0]);
        Assert.Equal("txn"u8.ToArray(), values[1]);
        Assert.Null(values[2]);
    }

    // ── #186: the linked child list ──────────────────────────────────────────

    [Theory]
    [InlineData("forward")]
    [InlineData("reverse")]
    [InlineData("interleaved")]
    public void PinnedSlices_LeaveTheChildListInAnyDisposalOrder(string order)
    {
        using var db = new TempDb();
        ColumnFamilyHandle cf = db.Db.GetDefaultColumnFamily();

        byte[][] keys = [.. Enumerable.Range(0, 200).Select(i => Encoding.UTF8.GetBytes($"k{i}"))];
        foreach (byte[] key in keys)
        {
            db.Db.Put(key, key);
        }

        int before = db.Db.ChildCount;

        PinnableSlice?[] slices = db.Db.MultiGetPinned(keys, cf);
        Assert.Equal(before + keys.Length, db.Db.ChildCount);

        IEnumerable<int> indices = order switch
        {
            "forward" => Enumerable.Range(0, slices.Length),
            "reverse" => Enumerable.Range(0, slices.Length).Reverse(),
            _ => Enumerable.Range(0, slices.Length).Where(i => i % 2 == 0)
                    .Concat(Enumerable.Range(0, slices.Length).Where(i => i % 2 == 1)),
        };

        int remaining = slices.Length;
        foreach (int i in indices)
        {
            Assert.Equal(keys[i], slices[i]!.ToArray());
            slices[i]!.Dispose();
            remaining--;
            Assert.Equal(before + remaining, db.Db.ChildCount);
        }
    }

    /// <summary>
    /// Closing the database releases whatever is still open, newest first, and
    /// leaves each one disposed.
    /// </summary>
    [Fact]
    public void ClosingTheDatabase_ReleasesEveryOpenChild()
    {
        var db = new TempDb();
        db.Db.Put("k", "v");

        var children = new List<RocksDbHandle>();
        for (int i = 0; i < 50; i++)
        {
            children.Add(db.Db.GetPinned("k"u8)!);
            children.Add(db.Db.NewIterator());
            children.Add(db.Db.NewSnapshot());
        }

        // Some disposed already, from the middle of the list.
        for (int i = 10; i < 40; i++)
        {
            children[i].Dispose();
        }

        db.Dispose();

        Assert.All(children, child => Assert.True(child.IsDisposed));
    }

    [Fact]
    public void PinnableSlice_ValueAndLengthAgree_AndThrowAfterDisposal()
    {
        using var db = new TempDb();
        db.Db.Put("k", "value");

        PinnableSlice slice = db.Db.GetPinned("k"u8)!;
        Assert.Equal(5, slice.Length);
        Assert.Equal("value", Encoding.UTF8.GetString(slice.Value));

        slice.Dispose();
        Assert.Throws<ObjectDisposedException>(() => slice.Length);
        Assert.Throws<ObjectDisposedException>(() => slice.Value.Length);
    }
}
