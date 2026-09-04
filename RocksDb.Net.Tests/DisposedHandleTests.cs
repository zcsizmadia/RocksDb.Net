namespace RocksDbNet.Tests;

/// <summary>
/// Using a disposed wrapper must be an exception, not a native crash. Issue
/// #122.
/// </summary>
/// <remarks>
/// The C API dereferences whatever pointer it is handed without a null check,
/// so before the guard on <see cref="RocksDbHandle.Handle"/> every one of these
/// took the process down with an access violation that named nothing useful.
/// Each test here would have been a crashing test rather than a failing one.
/// </remarks>
public class DisposedHandleTests
{
    // ── Options passed to a read or a write ─────────────────────────────────

    [Fact]
    public void Get_WithDisposedReadOptions_Throws()
    {
        using var db = new TempDb();

        var readOptions = new ReadOptions();
        readOptions.Dispose();

        db.Db.Put("a", "1");

        Assert.Throws<ObjectDisposedException>(() => db.Db.GetString("a", readOptions));
    }

    [Fact]
    public void Put_WithDisposedWriteOptions_Throws()
    {
        using var db = new TempDb();

        var writeOptions = new WriteOptions();
        writeOptions.Dispose();

        Assert.Throws<ObjectDisposedException>(() => db.Db.Put("a", "1", writeOptions));
    }

    [Fact]
    public void Put_WithDisposedColumnFamily_Throws()
    {
        using var db = new TempDb();
        using var cfOptions = new DbOptions();

        ColumnFamilyHandle cf = db.Db.CreateColumnFamilies(cfOptions, ["events"])[0];
        cf.Dispose();

        Assert.Throws<ObjectDisposedException>(() => db.Db.Put("a", "1", cf));
    }

    [Fact]
    public void NewIterator_WithDisposedReadOptions_Throws()
    {
        using var db = new TempDb();

        var readOptions = new ReadOptions();
        readOptions.Dispose();

        Assert.Throws<ObjectDisposedException>(() => db.Db.NewIterator(readOptions));
    }

    // ── The static entry points ─────────────────────────────────────────────
    //
    // Open was given an explicit guard in #108 after this exact crash. These
    // are the ones that guard never reached.

    [Fact]
    public void Destroy_WithDisposedOptions_Throws()
    {
        using var dir = new TempDir();

        var options = new DbOptions();
        options.Dispose();

        Assert.Throws<ObjectDisposedException>(() => RocksDb.Destroy(options, dir.Path));
    }

    [Fact]
    public void Repair_WithDisposedOptions_Throws()
    {
        using var dir = new TempDir();

        var options = new DbOptions();
        options.Dispose();

        Assert.Throws<ObjectDisposedException>(() => RocksDb.Repair(options, dir.Path));
    }

    [Fact]
    public void ListColumnFamilies_WithDisposedOptions_Throws()
    {
        using var dir = new TempDir();

        var options = new DbOptions();
        options.Dispose();

        Assert.Throws<ObjectDisposedException>(() => RocksDb.ListColumnFamilies(options, dir.Path));
    }

    [Fact]
    public void TransactionDbOpen_WithDisposedOptions_Throws()
    {
        using var dir = new TempDir();
        using var txOptions = new TransactionDbOptions();

        var options = new DbOptions { CreateIfMissing = true };
        options.Dispose();

        Assert.Throws<ObjectDisposedException>(() => TransactionDb.Open(options, txOptions, dir.Path));
    }

    [Fact]
    public void BackupEngineOpen_WithDisposedOptions_Throws()
    {
        using var dir = new TempDir();

        var options = new DbOptions { CreateIfMissing = true };
        options.Dispose();

        Assert.Throws<ObjectDisposedException>(() => BackupEngine.Open(options, dir.Path));
    }

    // ── The database itself ─────────────────────────────────────────────────

    [Fact]
    public void Get_OnDisposedDatabase_Throws()
    {
        using var options = new DbOptions { CreateIfMissing = true };

        var db = TestDb.OpenInMemory(options);
        db.Put("a", "1");
        db.Dispose();

        Assert.Throws<ObjectDisposedException>(() => db.GetString("a"));
    }

    [Fact]
    public void Write_WithDisposedBatch_Throws()
    {
        using var db = new TempDb();

        var batch = new WriteBatch();
        batch.Put("a"u8, "1"u8);
        batch.Dispose();

        Assert.Throws<ObjectDisposedException>(() => db.Db.Write(batch));
    }

    [Fact]
    public void Iterator_AfterDispose_Throws()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");

        var iterator = db.Db.NewIterator();
        iterator.SeekToFirst();
        iterator.Dispose();

        Assert.Throws<ObjectDisposedException>(() => iterator.SeekToFirst());
    }

    [Fact]
    public void Snapshot_AfterDispose_Throws()
    {
        using var db = new TempDb();

        var snapshot = db.Db.NewSnapshot();
        snapshot.Dispose();

        using var readOptions = new ReadOptions();

        Assert.Throws<ObjectDisposedException>(() => readOptions.SetSnapshot(snapshot));
    }
}
