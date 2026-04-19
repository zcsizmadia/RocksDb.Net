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
