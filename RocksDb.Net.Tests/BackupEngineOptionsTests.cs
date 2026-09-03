namespace RocksDbNet.Tests;

/// <summary>
/// Covers <see cref="BackupEngineOptions"/>, <see cref="CreateBackupOptions"/>,
/// <see cref="RestoreOptions"/> and the <see cref="BackupEngine"/> members that
/// use them. See issue #25.
/// </summary>
public class BackupEngineOptionsTests
{
    /// <summary>
    /// Fresh options for one open call.
    /// </summary>
    /// <remarks>
    /// <see cref="RocksDb.Open(DbOptions, string)"/> takes ownership of the
    /// options it is given, so each open needs its own instance. Sharing one and
    /// then reusing it after the database closes is a use-after-free.
    /// <see cref="BackupEngine.Open(DbOptions, string)"/> does not take
    /// ownership, so the caller disposes those.
    /// </remarks>
    private static DbOptions NewDbOptions() => new() { CreateIfMissing = true };

    // ── BackupEngineOptions ──────────────────────────────────────────────────

    [Fact]
    public void BackupEngineOptions_BackupDir_RoundTrips()
    {
        using var dir = new TempDir();
        using var opts = new BackupEngineOptions(dir.Path);

        Assert.Equal(dir.Path, opts.BackupDir);

        string other = dir.Sub("elsewhere");
        opts.BackupDir = other;
        Assert.Equal(other, opts.BackupDir);
    }

    [Fact]
    public void BackupEngineOptions_EmptyPath_Throws()
        => Assert.Throws<ArgumentException>(() => new BackupEngineOptions(string.Empty));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackupEngineOptions_BoolProperties_RoundTrip(bool value)
    {
        using var dir = new TempDir();
        using var opts = new BackupEngineOptions(dir.Path);

        opts.ShareTableFiles = value;
        opts.Sync = value;
        opts.DestroyOldData = value;
        opts.BackupLogFiles = value;
        opts.ShareFilesWithChecksum = value;
        opts.CurrentTemperaturesOverrideManifest = value;

        Assert.Equal(value, opts.ShareTableFiles);
        Assert.Equal(value, opts.Sync);
        Assert.Equal(value, opts.DestroyOldData);
        Assert.Equal(value, opts.BackupLogFiles);
        Assert.Equal(value, opts.ShareFilesWithChecksum);
        Assert.Equal(value, opts.CurrentTemperaturesOverrideManifest);
    }

    [Fact]
    public void BackupEngineOptions_NumericProperties_RoundTrip()
    {
        using var dir = new TempDir();
        using var opts = new BackupEngineOptions(dir.Path);

        opts.BackupRateLimit = 1048576;
        opts.RestoreRateLimit = 2097152;
        opts.MaxBackgroundOperations = 2;
        opts.CallbackTriggerIntervalSize = 1024;
        opts.MaxValidBackupsToOpen = 5;
        opts.IoBufferSize = 65536;
        opts.SchemaVersion = 2;

        Assert.Equal(1048576UL, opts.BackupRateLimit);
        Assert.Equal(2097152UL, opts.RestoreRateLimit);
        Assert.Equal(2, opts.MaxBackgroundOperations);
        Assert.Equal(1024UL, opts.CallbackTriggerIntervalSize);
        Assert.Equal(5, opts.MaxValidBackupsToOpen);
        Assert.Equal(65536UL, opts.IoBufferSize);
        Assert.Equal(2, opts.SchemaVersion);
    }

    [Fact]
    public void BackupEngineOptions_RateLimiters_DoNotTakeOwnership()
    {
        using var dir = new TempDir();
        using var opts = new BackupEngineOptions(dir.Path);

        // RocksDb copies the shared_ptr, so the caller still owns these and the
        // using blocks below are correct.
        using var backupLimiter = new RateLimiter(1048576);
        using var restoreLimiter = new RateLimiter(2097152);

        opts.SetBackupRateLimiter(backupLimiter);
        opts.SetRestoreRateLimiter(restoreLimiter);

        Assert.False(backupLimiter.IsDisposed);
        Assert.False(restoreLimiter.IsDisposed);

        opts.SetBackupRateLimiter(null);
        opts.SetRestoreRateLimiter(null);
    }

    // ── Open with options ────────────────────────────────────────────────────

