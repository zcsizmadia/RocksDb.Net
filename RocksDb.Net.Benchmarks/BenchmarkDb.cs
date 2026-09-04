using RocksDbNet;

namespace RocksDbNet.Benchmarks;

/// <summary>
/// A database for a benchmark to measure against, and the keys to hit it with.
/// </summary>
/// <remarks>
/// <para>
/// In memory, deliberately. These benchmarks exist to measure what this
/// wrapper costs — a copy that could have been avoided, a native transition
/// paid per call — and a real file system buries that under page-cache
/// behaviour and device variance that has nothing to do with the code under
/// test. The trade is that no number here says anything about how fast RocksDb
/// is against a disk, which is not what any of these questions are about.
/// </para>
/// <para>
/// Everything is written and flushed during setup, so reads come from SST
/// files through the block cache rather than from the memtable. That is the
/// path where <see cref="RocksDb.GetPinned"/> can avoid a copy natively, so
/// measuring against the memtable would flatter the copying calls.
/// </para>
/// </remarks>
internal sealed class BenchmarkDb : IDisposable
{
    private readonly Env _env;

    public RocksDb Db { get; }

    public byte[][] Keys { get; }

    private BenchmarkDb(RocksDb db, Env env, byte[][] keys)
    {
        Db = db;
        _env = env;
        Keys = keys;
    }

    /// <summary>Builds a database of <paramref name="count"/> keys.</summary>
    /// <param name="valueSize">Bytes per value.</param>
    /// <param name="configure">
    /// Applied before the open, for a benchmark that needs a comparator, a
    /// listener or other options of its own.
    /// </param>
    public static BenchmarkDb Create(
        int count, int valueSize, Action<DbOptions>? configure = null)
    {
        var options = new DbOptions { CreateIfMissing = true };

        // Owned by the options, and so by the database: nothing to dispose
        // separately, and each database gets its own so they cannot collide on
        // the fixed path below.
        Env env = Env.CreateInMemory();
        options.Env = env;

        configure?.Invoke(options);

        // RocksDb's in-memory environment does not implement GetAbsolutePath,
        // so it rejects a real path outright. A fixed POSIX-style one works.
        RocksDb db = RocksDb.Open(options, "/bench");

        var keys = new byte[count][];
        byte[] value = new byte[valueSize];
        Random.Shared.NextBytes(value);

        for (int i = 0; i < count; i++)
        {
            keys[i] = KeyFor(i);
            db.Put(keys[i], value);
        }

        db.Flush();
        db.CompactRange();

        return new BenchmarkDb(db, env, keys);
    }

    /// <summary>
    /// Fixed-width and zero-padded, so ordering is the same bytewise as it is
    /// numerically and a custom comparator has the same work to do as the
    /// built-in one.
    /// </summary>
    public static byte[] KeyFor(int i) => System.Text.Encoding.UTF8.GetBytes($"key{i:D9}");

    public void Dispose()
    {
        Db.Dispose();
        _env.Dispose();
    }
}
