using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>
/// Metadata for a single backup entry.
/// </summary>
public sealed record BackupInfo(
    uint BackupId,
    long Timestamp,
    ulong Size,
    uint NumberFiles);

/// <summary>
/// Manages backups of a RocksDB database.
/// Maps to <c>rocksdb_backup_engine_t</c>.
/// </summary>
public sealed class BackupEngine : RocksDbHandle
{
    private BackupEngine(nint handle)
    {
        Handle = handle;
    }

    /// <summary>Opens a backup engine at the given path.</summary>
    public static BackupEngine Open(DbOptions options, string backupPath)
    {
        nint err = default;
        nint handle = NativeMethods.rocksdb_backup_engine_open(options.Handle, backupPath, ref err);
        NativeMethods.ThrowOnError(err);
        return new BackupEngine(handle);
    }

    /// <summary>Creates a new backup of the database.</summary>
    public void CreateNewBackup(RocksDb db, bool flushBeforeBackup = false)
    {
        nint err = default;
        NativeMethods.rocksdb_backup_engine_create_new_backup_flush(Handle, db.Handle, flushBeforeBackup ? (byte)1 : (byte)0, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Removes all but the <paramref name="numBackupsToKeep"/> most recent backups.</summary>
    public void PurgeOldBackups(uint numBackupsToKeep)
    {
        nint err = default;
        NativeMethods.rocksdb_backup_engine_purge_old_backups(Handle, numBackupsToKeep, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Restores the latest backup to <paramref name="dbDir"/>.</summary>
    public void RestoreDbFromLatestBackup(string dbDir, string walDir)
    {
        nint restoreOpts = NativeMethods.rocksdb_restore_options_create();
        try
        {
            nint err = default;
            NativeMethods.rocksdb_backup_engine_restore_db_from_latest_backup(Handle, dbDir, walDir, restoreOpts, ref err);
            NativeMethods.ThrowOnError(err);
        }
        finally
        {
            NativeMethods.rocksdb_restore_options_destroy(restoreOpts);
        }
    }

    /// <summary>Returns metadata for all available backups (newest first).</summary>
    public IEnumerable<BackupInfo> AsEnumerable()
    {
        nint info = NativeMethods.rocksdb_backup_engine_get_backup_info(Handle);
        try
        {
            int count = NativeMethods.rocksdb_backup_engine_info_count(info);
            for (int i = 0; i < count; i++)
            {
                yield return new BackupInfo(
                    BackupId: NativeMethods.rocksdb_backup_engine_info_backup_id(info, i),
                    Timestamp: NativeMethods.rocksdb_backup_engine_info_timestamp(info, i),
                    Size: NativeMethods.rocksdb_backup_engine_info_size(info, i),
                    NumberFiles: NativeMethods.rocksdb_backup_engine_info_number_files(info, i));
            }
        }
        finally
        {
            NativeMethods.rocksdb_backup_engine_info_destroy(info);
        }
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_backup_engine_close(Handle);
    }
}