    [Fact]
    public void Open_WithOptions_TakesAndListsBackups()
    {
        using var db = new TempDb();
        using var backupDir = new TempDir();

        db.Db.Put("a", "1");

        using var engineOpts = new BackupEngineOptions(backupDir.Path) { ShareTableFiles = true };
        using var engine = BackupEngine.Open(engineOpts);

        engine.CreateNewBackup(db.Db, flushBeforeBackup: true);

        BackupInfo info = Assert.Single(engine.AsEnumerable());
        Assert.True(info.BackupId > 0);
        Assert.True(info.Size > 0);
    }

    [Fact]
    public void Open_WithNullOptions_Throws()
        => Assert.Throws<ArgumentNullException>(() => BackupEngine.Open((BackupEngineOptions)null!));

    [Fact]
    public void Open_WithDestroyOldData_ClearsPreviousBackups()
    {
        using var db = new TempDb();
        using var backupDir = new TempDir();

        db.Db.Put("a", "1");

        using (var firstOpts = new BackupEngineOptions(backupDir.Path))
        using (var first = BackupEngine.Open(firstOpts))
        {
            first.CreateNewBackup(db.Db, flushBeforeBackup: true);
            Assert.Single(first.AsEnumerable());
        }

        using var secondOpts = new BackupEngineOptions(backupDir.Path) { DestroyOldData = true };
        using var second = BackupEngine.Open(secondOpts);

        Assert.Empty(second.AsEnumerable());
    }

    // ── CreateBackupOptions ──────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateBackupOptions_BoolProperties_RoundTrip(bool value)
    {
        using var opts = new CreateBackupOptions();

        opts.FlushBeforeBackup = value;
        opts.AtomicFlush = value;
        opts.DecreaseBackgroundThreadCpuPriority = value;

        Assert.Equal(value, opts.FlushBeforeBackup);
        Assert.Equal(value, opts.AtomicFlush);
        Assert.Equal(value, opts.DecreaseBackgroundThreadCpuPriority);
    }

    [Theory]
    [InlineData(CpuPriority.Idle)]
    [InlineData(CpuPriority.Low)]
    [InlineData(CpuPriority.Normal)]
    [InlineData(CpuPriority.High)]
    public void CreateBackupOptions_BackgroundThreadCpuPriority_RoundTrips(CpuPriority priority)
    {
        using var opts = new CreateBackupOptions();

        opts.BackgroundThreadCpuPriority = priority;
        Assert.Equal(priority, opts.BackgroundThreadCpuPriority);
    }

    [Fact]
    public void CreateNewBackup_WithOptions_ReturnsBackupId()
    {
        using var db = new TempDb();
        using var backupDir = new TempDir();

        db.Db.Put("a", "1");

        using var engineDbOpts = NewDbOptions();
        using var engine = BackupEngine.Open(engineDbOpts, backupDir.Path);
        using var backupOpts = new CreateBackupOptions { FlushBeforeBackup = true };

        uint id = engine.CreateNewBackup(db.Db, backupOpts);

        Assert.Equal(1U, id);
        Assert.Equal(id, Assert.Single(engine.AsEnumerable()).BackupId);
    }

    [Fact]
    public void CreateNewBackup_WithAppMetadata_RoundTripsThroughBackupInfo()
    {
        using var db = new TempDb();
        using var backupDir = new TempDir();

        db.Db.Put("a", "1");

        using var engineDbOpts = NewDbOptions();
        using var engine = BackupEngine.Open(engineDbOpts, backupDir.Path);
        using var backupOpts = new CreateBackupOptions { FlushBeforeBackup = true };

        // Deliberately not valid UTF-8, since RocksDb treats this as opaque bytes.
        byte[] metadata = [0x00, 0x01, 0xFF, 0xFE, 0x42];

        uint id = engine.CreateNewBackup(db.Db, backupOpts, metadata);

        BackupInfo info = Assert.Single(engine.AsEnumerable());
        Assert.Equal(id, info.BackupId);
        Assert.Equal(metadata, info.AppMetadata);
    }

    [Fact]
    public void CreateNewBackup_WithoutMetadata_HasEmptyAppMetadata()
    {
        using var db = new TempDb();
        using var backupDir = new TempDir();

        db.Db.Put("a", "1");

        using var engineDbOpts = NewDbOptions();
        using var engine = BackupEngine.Open(engineDbOpts, backupDir.Path);
        engine.CreateNewBackup(db.Db, flushBeforeBackup: true);

        Assert.Empty(Assert.Single(engine.AsEnumerable()).AppMetadata);
    }

