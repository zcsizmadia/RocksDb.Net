using System.Runtime.InteropServices;
using System.Text;

namespace RocksDbNet;

/// <summary>
/// Scheduling priority for the threads that copy files during a backup.
/// </summary>
/// <remarks>
/// Mirrored from <c>include/rocksdb/port_defs.h</c> because the C API declares
/// this parameter as a plain <c>int</c>.
/// </remarks>
public enum CpuPriority
{
    /// <summary>Only run when nothing else wants the CPU.</summary>
    Idle = 0,

    /// <summary>Below normal.</summary>
    Low = 1,

    /// <summary>Normal, the default.</summary>
    Normal = 2,

    /// <summary>Above normal.</summary>
    High = 3,
}

/// <summary>
/// How thoroughly a restore replaces the files already in the destination.
/// </summary>
/// <remarks>
/// Mirrored from <c>RestoreOptions::Mode</c> in
/// <c>include/rocksdb/utilities/backup_engine.h</c>. The values are not
/// contiguous, and <see cref="PurgeAllFiles"/> is the RocksDb default.
/// </remarks>
public enum RestoreMode
{
    /// <summary>
    /// Keep destination files that the latest database session id says are
    /// already correct. The most efficient mode, and the right one for restoring
    /// a healthy database.
    /// </summary>
    KeepLatestDbSessionIdFiles = 1,

    /// <summary>
    /// Checksum each destination file against the backup metadata and replace
    /// only the ones that do not match. Use this when the database is suspected
    /// to be damaged.
    /// </summary>
    VerifyChecksum = 2,

    /// <summary>
    /// Delete everything in the destination and restore every file. Least
    /// efficient and most thorough. This is the default.
    /// </summary>
    PurgeAllFiles = 0xFFFF,
}

/// <summary>
/// Options for opening a <see cref="BackupEngine"/>.
/// Maps to <c>rocksdb_backup_engine_options_t</c>.
/// </summary>
public sealed class BackupEngineOptions : RocksDbHandle
{
    /// <summary>Creates options for a backup engine rooted at <paramref name="backupDir"/>.</summary>
    public BackupEngineOptions(string backupDir)
    {
        ArgumentException.ThrowIfNullOrEmpty(backupDir);

        Handle = NativeMethods.rocksdb_backup_engine_options_create(backupDir);
    }

