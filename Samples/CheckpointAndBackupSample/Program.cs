using RocksDbNet;

// ─── Checkpoint & Backup : data safety and fast cloning ───────────────────────
// BackupEngine creates incremental backups that can be restored later.
// Checkpoint creates a hard-link-based instant copy of the database.

const string dbPath = "backup_checkpoint_db";
const string backupPath = "backup_checkpoint_backups";
const string checkpointPath = "backup_checkpoint_clone";
const string restorePath = "backup_checkpoint_restored";

// Clean up previous runs
foreach (var dir in new[] { dbPath, backupPath, checkpointPath, restorePath })
    if (Directory.Exists(dir)) Directory.Delete(dir, true);

// ── BackupEngine ───────────────────────────────────────────────────────────
Console.WriteLine("=== BackupEngine ===");

using (var db = RocksDb.Open(new DbOptions { CreateIfMissing = true }, dbPath))
{
    db.Put("version", "1");
    db.Put("data", "first version");

    // Create first backup
    using var backupOpts = new DbOptions();
    using var backup = BackupEngine.Open(backupOpts, backupPath);
    backup.CreateNewBackup(db);
    Console.WriteLine("  Created backup #1");

    // Modify data and create second backup
    db.Put("version", "2");
    db.Put("data", "second version");
    db.Put("extra", "only in v2");
    backup.CreateNewBackup(db);
    Console.WriteLine("  Created backup #2");

    // List backups
    foreach (var info in backup.AsEnumerable())
        Console.WriteLine($"  Backup #{info.BackupId}: {info.NumberFiles} files, {info.Size} bytes");

    // Purge old backups (keep only the latest)
    backup.PurgeOldBackups(1);
    Console.WriteLine("  Purged old backups (kept latest only)");
}

// Restore from latest backup
using (var backupOpts = new DbOptions())
using (var backup = BackupEngine.Open(backupOpts, backupPath))
{
    backup.RestoreDbFromLatestBackup(restorePath, restorePath);
    Console.WriteLine($"  Restored to: {restorePath}");
}

// Verify restored data
using (var restored = RocksDb.Open(new DbOptions { CreateIfMissing = false }, restorePath))
{
    Console.WriteLine($"  Restored version: {restored.GetString("version")}");
    Console.WriteLine($"  Restored data:    {restored.GetString("data")}");
    Console.WriteLine($"  Restored extra:   {restored.GetString("extra")}");
}

// ── Checkpoint ─────────────────────────────────────────────────────────────
Console.WriteLine("\n=== Checkpoint ===");

using (var db = RocksDb.Open(new DbOptions { CreateIfMissing = false }, dbPath))
{
    db.Put("checkpoint_marker", "present");

    using var cp = Checkpoint.Create(db);
    cp.CreateCheckpoint(checkpointPath);
    Console.WriteLine($"  Created checkpoint at: {checkpointPath}");
}

// Open the checkpoint as a separate database
using (var clone = RocksDb.Open(new DbOptions(), checkpointPath))
{
    Console.WriteLine($"  Clone version:          {clone.GetString("version")}");
    Console.WriteLine($"  Clone checkpoint_marker: {clone.GetString("checkpoint_marker")}");
}

Console.WriteLine("\nBackup & Checkpoint sample completed.");
