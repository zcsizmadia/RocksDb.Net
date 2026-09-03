using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Reads that avoid copying the value into managed memory. See issue #68.
/// </summary>
public class PinnedReadTests
{
    [Fact]
    public void GetPinned_ReturnsTheValueWithoutCopying()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");

        using PinnableSlice? slice = db.Db.GetPinned("key"u8);

        Assert.NotNull(slice);
        Assert.Equal(5, slice.Length);
        Assert.True(slice.Value.SequenceEqual("value"u8));
        Assert.Equal("value", slice.ToUtf8String());
        Assert.Equal("value"u8.ToArray(), slice.ToArray());
    }

    [Fact]
    public void GetPinned_MissingKey_ReturnsNull()
    {
        using var db = new TempDb();

        using PinnableSlice? slice = db.Db.GetPinned("absent"u8);

        Assert.Null(slice);
    }

    /// <summary>
    /// An empty value is a real value, not an absent one, and the two return
    /// paths look identical natively.
    /// </summary>
    [Fact]
    public void GetPinned_EmptyValue_IsNotConfusedWithAbsent()
    {
        using var db = new TempDb();
        db.Db.Put("key"u8, []);

        using PinnableSlice? slice = db.Db.GetPinned("key"u8);

        Assert.NotNull(slice);
        Assert.Equal(0, slice.Length);
        Assert.True(slice.Value.IsEmpty);
    }

    [Fact]
    public void GetPinned_ColumnFamily_ReadsFromThatFamilyOnly()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");

        db.Put("key", "in-cf1", cf1);

        using PinnableSlice? fromCf = db.GetPinned("key"u8, cf1);
        Assert.NotNull(fromCf);
        Assert.Equal("in-cf1", fromCf.ToUtf8String());

        using PinnableSlice? fromDefault = db.GetPinned("key"u8);
        Assert.Null(fromDefault);

        Assert.Throws<ArgumentNullException>(() => db.GetPinned("key"u8, (ColumnFamilyHandle)null!));
    }

    /// <summary>
    /// The value must stay readable across writes, a flush and a compaction,
    /// because that is the whole promise of pinning it.
    /// </summary>
    [Fact]
    public void GetPinned_ValueSurvivesFlushAndCompaction()
    {
        using var db = new TempDb();
        db.Db.Put("key", "original");
        db.Db.Flush();

        using PinnableSlice? slice = db.Db.GetPinned("key"u8);
        Assert.NotNull(slice);

        // Overwrite, flush and compact while the slice is held.
        db.Db.Put("key", "replaced");
        for (int i = 0; i < 200; i++)
        {
            db.Db.Put($"filler{i:D4}", new string('x', 512));
        }

        db.Db.Flush();
        db.Db.CompactRange();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        Assert.Equal("original", slice.ToUtf8String());
        Assert.Equal("replaced", db.Db.GetString("key"));
    }

    [Fact]
    public void GetPinned_HonoursASnapshot()
    {
        using var db = new TempDb();
        db.Db.Put("key", "before");

        using Snapshot snapshot = db.Db.NewSnapshot();
        using var opts = new ReadOptions();
        opts.SetSnapshot(snapshot);

        db.Db.Put("key", "after");

        using PinnableSlice? asOfSnapshot = db.Db.GetPinned("key"u8, opts);
        Assert.NotNull(asOfSnapshot);
        Assert.Equal("before", asOfSnapshot.ToUtf8String());
    }

    [Fact]
    public void PinnableSlice_AfterDispose_Throws()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");

        PinnableSlice? slice = db.Db.GetPinned("key"u8);
        Assert.NotNull(slice);
        slice.Dispose();

        Assert.Throws<ObjectDisposedException>(() => slice.Length);
        Assert.Throws<ObjectDisposedException>(() => slice.ToArray());
    }

    /// <summary>
    /// A leaked slice must not crash when it is finalized after the database has
    /// been closed.
    /// </summary>
    /// <remarks>
    /// Unlike the parent-child cases in <see cref="LifetimeTests"/>, this one does
    /// not fail without the parent link: removing it and re-running this test
    /// still passes, including when the value is served from an SST through an
    /// explicit block cache rather than from the memtable. So RocksDb's own
    /// cleanup appears to hold what it needs. The parent link stays because it
    /// costs nothing, matches every other child handle, and guarantees the
    /// ordering rather than relying on that observation.
    /// </remarks>
    [Fact]
    public void LeakedSlice_FinalizedAfterTheDatabaseIsClosed_DoesNotCrash()
    {
        using var dir = new TempDir();

        ReadAndAbandon(dir.Path);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        using var opts = new DbOptions { CreateIfMissing = true };
        using var reopened = RocksDb.Open(opts, dir.Path);
        Assert.Equal("value", reopened.GetString("key"));
    }

    // Separated so the slice cannot stay alive in a local of the test method.
    //
    // The flush matters. A value still in the memtable is copied into the slice,
    // so abandoning it is harmless. A value read out of an SST through the block
    // cache is pinned to a cache entry instead, and releasing that entry after
    // the cache has gone is the actual hazard.
    private static void ReadAndAbandon(string path)
    {
        var cache = Cache.CreateLru(8 * 1024 * 1024);
        var tableOptions = new BlockBasedTableOptions();
        tableOptions.SetBlockCache(cache);

        var opts = new DbOptions { CreateIfMissing = true };
        opts.BlockBasedTableFactory = tableOptions;

        using var db = RocksDb.Open(opts, path);

        db.Put("key", "value");
        for (int i = 0; i < 200; i++)
        {
            db.Put($"filler{i:D4}", new string('x', 512));
        }

        db.Flush();

        // Now served from an SST block held in the cache.
        PinnableSlice? slice = db.GetPinned("key"u8);
        Assert.NotNull(slice);
        Assert.Equal("value", slice.ToUtf8String());

        // Deliberately not disposed, and deliberately not returned.
    }

    /// <summary>Disposing the slice explicitly after the database is also safe.</summary>
    [Fact]
    public void Slice_DisposedAfterTheDatabaseIsClosed_DoesNotCrash()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };

        PinnableSlice? slice;
        using (var db = RocksDb.Open(opts, dir.Path))
        {
            db.Put("key", "value");
            slice = db.GetPinned("key"u8);
        }

        Assert.NotNull(slice);
        slice.Dispose();
        Assert.True(slice.IsDisposed);
    }

    // ── TryGetInto ───────────────────────────────────────────────────────────

    [Fact]
    public void TryGetInto_FillsTheBufferAndReportsTheLength()
    {
        using var db = new TempDb();
        db.Db.Put("key", "value");

        Span<byte> buffer = stackalloc byte[16];
        bool copied = db.Db.TryGetInto("key"u8, buffer, out int length);

        Assert.True(copied);
        Assert.Equal(5, length);
        Assert.True(buffer[..length].SequenceEqual("value"u8));
    }

    /// <summary>
    /// The point of the out parameter: a caller handed false can size a buffer
    /// from it and retry, rather than guessing.
    /// </summary>
    [Fact]
    public void TryGetInto_BufferTooSmall_ReportsTheRequiredLength()
    {
        using var db = new TempDb();
        db.Db.Put("key", "a longer value than the buffer");

        Span<byte> tooSmall = stackalloc byte[4];
        Assert.False(db.Db.TryGetInto("key"u8, tooSmall, out int required));
        Assert.Equal(30, required);

        Span<byte> right = new byte[required];
        Assert.True(db.Db.TryGetInto("key"u8, right, out int length));
        Assert.Equal(required, length);
        Assert.Equal("a longer value than the buffer", Encoding.UTF8.GetString(right[..length]));
    }

    /// <summary>
    /// Absent and too-small both return false, so the length is what tells them
    /// apart.
    /// </summary>
    [Fact]
    public void TryGetInto_MissingKey_ReportsZeroLength()
    {
        using var db = new TempDb();

        Span<byte> buffer = stackalloc byte[16];

        Assert.False(db.Db.TryGetInto("absent"u8, buffer, out int length));
        Assert.Equal(0, length);
    }

    [Fact]
    public void TryGetInto_EmptyValue_SucceedsIntoAnyBuffer()
    {
        using var db = new TempDb();
        db.Db.Put("key"u8, []);

        Assert.True(db.Db.TryGetInto("key"u8, [], out int length));
        Assert.Equal(0, length);
    }

    [Fact]
    public void TryGetInto_ColumnFamily_ReadsFromThatFamilyOnly()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");

        db.Put("key", "in-cf1", cf1);

        // A heap array rather than stackalloc, because the null-guard assertion
        // below needs to capture it in a lambda.
        byte[] buffer = new byte[16];

        Assert.True(db.TryGetInto("key"u8, cf1, buffer, out int length));
        Assert.Equal("in-cf1", Encoding.UTF8.GetString(buffer.AsSpan(0, length)));

        Assert.False(db.TryGetInto("key"u8, buffer, out int missing));
        Assert.Equal(0, missing);

        Assert.Throws<ArgumentNullException>(
            () => db.TryGetInto("key"u8, null!, buffer, out _));
    }
}