    [Fact]
    public void ExcludeFilesCallback_IsInvokedPerFile()
    {
        using var db = new TempDb();
        using var backupDir = new TempDir();

        db.Db.Put("a", "1");
        db.Db.Flush();
        db.Db.Put("b", "2");
        db.Db.Flush();

        var seen = new List<string>();

        // Excluding requires the checksum-based sharing scheme.
        using var engineOpts = new BackupEngineOptions(backupDir.Path)
        {
            ShareTableFiles = true,
            ShareFilesWithChecksum = true,
            SchemaVersion = 2, // Required by the exclude-files callback.
        };
        using var engine = BackupEngine.Open(engineOpts);

        using var backupOpts = new CreateBackupOptions { FlushBeforeBackup = true };
        backupOpts.SetExcludeFilesCallback(path =>
        {
            lock (seen)
            {
                seen.Add(path);
            }

            return false; // Exclude nothing, just observe.
        });

        engine.CreateNewBackup(db.Db, backupOpts);

        lock (seen)
        {
            Assert.NotEmpty(seen);
            Assert.All(seen, p => Assert.False(string.IsNullOrEmpty(p)));
        }
    }

    [Fact]
    public void ExcludeFilesCallback_Throwing_ExcludesNothingAndReports()
    {
        using var db = new TempDb();
        using var backupDir = new TempDir();

        db.Db.Put("a", "1");
        db.Db.Flush();

        var reported = new List<string>();
        void OnUnhandled(object? sender, CallbackExceptionEventArgs e)
        {
            lock (reported)
            {
                reported.Add(e.CallbackName);
            }
        }

        RocksDbCallbacks.UnhandledException += OnUnhandled;
        try
        {
            using var engineOpts = new BackupEngineOptions(backupDir.Path)
            {
                ShareTableFiles = true,
                ShareFilesWithChecksum = true,
                SchemaVersion = 2, // Required by the exclude-files callback.
            };
            using var engine = BackupEngine.Open(engineOpts);

            using var backupOpts = new CreateBackupOptions { FlushBeforeBackup = true };
            backupOpts.SetExcludeFilesCallback(_ => throw new InvalidOperationException("exclude boom"));

            // The backup still succeeds: a throwing callback excludes nothing.
            engine.CreateNewBackup(db.Db, backupOpts);

            Assert.Single(engine.AsEnumerable());
            lock (reported)
            {
                Assert.Contains("SetExcludeFilesCallback", reported);
            }
        }
        finally
        {
            RocksDbCallbacks.UnhandledException -= OnUnhandled;
        }
    }

    [Fact]
    public void ProgressCallback_IsInvoked()
    {
        using var db = new TempDb();
        using var backupDir = new TempDir();

        for (int i = 0; i < 200; i++)
        {
            db.Db.Put($"key{i:D5}", new string('v', 256));
        }

        db.Db.Flush();

        int progressCalls = 0;

        // A tiny trigger interval makes the callback fire during a small backup.
        using var engineOpts = new BackupEngineOptions(backupDir.Path) { CallbackTriggerIntervalSize = 1 };
        using var engine = BackupEngine.Open(engineOpts);

        using var backupOpts = new CreateBackupOptions { FlushBeforeBackup = true };
        backupOpts.SetProgressCallback(() => Interlocked.Increment(ref progressCalls));

        engine.CreateNewBackup(db.Db, backupOpts);

        Assert.True(Volatile.Read(ref progressCalls) > 0);
    }

    [Fact]
    public void Callbacks_CanBeRemovedAndReplaced()
    {
        using var opts = new CreateBackupOptions();

        // Each call frees the previous GCHandle itself, since the C API gives no
        // destructor for these callbacks.
        for (int i = 0; i < 2_000; i++)
        {
            opts.SetProgressCallback(() => { });
            opts.SetExcludeFilesCallback(_ => false);
        }

        opts.SetProgressCallback(null);
        opts.SetExcludeFilesCallback(null);
    }

