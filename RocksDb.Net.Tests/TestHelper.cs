using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Creates a temporary directory for each test and cleans it up on dispose.
/// </summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rocksdbnet_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Sub(string name)
    {
        var p = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}

/// <summary>
/// Opens a RocksDb in a temp directory and disposes both on cleanup.
/// </summary>
public sealed class TempDb : IDisposable
{
    public TempDir Dir { get; }
    public RocksDb Db { get; }
    public DbOptions Options { get; }
    public string Path => Dir.Path;

    public TempDb(Action<DbOptions>? configure = null)
    {
        Dir = new TempDir();
        Options = new DbOptions { CreateIfMissing = true };
        configure?.Invoke(Options);
        Db = RocksDb.Open(Options, Dir.Path);
    }

    public void Dispose()
    {
        Db.Dispose();
        Options.Dispose();
        Dir.Dispose();
    }
}

/// <summary>
/// Setup shared by tests that need a particular on-disk shape, rather than just
/// an open database.
/// </summary>
public static class TestDb
{
    /// <summary>
    /// Enables blob files with no size threshold, so every value goes to a blob
    /// file rather than into the SST.
    /// </summary>
    public static DbOptions EnableBlobs(this DbOptions options)
    {
        options.EnableBlobFiles = true;
        options.MinBlobSize = 0;
        return options;
    }

    /// <summary>
    /// Writes two SST files whose key ranges overlap, and returns their names.
    /// </summary>
    /// <remarks>
    /// Overlap is the point. Two files with disjoint ranges get trivially moved
    /// rather than merged, and a trivial move leaves almost every compaction
    /// statistic at zero, so tests built on disjoint files assert nothing.
    /// </remarks>
    public static string[] WriteOverlappingSstFiles(this RocksDb db)
    {
        db.Put("a", "1");
        db.Put("b", "2");
        db.Flush();
        db.Put("a", "1-updated");
        db.Put("b", "2-updated");
        db.Flush();

        return db.LiveFileNames();
    }

    /// <summary>
    /// Writes <paramref name="count"/> SST files, one per flush, and returns
    /// their names.
    /// </summary>
    public static string[] WriteSstFiles(this RocksDb db, int count)
    {
        for (int i = 0; i < count; i++)
        {
            db.Put($"key{i:D5}", $"value{i}");
            db.Flush();
        }

        return db.LiveFileNames();
    }

    /// <summary>Names of the currently live SST files.</summary>
    public static string[] LiveFileNames(this RocksDb db)
    {
        return [.. db.GetLiveFiles().Select(f => f.Name)];
    }

    /// <summary>
    /// Writes records and closes the database without flushing, leaving them in
    /// the write-ahead log for the next open to replay.
    /// </summary>
    /// <remarks>
    /// The only way to exercise a <see cref="WalFilter"/>, which runs during
    /// recovery and nowhere else.
    /// </remarks>
    public static void WriteRecordsLeftInTheWal(string path, params (string Key, string Value)[] records)
    {
        using var opts = new DbOptions { CreateIfMissing = true, AvoidFlushDuringShutdown = true };
        using var db = RocksDb.Open(opts, path);

        foreach ((string key, string value) in records)
        {
            db.Put(key, value);
        }
    }
}

/// <summary>
/// Collects every event-listener callback so a test can assert on what RocksDb
/// reported.
/// </summary>
/// <remarks>
/// Overrides all ten events on purpose. A listener that overrides only some is
/// itself worth testing, since RocksDb invokes all ten callbacks without a null
/// check, but that belongs in a dedicated test rather than in shared setup.
/// </remarks>
public sealed class RecordingListener : EventListener
{
    private readonly object _gate = new();
    private readonly List<FlushJobInfo> _flushBegin = [];
    private readonly List<FlushJobInfo> _flushCompleted = [];
    private readonly List<CompactionJobInfo> _compactionBegin = [];
    private readonly List<CompactionJobInfo> _compactionCompleted = [];
    private readonly List<SubCompactionJobInfo> _subCompactionCompleted = [];
    private readonly List<ExternalFileIngestionInfo> _ingested = [];
    private readonly List<BackgroundErrorInfo> _backgroundErrors = [];
    private readonly List<WriteStallInfo> _stalls = [];
    private readonly List<MemTableInfo> _memTablesSealed = [];

    public override void OnFlushBegin(FlushJobInfo info) => Add(_flushBegin, info);

    public override void OnFlushCompleted(FlushJobInfo info) => Add(_flushCompleted, info);

    public override void OnCompactionBegin(CompactionJobInfo info) => Add(_compactionBegin, info);

    public override void OnCompactionCompleted(CompactionJobInfo info) => Add(_compactionCompleted, info);

    public override void OnSubCompactionCompleted(SubCompactionJobInfo info) => Add(_subCompactionCompleted, info);

