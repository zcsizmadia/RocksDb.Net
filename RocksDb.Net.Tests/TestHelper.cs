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
/// Opens a RocksDb for a test and disposes everything it created.
/// </summary>
/// <remarks>
/// <para>
/// In memory by default, through RocksDb's own in-memory environment. Almost
/// no test cares where its bytes land, and the ones that do are the minority:
/// this used to create and delete a real directory per test, and on Windows
/// that dominated the run. Measured over 100 open, write, flush and close
/// cycles: 1926 ms against a real directory, 268 ms in memory.
/// </para>
/// <para>
/// Use <see cref="OnDisk"/> when the test inspects real files, hands the
/// options to something that writes outside the database directory such as a
/// backup engine, or is about the file system rather than the database.
/// </para>
/// </remarks>
public sealed class TempDb : IDisposable
{
    private readonly Env? _env;

    /// <summary>The real directory, or <c>null</c> for an in-memory database.</summary>
    public TempDir? Dir { get; }

    public RocksDb Db { get; }

    public DbOptions Options { get; }

    /// <summary>
    /// Where the database lives: a real path on disk, or a path inside this
    /// instance's own in-memory environment.
    /// </summary>
    public string Path { get; }

    public TempDb(Action<DbOptions>? configure = null)
        : this(onDisk: false, configure)
    {
    }

    /// <summary>Opens the database in a real temporary directory.</summary>
    public static TempDb OnDisk(Action<DbOptions>? configure = null) => new(onDisk: true, configure);

    private TempDb(bool onDisk, Action<DbOptions>? configure)
    {
        Options = new DbOptions { CreateIfMissing = true };

        if (onDisk)
        {
            Dir = new TempDir();
            Path = Dir.Path;
        }
        else
        {
            // One environment per database rather than one shared by the suite.
            // A shared handle is freed when the last holder lets go, so an
            // environment reused by a second database after the first closed is
            // already disposed. See the ownership guide.
            _env = Env.CreateInMemory();
            Options.Env = _env;
            Path = "/db";
        }

        configure?.Invoke(Options);
        Db = RocksDb.Open(Options, Path);
    }

    public void Dispose()
    {
        // The database owns the options and disposes them, which releases the
        // hold on the environment and so disposes that too. The calls below are
        // idempotent and are here so this reads as owning what it created.
        Db.Dispose();
        Options.Dispose();
        _env?.Dispose();
        Dir?.Dispose();
    }
}

/// <summary>
/// Setup shared by tests that need a particular on-disk shape, rather than just
/// an open database.
/// </summary>
public static class TestDb
{
    /// <summary>
    /// Where an in-memory database lives.
    /// </summary>
    /// <remarks>
    /// A plain absolute path rather than the real temporary directory the test
    /// may also have. RocksDb's in-memory environment does not implement
    /// <c>GetAbsolutePath</c>, so handing it a Windows path such as
    /// <c>C:\Users\...</c> fails the open with "Not implemented:
    /// GetAbsolutePath". Each database gets its own environment, so one fixed
    /// path cannot collide with another.
    /// </remarks>
    public const string InMemoryPath = "/db";

    /// <summary>
    /// Opens a database in memory, for a test that builds its own options
    /// rather than using <see cref="TempDb"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The environment is attached to <paramref name="options"/>, so the
    /// database owns it: closing the database disposes the options it took
    /// ownership of, that lets go of the hold, and the environment goes with
    /// it. Nothing for the caller to dispose.
    /// </para>
    /// <para>
    /// One environment per database, not one shared by the suite. A shared
    /// handle is freed when the last holder lets go, so an environment reused
    /// after the first database closed is already disposed. See the ownership
    /// guide.
    /// </para>
    /// </remarks>
    public static RocksDb OpenInMemory(DbOptions options)
    {
        string path = InMemory(options);
        return RocksDb.Open(options, path);
    }

    /// <summary>
    /// Attaches an in-memory environment to <paramref name="options"/> and
    /// returns the path to open, for a test that has to keep its own
    /// <c>RocksDb.Open</c> call rather than calling
    /// <see cref="OpenInMemory(DbOptions)"/>.
    /// </summary>
    /// <remarks>
    /// The documentation tests are the reason this exists. They keep the code
    /// identical to what the README and the guides show, apart from the path, so
    /// the open itself has to stay as a reader sees it. The environment is owned
    /// exactly as in <see cref="OpenInMemory(DbOptions)"/>.
    /// </remarks>
    public static string InMemory(DbOptions options)
    {
        options.Env = Env.CreateInMemory();
        return InMemoryPath;
    }

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
    /// <para>
    /// Returns rather than throws, so the caller can assert on it and say in its
    /// own words what was being waited for.
    /// </para>
    /// <para>
    /// Every assertion on an event listener callback needs this. RocksDb delivers
    /// them on its own background threads, so a flush or a compaction returning
    /// does not mean the callback for it has been made. A real file system hid
    /// that by being slow: in memory a flush finishes in microseconds and the
    /// assertion beats the callback, which is how a macOS job failed on
    /// <c>EventListener_FlushJobInfo_Properties</c> while every other job passed.
    /// </para>
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

/// <summary>
/// Round-trips boolean options one at a time, so a setter that writes the wrong
/// field is visible.
/// </summary>
/// <remarks>
/// The tests that use this used to set every property to the same value and
/// then assert every property held it. If a setter wrote a neighbouring field
/// instead of its own, all the assertions still passed, because everything had
/// been set to the same value anyway. Setting one property at a time and
/// checking that the others did not move is what catches that.
/// </remarks>
public static class BoolProperty
{
    /// <summary>
    /// Asserts each property round-trips both ways and moves nothing else.
    /// </summary>
    public static void AssertRoundTripsIndependently<T>(
        T target, params (string Name, Action<T, bool> Set, Func<T, bool> Get)[] properties)
    {
        Assert.NotEmpty(properties);

        foreach ((string name, Action<T, bool> set, Func<T, bool> get) in properties)
        {
            foreach ((_, Action<T, bool> reset, _) in properties)
            {
                reset(target, false);
            }

            set(target, true);

            Assert.True(get(target), $"{name} did not read back as true");

            foreach ((string otherName, _, Func<T, bool> otherGet) in properties)
            {
                if (otherName == name)
                {
                    continue;
                }

                Assert.False(otherGet(target), $"setting {name} also set {otherName}");
            }

            set(target, false);

            Assert.False(get(target), $"{name} did not read back as false");
        }
    }
}