    /// <summary>The directory backups are written to.</summary>
    public unsafe string BackupDir
    {
        get => NativeMethods.PtrToStringUTF8(
            NativeMethods.rocksdb_backup_engine_options_get_backup_dir(Handle, out nuint length), length) ?? string.Empty;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            fixed (byte* p = Encoding.UTF8.GetBytes(value + '\0'))
                NativeMethods.rocksdb_backup_engine_options_set_backup_dir(Handle, p);
        }
    }

    /// <summary>
    /// If true, table files are shared between backups instead of copied into
    /// each one, which makes incremental backups much cheaper.
    /// </summary>
    public bool ShareTableFiles
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_share_table_files(Handle) != 0;
        set => NativeMethods.rocksdb_backup_engine_options_set_share_table_files(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, each backup is synced to durable storage before it is reported complete.</summary>
    public bool Sync
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_sync(Handle) != 0;
        set => NativeMethods.rocksdb_backup_engine_options_set_sync(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, opening the engine deletes any backups already in the directory.
    /// </summary>
    public bool DestroyOldData
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_destroy_old_data(Handle) != 0;
        set => NativeMethods.rocksdb_backup_engine_options_set_destroy_old_data(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>If true, WAL files are included in the backup.</summary>
    public bool BackupLogFiles
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_backup_log_files(Handle) != 0;
        set => NativeMethods.rocksdb_backup_engine_options_set_backup_log_files(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Backup throughput limit in bytes per second. 0 means unlimited.</summary>
    public ulong BackupRateLimit
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_backup_rate_limit(Handle);
        set => NativeMethods.rocksdb_backup_engine_options_set_backup_rate_limit(Handle, value);
    }

    /// <summary>Restore throughput limit in bytes per second. 0 means unlimited.</summary>
    public ulong RestoreRateLimit
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_restore_rate_limit(Handle);
        set => NativeMethods.rocksdb_backup_engine_options_set_restore_rate_limit(Handle, value);
    }

    /// <summary>Number of threads used to copy files during a backup or restore.</summary>
    public int MaxBackgroundOperations
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_max_background_operations(Handle);
        set => NativeMethods.rocksdb_backup_engine_options_set_max_background_operations(Handle, value);
    }

    /// <summary>
    /// Bytes copied between invocations of the progress callback set on
    /// <see cref="CreateBackupOptions"/>.
    /// </summary>
    public ulong CallbackTriggerIntervalSize
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_callback_trigger_interval_size(Handle);
        set => NativeMethods.rocksdb_backup_engine_options_set_callback_trigger_interval_size(Handle, value);
    }

    /// <summary>
    /// Maximum number of backups to read metadata for when opening. A negative
    /// value means all of them.
    /// </summary>
    public int MaxValidBackupsToOpen
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_max_valid_backups_to_open(Handle);
        set => NativeMethods.rocksdb_backup_engine_options_set_max_valid_backups_to_open(Handle, value);
    }

    /// <summary>
    /// How shared file names encode their checksum. RocksDb does not publish
    /// these values through the C API, so this stays an <c>int</c>.
    /// </summary>
    public int ShareFilesWithChecksumNaming
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_share_files_with_checksum_naming(Handle);
        set => NativeMethods.rocksdb_backup_engine_options_set_share_files_with_checksum_naming(Handle, value);
    }

    // ── File sharing, buffering and schema ───────────────────────────────────

    /// <summary>
    /// If true, shared files are named and matched by checksum as well as size,
    /// which detects a corrupt shared file that happens to be the right length.
    /// </summary>
    public bool ShareFilesWithChecksum
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_share_files_with_checksum(Handle) != 0;
        set => NativeMethods.rocksdb_backup_engine_options_set_share_files_with_checksum(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Size in bytes of the buffer used to copy file contents. 0 lets RocksDb choose.</summary>
    public ulong IoBufferSize
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_io_buffer_size(Handle);
        set => NativeMethods.rocksdb_backup_engine_options_set_io_buffer_size(Handle, value);
    }

    /// <summary>
    /// On-disk schema version for new backups. Newer versions support more
    /// features but cannot be read by older RocksDb releases.
    /// </summary>
    public int SchemaVersion
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_schema_version(Handle);
        set => NativeMethods.rocksdb_backup_engine_options_set_schema_version(Handle, value);
    }

    /// <summary>
    /// If true, the file temperatures observed at backup time take precedence
    /// over the ones recorded in the manifest.
    /// </summary>
    public bool CurrentTemperaturesOverrideManifest
    {
        get => NativeMethods.rocksdb_backup_engine_options_get_current_temperatures_override_manifest(Handle) != 0;
        set => NativeMethods.rocksdb_backup_engine_options_set_current_temperatures_override_manifest(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>Throttles backup I/O using a rate limiter object.</summary>
    /// <remarks>
    /// Write-only, since the C API offers no getter. RocksDb copies the shared
    /// pointer rather than taking ownership, so the caller stays responsible for
    /// disposing <paramref name="rateLimiter"/> and must keep it alive while the
    /// engine is in use. Prefer this over <see cref="BackupRateLimit"/> when the
    /// limiter is shared with other work.
    /// </remarks>
    public BackupEngineOptions SetBackupRateLimiter(RateLimiter? rateLimiter)
    {
        NativeMethods.rocksdb_backup_engine_options_set_backup_rate_limiter(Handle, rateLimiter?.Handle ?? nint.Zero);
        return this;
    }

    /// <summary>Throttles restore I/O using a rate limiter object.</summary>
    /// <remarks>Ownership works exactly as for <see cref="SetBackupRateLimiter"/>.</remarks>
    public BackupEngineOptions SetRestoreRateLimiter(RateLimiter? rateLimiter)
    {
        NativeMethods.rocksdb_backup_engine_options_set_restore_rate_limiter(Handle, rateLimiter?.Handle ?? nint.Zero);
        return this;
    }

    /// <summary>Selects the environment the engine reads and writes through.</summary>
    public BackupEngineOptions SetEnv(Env? env)
    {
        NativeMethods.rocksdb_backup_engine_options_set_env(Handle, env?.Handle ?? nint.Zero);
        return this;
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_backup_engine_options_destroy(Handle);
    }
}

/// <summary>
/// Options for a single <see cref="BackupEngine.CreateNewBackup(RocksDb, CreateBackupOptions)"/> call.
/// Maps to <c>rocksdb_create_backup_options_t</c>.
/// </summary>
/// <remarks>
/// The two callbacks are the reason this type is disposable in its own right.
/// Unlike RocksDb's other callback registrations, these take no destructor, so
/// nothing on the native side ever tells managed code the callback is finished
/// with. This class therefore owns the <see cref="GCHandle"/> that roots each
/// delegate and frees it on dispose.
/// </remarks>
public sealed class CreateBackupOptions : RocksDbHandle
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ProgressCb(nint state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate byte ExcludeFilesCb(nint state, byte* relativeFile, nuint relativeFileLen);

    private ProgressCb? _progressCb;
    private ExcludeFilesCb? _excludeFilesCb;
    private GCHandle _progressState;
    private GCHandle _excludeFilesState;

    public CreateBackupOptions()
        : base(NativeMethods.rocksdb_create_backup_options_create())
    {
    }

    /// <summary>If true, memtables are flushed before the backup is taken.</summary>
    public bool FlushBeforeBackup
    {
        get => NativeMethods.rocksdb_create_backup_options_get_flush_before_backup(Handle) != 0;
        set => NativeMethods.rocksdb_create_backup_options_set_flush_before_backup(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, the pre-backup flush covers every column family atomically, so
    /// the backup is consistent across them.
    /// </summary>
    public bool AtomicFlush
    {
        get => NativeMethods.rocksdb_create_backup_options_get_atomic_flush(Handle) != 0;
        set => NativeMethods.rocksdb_create_backup_options_set_atomic_flush(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// If true, the copy threads run at <see cref="BackgroundThreadCpuPriority"/>
    /// instead of the default priority, keeping a backup from competing with
    /// foreground work.
    /// </summary>
    public bool DecreaseBackgroundThreadCpuPriority
    {
        get => NativeMethods.rocksdb_create_backup_options_get_decrease_background_thread_cpu_priority(Handle) != 0;
        set => NativeMethods.rocksdb_create_backup_options_set_decrease_background_thread_cpu_priority(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Priority for the copy threads. Only applied when
    /// <see cref="DecreaseBackgroundThreadCpuPriority"/> is <c>true</c>.
    /// </summary>
    public CpuPriority BackgroundThreadCpuPriority
    {
        get => (CpuPriority)NativeMethods.rocksdb_create_backup_options_get_background_thread_cpu_priority(Handle);
        set => NativeMethods.rocksdb_create_backup_options_set_background_thread_cpu_priority(Handle, (int)value);
    }

    /// <summary>
    /// Installs a callback invoked as the backup progresses, roughly every
    /// <see cref="BackupEngineOptions.CallbackTriggerIntervalSize"/> bytes.
    /// </summary>
    /// <param name="onProgress">
    /// Called on a backup copy thread, and on several of them concurrently when
    /// <see cref="BackupEngineOptions.MaxBackgroundOperations"/> is above one, so
    /// it must be thread-safe. An exception is caught and reported through
    /// <see cref="RocksDbCallbacks.UnhandledException"/> rather than reaching
    /// native code. Pass <c>null</c> to remove a previously installed callback.
    /// </param>
    public CreateBackupOptions SetProgressCallback(Action? onProgress)
    {
        ThrowIfDisposed();

        // No native destructor exists for this callback, so the old handle has
        // to be released here.
        FreeProgressState();

        if (onProgress is null)
        {
            _progressCb = null;
            NativeMethods.rocksdb_create_backup_options_set_progress_callback(Handle, nint.Zero, nint.Zero);
            return this;
        }

        _progressState = GCHandle.Alloc(onProgress);
        _progressCb = InvokeProgress;

        NativeMethods.rocksdb_create_backup_options_set_progress_callback(
            Handle,
            GCHandle.ToIntPtr(_progressState),
            Marshal.GetFunctionPointerForDelegate(_progressCb));

        return this;
    }

    /// <summary>
    /// Installs a callback that decides which files to leave out of the backup.
    /// </summary>
    /// <param name="shouldExclude">
    /// Called once per candidate file with its path relative to the database
    /// directory. Return <c>true</c> to exclude the file. Runs on backup copy
    /// threads, so it must be thread-safe. An exception is caught, reported, and
    /// treated as "do not exclude", since wrongly excluding a file would leave an
    /// incomplete backup. Pass <c>null</c> to remove a previously installed
    /// callback.
    /// </param>
    /// <remarks>
    /// <para>
    /// Requires <see cref="BackupEngineOptions.SchemaVersion"/> to be 2 or
    /// higher. RocksDb fails the backup with an "exclude_files_callback requires
    /// schema_version &gt;= 2" error otherwise.
    /// </para>
    /// <para>
    /// A backup taken with exclusions can only be restored by supplying the
    /// other directories holding the excluded files.
    /// </para>
    /// </remarks>
    public unsafe CreateBackupOptions SetExcludeFilesCallback(Func<string, bool>? shouldExclude)
    {
        ThrowIfDisposed();

        FreeExcludeFilesState();

        if (shouldExclude is null)
        {
            _excludeFilesCb = null;
            NativeMethods.rocksdb_create_backup_options_set_exclude_files_callback(Handle, nint.Zero, nint.Zero);
            return this;
        }

        _excludeFilesState = GCHandle.Alloc(shouldExclude);
        _excludeFilesCb = InvokeExcludeFiles;

        NativeMethods.rocksdb_create_backup_options_set_exclude_files_callback(
            Handle,
            GCHandle.ToIntPtr(_excludeFilesState),
            Marshal.GetFunctionPointerForDelegate(_excludeFilesCb));

        return this;
    }

    private static void InvokeProgress(nint state)
    {
        try
        {
            if (GCHandle.FromIntPtr(state).Target is Action onProgress)
            {
                onProgress();
            }
        }
        catch (Exception ex)
        {
            // RocksDb ignores the outcome of this callback, so reporting and
            // continuing loses nothing.
            RocksDbCallbacks.Report(nameof(SetProgressCallback), ex, state);
        }
    }

    private static unsafe byte InvokeExcludeFiles(nint state, byte* relativeFile, nuint relativeFileLen)
    {
        try
        {
            if (GCHandle.FromIntPtr(state).Target is not Func<string, bool> shouldExclude)
            {
                return 0;
            }

            string path = NativeMethods.PtrToStringUTF8(relativeFile, relativeFileLen) ?? string.Empty;
            return shouldExclude(path) ? (byte)1 : (byte)0;
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report(nameof(SetExcludeFilesCallback), ex, state);

            // Including the file is the safe answer: excluding one by mistake
            // would produce a backup that cannot be restored on its own.
            return 0;
        }
    }

    private void FreeProgressState()
    {
        if (_progressState.IsAllocated)
        {
            _progressState.Free();
        }

        _progressState = default;
    }

    private void FreeExcludeFilesState()
    {
        if (_excludeFilesState.IsAllocated)
        {
            _excludeFilesState.Free();
        }

        _excludeFilesState = default;
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_create_backup_options_destroy(Handle);
    }

    protected override void DisposeUnmanagedResources()
    {
        // Destroy the native options first so RocksDb cannot invoke a callback
        // whose state we are about to free.
        base.DisposeUnmanagedResources();

        FreeProgressState();
        FreeExcludeFilesState();

        _progressCb = null;
        _excludeFilesCb = null;
    }
}

/// <summary>
/// Options for restoring a database from a backup.
/// Maps to <c>rocksdb_restore_options_t</c>.
/// </summary>
public sealed class RestoreOptions : RocksDbHandle
{
    public RestoreOptions()
        : base(NativeMethods.rocksdb_restore_options_create())
    {
    }

    /// <summary>
    /// If true, existing WAL files in the destination are kept rather than
    /// overwritten, and archived logs are moved into the WAL directory.
    /// </summary>
    public bool KeepLogFiles
    {
        get => NativeMethods.rocksdb_restore_options_get_keep_log_files(Handle) != 0;
        set => NativeMethods.rocksdb_restore_options_set_keep_log_files(Handle, value ? 1 : 0);
    }

    /// <summary>
    /// How thoroughly the restore replaces files already in the destination.
    /// Defaults to <see cref="RestoreMode.PurgeAllFiles"/>.
    /// </summary>
    public RestoreMode Mode
    {
        get => (RestoreMode)NativeMethods.rocksdb_restore_options_get_mode(Handle);
        set => NativeMethods.rocksdb_restore_options_set_mode(Handle, (int)value);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_restore_options_destroy(Handle);
    }
}