    public override void OnExternalFileIngested(ExternalFileIngestionInfo info) => Add(_ingested, info);

    public override void OnBackgroundError(BackgroundErrorInfo info) => Add(_backgroundErrors, info);

    public override void OnStallConditionsChanged(WriteStallInfo info) => Add(_stalls, info);

    public override void OnMemTableSealed(MemTableInfo info) => Add(_memTablesSealed, info);

    public IReadOnlyList<FlushJobInfo> FlushBegin => Snapshot(_flushBegin);

    public IReadOnlyList<FlushJobInfo> FlushCompleted => Snapshot(_flushCompleted);

    public IReadOnlyList<CompactionJobInfo> CompactionBegin => Snapshot(_compactionBegin);

    public IReadOnlyList<CompactionJobInfo> CompactionCompleted => Snapshot(_compactionCompleted);

    public IReadOnlyList<SubCompactionJobInfo> SubCompactionCompleted => Snapshot(_subCompactionCompleted);

    public IReadOnlyList<ExternalFileIngestionInfo> Ingested => Snapshot(_ingested);

    public IReadOnlyList<BackgroundErrorInfo> BackgroundErrors => Snapshot(_backgroundErrors);

    public IReadOnlyList<WriteStallInfo> Stalls => Snapshot(_stalls);

    public IReadOnlyList<MemTableInfo> MemTablesSealed => Snapshot(_memTablesSealed);

    // Callbacks arrive on RocksDb background threads, so both sides lock.
    private void Add<T>(List<T> target, T item)
    {
        lock (_gate)
        {
            target.Add(item);
        }
    }

    private IReadOnlyList<T> Snapshot<T>(List<T> source)
    {
        lock (_gate)
        {
            return [.. source];
        }
    }
}

/// <summary>
/// Captures exceptions reported through
/// <see cref="RocksDbCallbacks.UnhandledException"/> for the lifetime of a test.
/// </summary>
/// <remarks>
/// <para>
/// The event is process-wide and xUnit runs test classes in parallel, so an
/// unfiltered recorder picks up exceptions that unrelated tests provoke on
/// purpose. Any test asserting that <b>no</b> exception was reported must pass
/// the instance it cares about, or it will fail intermittently. Filtering by
/// callback name is not enough, because several test classes throw from
/// callbacks of the same name.
/// </para>
/// <para>
/// Tests asserting that an exception <b>was</b> reported can leave the filter
/// off, since those provoke the throw themselves and match on the callback name.
/// </para>
/// </remarks>
public sealed class CallbackExceptionRecorder : IDisposable
{
    private readonly object _gate = new();
    private readonly List<CallbackExceptionEventArgs> _reported = [];
    private readonly object? _source;

    /// <summary>Records every reported exception, whatever its source.</summary>
    public CallbackExceptionRecorder()
        => RocksDbCallbacks.UnhandledException += OnUnhandled;

    /// <summary>
    /// Records only exceptions raised by callbacks on <paramref name="source"/>,
    /// ignoring those other tests provoke in parallel.
    /// </summary>
    /// <param name="source">
    /// The wrapper whose callbacks are of interest, for example a
    /// <see cref="CompactionFilter"/> or an <see cref="EventListener"/>. This is
    /// the sender the event reports.
    /// </param>
    public CallbackExceptionRecorder(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        RocksDbCallbacks.UnhandledException += OnUnhandled;
    }

    public IReadOnlyList<CallbackExceptionEventArgs> Reported
    {
        get
        {
            lock (_gate)
            {
                return [.. _reported];
            }
        }
    }

    public bool Contains(string callbackName)
        => Reported.Any(r => r.CallbackName == callbackName);

    private void OnUnhandled(object? sender, CallbackExceptionEventArgs e)
    {
        if (_source is not null && !ReferenceEquals(sender, _source))
        {
            return;
        }

        lock (_gate)
        {
            _reported.Add(e);
        }
    }

    public void Dispose()
        => RocksDbCallbacks.UnhandledException -= OnUnhandled;
}

/// <summary>
/// Polls for a condition RocksDb reaches on a background thread.
/// </summary>
/// <remarks>
/// Flushes and compactions are asynchronous, so a test that checks immediately
/// after asking for one is really testing how fast the machine is. Several test
/// classes had grown their own copy of this.
/// </remarks>
public static class Wait
{
    /// <summary>
    /// Polls until the condition holds, returning whether it ever did.
    /// </summary>
    /// <remarks>
    /// Returns rather than throws, so the caller can assert on it and say in its
    /// own words what was being waited for.
    /// </remarks>
    public static bool Until(Func<bool> condition, TimeSpan? timeout = null)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(30);

        while (elapsed.Elapsed < limit)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return condition();
    }
}
