namespace RocksDbNet;

/// <summary>
/// Metadata for a single backup entry.
/// </summary>
public sealed record BackupInfo(
    uint BackupId,
    long Timestamp,
    ulong Size,
    uint NumberFiles)
{
    /// <summary>
    /// Application-supplied metadata attached when the backup was created, or an
    /// empty array when none was. Raw bytes, since the content is opaque to
    /// RocksDb and need not be text.
    /// </summary>
    public byte[] AppMetadata { get; init; } = [];
}

/// <summary>
/// Manages backups of a RocksDb database.
/// Maps to <c>rocksdb_backup_engine_t</c>.
/// </summary>
public sealed class BackupEngine : RocksDbHandle
{
    private BackupEngine(nint handle)
        : base(handle)
    {
    }

    /// <summary>Opens a backup engine at the given path.</summary>
    public static BackupEngine Open(DbOptions options, string backupPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(backupPath);

        nint err = default;
        nint handle = NativeMethods.rocksdb_backup_engine_open(options.Handle, backupPath, ref err);
        NativeMethods.ThrowOnError(err);
        return new BackupEngine(handle);
    }

    /// <summary>
    /// Opens a backup engine configured by <paramref name="options"/>, which
    /// carries the backup directory along with the sharing, rate limiting and
    /// schema settings.
    /// </summary>
    /// <param name="options">Engine configuration. Read here and not retained.</param>
    /// <param name="env">
    /// The environment to read and write through, or <c>null</c> to use the
    /// default environment for the duration of the call.
    /// </param>
    /// <remarks>
    /// The environment is required by the C API, which dereferences it without a
    /// null check, so a default one is created here when the caller passes
    /// <c>null</c>. A caller-supplied <paramref name="env"/> is not retained;
    /// disposing it remains the caller's responsibility.
    /// </remarks>
    public static BackupEngine Open(BackupEngineOptions options, Env? env = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        Env environment = env ?? Env.Create();
        try
        {
            nint err = default;
            nint handle = NativeMethods.rocksdb_backup_engine_open_opts(options.Handle, environment.Handle, ref err);
            NativeMethods.ThrowOnError(err);
            return new BackupEngine(handle);
        }
        finally
        {
            if (env is null)
            {
                environment.Dispose();
            }
        }
    }

    /// <summary>Creates a new backup of the database.</summary>
    public void CreateNewBackup(RocksDb db, bool flushBeforeBackup = false)
    {
        ArgumentNullException.ThrowIfNull(db);

        nint err = default;
        NativeMethods.rocksdb_backup_engine_create_new_backup_flush(Handle, db.Handle, flushBeforeBackup ? (byte)1 : (byte)0, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Creates a new backup using <paramref name="options"/>, and returns the
    /// identifier RocksDb assigned to it.
    /// </summary>
    public unsafe uint CreateNewBackup(RocksDb db, CreateBackupOptions options)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);

        uint backupId = 0;
        nint err = default;
        NativeMethods.rocksdb_backup_engine_create_new_backup_with_options(
            Handle, db.Handle, options.Handle, &backupId, ref err);
        NativeMethods.ThrowOnError(err);
        return backupId;
    }

    /// <summary>
    /// Creates a new backup with application metadata attached, and returns the
    /// identifier RocksDb assigned to it.
    /// </summary>
    /// <param name="db">The database to back up.</param>
    /// <param name="options">Settings for this backup.</param>
    /// <param name="appMetadata">
    /// Opaque bytes stored alongside the backup and returned in
    /// <see cref="BackupInfo.AppMetadata"/>. RocksDb copies them, and treats them
    /// as binary, so they need not be text.
    /// </param>
    public unsafe uint CreateNewBackup(RocksDb db, CreateBackupOptions options, ReadOnlySpan<byte> appMetadata)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);

        uint backupId = 0;
        nint err = default;
        fixed (byte* metadata = appMetadata)
            NativeMethods.rocksdb_backup_engine_create_new_backup_with_metadata(
                Handle, db.Handle, options.Handle, metadata, (nuint)appMetadata.Length, &backupId, ref err);
        NativeMethods.ThrowOnError(err);
        return backupId;
    }

    /// <summary>
    /// Asks a backup running on another thread to stop early. The
    /// <see cref="CreateNewBackup(RocksDb, CreateBackupOptions)"/> call it
    /// interrupts fails with an error rather than returning a partial backup.
    /// </summary>
    public void StopBackup()
        => NativeMethods.rocksdb_backup_engine_stop_backup(Handle);

    /// <summary>Removes all but the <paramref name="numBackupsToKeep"/> most recent backups.</summary>
    public void PurgeOldBackups(uint numBackupsToKeep)
    {
        nint err = default;
        NativeMethods.rocksdb_backup_engine_purge_old_backups(Handle, numBackupsToKeep, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Checks that the files making up <paramref name="backupId"/> are present
    /// and the expected size.
    /// </summary>
    /// <exception cref="RocksDbException">The backup is missing files or damaged.</exception>
    public void VerifyBackup(uint backupId)
    {
        nint err = default;
        NativeMethods.rocksdb_backup_engine_verify_backup(Handle, backupId, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Restores the latest backup to <paramref name="dbDir"/>.</summary>
    public void RestoreDbFromLatestBackup(string dbDir, string walDir)
    {
        ArgumentException.ThrowIfNullOrEmpty(dbDir);
        ArgumentException.ThrowIfNullOrEmpty(walDir);

        using var restoreOptions = new RestoreOptions();
        RestoreDbFromLatestBackup(dbDir, walDir, restoreOptions);
    }

    /// <summary>Restores the latest backup to <paramref name="dbDir"/> using explicit options.</summary>
    public void RestoreDbFromLatestBackup(string dbDir, string walDir, RestoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(dbDir);
        ArgumentException.ThrowIfNullOrEmpty(walDir);

        nint err = default;
        NativeMethods.rocksdb_backup_engine_restore_db_from_latest_backup(Handle, dbDir, walDir, options.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Restores a specific backup to <paramref name="dbDir"/>.</summary>
    public void RestoreDbFromBackup(string dbDir, string walDir, uint backupId, RestoreOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dbDir);
        ArgumentException.ThrowIfNullOrEmpty(walDir);

        RestoreOptions restoreOptions = options ?? new RestoreOptions();
        try
        {
            nint err = default;
            NativeMethods.rocksdb_backup_engine_restore_db_from_backup(
                Handle, dbDir, walDir, restoreOptions.Handle, backupId, ref err);
            NativeMethods.ThrowOnError(err);
        }
        finally
        {
            if (options is null)
            {
                restoreOptions.Dispose();
            }
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
                    NumberFiles: NativeMethods.rocksdb_backup_engine_info_number_files(info, i))
                {
                    AppMetadata = ReadAppMetadata(info, i),
                };
            }
        }
        finally
        {
            NativeMethods.rocksdb_backup_engine_info_destroy(info);
        }
    }

    /// <summary>
    /// Copies one entry's application metadata out. The native pointer belongs to
    /// the info object and dies with it, so this cannot be deferred. It also
    /// cannot live in the iterator above, since an iterator method may not be
    /// <c>unsafe</c>.
    /// </summary>
    private static unsafe byte[] ReadAppMetadata(nint info, int index)
    {
        byte* metadata = NativeMethods.rocksdb_backup_engine_info_app_metadata(info, index, out nuint length);

        return metadata is null || length == 0
            ? []
            : new ReadOnlySpan<byte>(metadata, checked((int)length)).ToArray();
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_backup_engine_close(Handle);
    }
}
