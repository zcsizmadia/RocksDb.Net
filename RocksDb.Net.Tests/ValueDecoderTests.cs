using System.Buffers.Binary;
using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// <c>ContainsKey</c>, an exact existence check, and <c>TryGet</c> with a
/// decoder, which reads a value in place instead of copying it to an array.
/// </summary>
public class ValueDecoderTests
{
    private static byte[] Int64(long value)
    {
        byte[] bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return bytes;
    }

    [Fact]
    public void ContainsKey_IsExact_ThroughWritesDeletesAndFlushes()
    {
        using var db = new TempDb();

        Assert.False(db.Db.ContainsKey("k"u8));

        db.Db.Put("k"u8, "v"u8);
        Assert.True(db.Db.ContainsKey("k"u8));
        Assert.True(db.Db.ContainsKey("k"));

        db.Db.Flush();
        Assert.True(db.Db.ContainsKey("k"u8));

        // A tombstone over a flushed value: the key is in a table file, so only
        // a real read can say it is gone.
        db.Db.Delete("k"u8);
        Assert.False(db.Db.ContainsKey("k"u8));

        db.Db.Flush();
        Assert.False(db.Db.ContainsKey("k"u8));
    }

    [Fact]
    public void ContainsKey_FindsAnEmptyValue()
    {
        using var db = new TempDb();
        db.Db.Put("empty"u8, ReadOnlySpan<byte>.Empty);

        Assert.True(db.Db.ContainsKey("empty"u8));
    }

    [Fact]
    public void ContainsKey_ReadsTheGivenColumnFamily()
    {
        using var cfOptions = new DbOptions();
        using var db = new TempDb();
        ColumnFamilyHandle other = db.Db.CreateColumnFamily(cfOptions, "other");

        db.Db.Put("k"u8, "v"u8, other);

        Assert.True(db.Db.ContainsKey("k"u8, other));
        Assert.True(db.Db.ContainsKey("k", other));
        Assert.False(db.Db.ContainsKey("k"u8));
    }

    [Fact]
    public void TryGet_DecodesInPlace_AndSkipsTheDecoderForAMissingKey()
    {
        using var db = new TempDb();
        db.Db.Put("n"u8, Int64(42));

        Assert.True(db.Db.TryGet("n"u8, static v => BinaryPrimitives.ReadInt64LittleEndian(v), out long n));
        Assert.Equal(42, n);

        bool called = false;
        Assert.False(db.Db.TryGet("missing"u8, v => { called = true; return v.Length; }, out int length));
        Assert.False(called);
        Assert.Equal(0, length);
    }

    [Fact]
    public void TryGet_HandsAnEmptyValueToTheDecoder()
    {
        using var db = new TempDb();
        db.Db.Put("empty"u8, ReadOnlySpan<byte>.Empty);

        Assert.True(db.Db.TryGet("empty"u8, static v => v.Length, out int length));
        Assert.Equal(0, length);
    }

    [Fact]
    public void TryGet_WithState_AndWithAColumnFamily()
    {
        using var cfOptions = new DbOptions();
        using var db = new TempDb();
        ColumnFamilyHandle other = db.Db.CreateColumnFamily(cfOptions, "other");

        db.Db.Put("greeting"u8, "hello"u8);
        db.Db.Put("greeting"u8, "hallo"u8, other);

        Encoding encoding = Encoding.UTF8;

        Assert.True(db.Db.TryGet("greeting"u8, encoding, static (v, e) => e.GetString(v), out string? text));
        Assert.Equal("hello", text);

        Assert.True(db.Db.TryGet("greeting"u8, other, static v => Encoding.UTF8.GetString(v), out text));
        Assert.Equal("hallo", text);

        Assert.True(db.Db.TryGet("greeting"u8, other, encoding, static (v, e) => e.GetString(v), out text));
        Assert.Equal("hallo", text);
    }

    /// <summary>
    /// A decoder that throws: the exception reaches the caller unchanged and the
    /// pinned slice is still released.
    /// </summary>
    /// <remarks>
    /// The slice is invisible from here, so release is shown by what an
    /// unreleased one would prevent. A pinned slice registers nothing with the
    /// database on this path, so the database closing cleanly after many throws,
    /// with the block cache still able to evict, is what is left to check.
    /// </remarks>
    [Fact]
    public void TryGet_ADecoderThatThrows_PropagatesAndReleasesTheValue()
    {
        using var db = new TempDb();
        db.Db.Put("k"u8, "v"u8);
        db.Db.Flush();

        for (int i = 0; i < 1000; i++)
        {
            Assert.Throws<FormatException>(() =>
                db.Db.TryGet<int>("k"u8, static _ => throw new FormatException(), out _));
        }

        Assert.True(db.Db.TryGet("k"u8, static v => v.ToArray(), out byte[]? value));
        Assert.Equal("v"u8.ToArray(), value);
    }

    [Fact]
    public void TryGet_RejectsANullDecoder()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.TryGet<int>("k"u8, null!, out _));
    }

    /// <summary>
    /// The point of both: a <c>static</c> decoder returning a value type, and
    /// the existence check, allocate nothing at all.
    /// </summary>
    [Fact]
    public void ContainsKeyAndTryGet_AllocateNothing()
    {
        using var db = new TempDb();
        db.Db.Put("n"u8, Int64(7));

        // Once to warm up, then measured. The same call sites both times: each
        // lambda expression caches its own delegate on first use, which is a
        // one-off allocation per site rather than per call.
        Read(db.Db, 1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        long sum = Read(db.Db, 1000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(8000, sum);
        Assert.Equal(0, allocated);

        static long Read(RocksDb db, int times)
        {
            long sum = 0;
            for (int i = 0; i < times; i++)
            {
                db.TryGet("n"u8, static v => BinaryPrimitives.ReadInt64LittleEndian(v), out long n);
                sum += n;

                if (db.ContainsKey("n"u8))
                {
                    sum++;
                }
            }

            return sum;
        }
    }

    /// <summary>
    /// On a transaction, both see the transaction's own uncommitted writes,
    /// which the database does not see until it commits.
    /// </summary>
    [Fact]
    public void OnATransaction_TheyIncludeItsOwnWrites()
    {
        using var dir = new TempDir();
        using var dbOptions = new DbOptions { CreateIfMissing = true };
        using var txnDbOptions = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);

        db.Put("committed"u8, Int64(1));

        using Transaction txn = db.BeginTransaction();
        txn.Put("pending"u8, Int64(2));

        Assert.True(txn.ContainsKey("pending"u8));
        Assert.True(txn.ContainsKey("committed"u8));
        Assert.False(db.ContainsKey("pending"u8));
        Assert.True(db.ContainsKey("committed"));

        Assert.True(txn.TryGet("pending"u8, static v => BinaryPrimitives.ReadInt64LittleEndian(v), out long pending));
        Assert.Equal(2, pending);
        Assert.False(db.TryGet("pending"u8, static v => BinaryPrimitives.ReadInt64LittleEndian(v), out long _));

        txn.Commit();

        Assert.True(db.TryGet("pending"u8, 10L, static (v, scale) => scale * BinaryPrimitives.ReadInt64LittleEndian(v), out long scaled));
        Assert.Equal(20, scaled);
    }
}