    [Fact]
    public void Callbacks_AfterDispose_Throw()
    {
        var opts = new CreateBackupOptions();
        opts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => opts.SetProgressCallback(() => { }));
        Assert.Throws<ObjectDisposedException>(() => opts.SetExcludeFilesCallback(_ => false));
    }

    // ── StopBackup and VerifyBackup ──────────────────────────────────────────

    [Fact]
    public void VerifyBackup_OnHealthyBackup_Succeeds()
    {
        using var db = new TempDb();
        using var backupDir = new TempDir();

        db.Db.Put("a", "1");

        using var engineDbOpts = NewDbOptions();
        using var engine = BackupEngine.Open(engineDbOpts, backupDir.Path);
        engine.CreateNewBackup(db.Db, flushBeforeBackup: true);

        engine.VerifyBackup(Assert.Single(engine.AsEnumerable()).BackupId);
    }

    [Fact]
    public void VerifyBackup_UnknownId_Throws()
    {
        using var backupDir = new TempDir();

        using var engineDbOpts = NewDbOptions();
        using var engine = BackupEngine.Open(engineDbOpts, backupDir.Path);

        Assert.Throws<RocksDbException>(() => engine.VerifyBackup(999));
    }

    [Fact]
    public void StopBackup_WithNoBackupRunning_DoesNotThrow()
    {
        using var backupDir = new TempDir();

        using var engineDbOpts = NewDbOptions();
        using var engine = BackupEngine.Open(engineDbOpts, backupDir.Path);

        engine.StopBackup();
    }

    // ── RestoreOptions ───────────────────────────────────────────────────────

    [Fact]
    public void RestoreOptions_KeepLogFiles_RoundTrips()
    {
        using var opts = new RestoreOptions();

        opts.KeepLogFiles = true;
        Assert.True(opts.KeepLogFiles);

        opts.KeepLogFiles = false;
        Assert.False(opts.KeepLogFiles);
    }

    [Theory]
    [InlineData(RestoreMode.KeepLatestDbSessionIdFiles)]
    [InlineData(RestoreMode.VerifyChecksum)]
    [InlineData(RestoreMode.PurgeAllFiles)]
    public void RestoreOptions_Mode_RoundTrips(RestoreMode mode)
    {
        using var opts = new RestoreOptions();

        opts.Mode = mode;
        Assert.Equal(mode, opts.Mode);
    }

    [Fact]
    public void RestoreOptions_Mode_DefaultsToPurgeAllFiles()
    {
        using var opts = new RestoreOptions();

        Assert.Equal(RestoreMode.PurgeAllFiles, opts.Mode);
    }

    [Theory]
    [InlineData(RestoreMode.PurgeAllFiles)]
    [InlineData(RestoreMode.VerifyChecksum)]
    [InlineData(RestoreMode.KeepLatestDbSessionIdFiles)]
    public void RestoreDbFromLatestBackup_WithMode_RestoresTheData(RestoreMode mode)
    {
        using var backupDir = new TempDir();
        using var restoreDir = new TempDir();

        using (var db = new TempDb())
        {
            db.Db.Put("a", "1");
            db.Db.Put("b", "2");

            using var backupDbOpts = NewDbOptions();
            using var backupEngine = BackupEngine.Open(backupDbOpts, backupDir.Path);
            backupEngine.CreateNewBackup(db.Db, flushBeforeBackup: true);
        }

        using var engineDbOpts = NewDbOptions();
        using var engine = BackupEngine.Open(engineDbOpts, backupDir.Path);
        using var restoreOpts = new RestoreOptions { Mode = mode };
        engine.RestoreDbFromLatestBackup(restoreDir.Path, restoreDir.Path, restoreOpts);

        using var restoredOpts = NewDbOptions();
        using var restored = RocksDb.Open(restoredOpts, restoreDir.Path);
        Assert.Equal("1", restored.GetString("a"));
        Assert.Equal("2", restored.GetString("b"));
    }

    [Fact]
    public void RestoreDbFromBackup_ByIdRestoresThatBackup()
    {
        using var backupDir = new TempDir();
        using var restoreDir = new TempDir();

        uint firstBackupId;

        using (var db = new TempDb())
        {
            using var backupDbOpts = NewDbOptions();
            using var backupEngine = BackupEngine.Open(backupDbOpts, backupDir.Path);

            db.Db.Put("a", "first");
            backupEngine.CreateNewBackup(db.Db, flushBeforeBackup: true);
            firstBackupId = backupEngine.AsEnumerable().Single().BackupId;

            db.Db.Put("a", "second");
            backupEngine.CreateNewBackup(db.Db, flushBeforeBackup: true);
        }

        using var engineDbOpts = NewDbOptions();
        using var engine = BackupEngine.Open(engineDbOpts, backupDir.Path);
        engine.RestoreDbFromBackup(restoreDir.Path, restoreDir.Path, firstBackupId);

        using var restoredOpts = NewDbOptions();
        using var restored = RocksDb.Open(restoredOpts, restoreDir.Path);
        Assert.Equal("first", restored.GetString("a"));
    }

    [Fact]
    public void RestoreDbFromLatestBackup_WithNullOptions_Throws()
    {
        using var backupDir = new TempDir();

        using var engineDbOpts = NewDbOptions();
        using var engine = BackupEngine.Open(engineDbOpts, backupDir.Path);

        Assert.Throws<ArgumentNullException>(
            () => engine.RestoreDbFromLatestBackup(backupDir.Path, backupDir.Path, null!));
    }
}
