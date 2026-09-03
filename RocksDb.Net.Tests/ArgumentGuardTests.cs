namespace RocksDbNet.Tests;

/// <summary>
/// Asserts that bad arguments produce an exception naming the parameter, rather
/// than reaching native code. See issue #62.
/// </summary>
/// <remarks>
/// The stakes are higher here than for ordinary argument validation. A null
/// column family handle was dereferenced without a check, and a null string was
/// marshalled through as a null <c>const char*</c> into a <c>std::string</c>
/// constructor, which is undefined behaviour and in practice a crash. Some
/// methods guarded and comparable ones did not, so the inconsistency was
/// arbitrary rather than considered.
/// </remarks>
public class ArgumentGuardTests
{
    [Fact]
    public void ColumnFamilyOverloads_RejectNullHandle()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.Put("k", "v", (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.Delete("k", (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.Merge("k", "v", (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.Get("k"u8, (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.GetString("k", (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.KeyMayExist("k", (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.NewIterator((ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => db.Db.GetProperty("rocksdb.stats", null!));
    }

    [Fact]
    public void WriteBatchColumnFamilyOverloads_RejectNullHandle()
    {
        using var batch = new WriteBatch();

        Assert.Throws<ArgumentNullException>(() => batch.Put("k"u8, "v"u8, (ColumnFamilyHandle)null!));
        Assert.Throws<ArgumentNullException>(() => batch.Delete("k"u8, (ColumnFamilyHandle)null!));
    }

    /// <summary>
    /// A null string used to be handed to native code as a null pointer.
    /// </summary>
    [Fact]
    public void StringParameters_RejectNullAndEmpty()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.GetProperty(null!));
        Assert.Throws<ArgumentException>(() => db.Db.GetProperty(string.Empty));

        Assert.Throws<ArgumentNullException>(() => Checkpoint.Create(null!));

        using Checkpoint checkpoint = Checkpoint.Create(db.Db);
        Assert.Throws<ArgumentNullException>(() => checkpoint.CreateCheckpoint(null!));
        Assert.Throws<ArgumentException>(() => checkpoint.CreateCheckpoint(string.Empty));
    }

    [Fact]
    public void BackupEngine_RejectsNullArguments()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };

        Assert.Throws<ArgumentNullException>(() => BackupEngine.Open(opts, null!));
        Assert.Throws<ArgumentException>(() => BackupEngine.Open(opts, string.Empty));
        Assert.Throws<ArgumentNullException>(() => BackupEngine.Open((DbOptions)null!, dir.Path));

        using var engine = BackupEngine.Open(opts, dir.Sub("backups"));

        Assert.Throws<ArgumentNullException>(() => engine.CreateNewBackup(null!));
        Assert.Throws<ArgumentNullException>(() => engine.RestoreDbFromLatestBackup(null!, dir.Path));
        Assert.Throws<ArgumentNullException>(() => engine.RestoreDbFromLatestBackup(dir.Path, null!));
        Assert.Throws<ArgumentNullException>(() => engine.RestoreDbFromBackup(null!, dir.Path, 1));
    }
}
