namespace RocksDbNet.Tests;

public class BackupEngineTests
{
    [Fact]
    public void CreateBackup_RestoreBackup_Works()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string backupPath = dir.Sub("backup");
        string restorePath = dir.Sub("restore");

        // Create DB and write data
        using (var db = RocksDb.Open(new DbOptions { CreateIfMissing = true }, dbPath))
        {
            db.Put("key", "value");

            using var backup = BackupEngine.Open(new DbOptions(), backupPath);
            backup.CreateNewBackup(db, flushBeforeBackup: true);
        }

        // Restore
        using var backup2 = BackupEngine.Open(new DbOptions(), backupPath);
        backup2.RestoreDbFromLatestBackup(restorePath, restorePath);

        // Verify restored data
        using var restored = RocksDb.Open(new DbOptions(), restorePath);
        Assert.Equal("value", restored.GetString("key"));
    }

    [Fact]
    public void BackupInfo_ReturnsMetadata()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string backupPath = dir.Sub("backup");

        using var opts = new DbOptions { CreateIfMissing = true };

        using var db = RocksDb.Open(opts, dbPath);
        db.Put("key", "value");

        using var backup = BackupEngine.Open(opts, backupPath);
        backup.CreateNewBackup(db, flushBeforeBackup: true);

        var infos = backup.AsEnumerable().ToList();

        Assert.Single(infos);
        Assert.True(infos[0].BackupId > 0);
        Assert.True(infos[0].Size > 0);
    }

    [Fact]
    public void PurgeOldBackups_Works()
    {
        using var dir = new TempDir();
        string dbPath = dir.Sub("db");
        string backupPath = dir.Sub("backup");

        using var opts = new DbOptions { CreateIfMissing = true };

        using var db = RocksDb.Open(opts, dbPath);
        db.Put("k1", "v1");

        using var backup = BackupEngine.Open(opts, backupPath);
        backup.CreateNewBackup(db, flushBeforeBackup: true);

        db.Put("k2", "v2");
        backup.CreateNewBackup(db, flushBeforeBackup: true);

        db.Put("k3", "v3");
        backup.CreateNewBackup(db, flushBeforeBackup: true);

        // Keep only the latest
        backup.PurgeOldBackups(1);

        var infos = backup.AsEnumerable().ToList();
        Assert.Single(infos);
    }
}
