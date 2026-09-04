using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// The options a database was last opened with, read back from the
/// <c>OPTIONS-</c> file RocksDb writes into its directory.
/// </summary>
/// <remarks>
/// <para>
/// For reopening a database you did not configure. Without this the choice is
/// to guess, or to hard-code options that then have to be kept in step with
/// whoever created the database — and getting the comparator wrong is not a
/// configuration mistake but a corrupt read. It is also the honest way to write
/// a diagnostic or repair tool against a database you have been handed.
/// </para>
/// <para>
/// Everything here is freed as a unit. RocksDb allocates the database options,
/// the column family names and the per-family options in one call and takes
/// them back in one call, so the <see cref="DbOptions"/> this exposes are not
/// yours to dispose: disposing this releases all of them, and using one
/// afterwards throws <see cref="ObjectDisposedException"/> rather than reading
/// freed memory.
/// </para>
/// <para>
/// Partial parity with what other bindings offer, and worth being explicit
/// about: RocksDb's C API exposes only "load the latest options from a database
/// directory". There is no equivalent of loading a named options file, or of
/// asking which options file is the latest, so neither is here.
/// </para>
/// </remarks>
public sealed class LoadedOptions : IDisposable
{
    private readonly nint _dbOptionsHandle;
    private readonly unsafe byte** _names;
    private readonly unsafe nint* _columnFamilyOptions;
    private readonly nuint _count;
    private readonly DbOptions[] _perColumnFamily;

    private bool _disposed;

    private unsafe LoadedOptions(
        nint dbOptionsHandle,
        byte** names,
        nint* columnFamilyOptions,
        nuint count,
        DbOptions databaseOptions,
        IReadOnlyList<string> columnFamilyNames,
        DbOptions[] perColumnFamily)
    {
        _dbOptionsHandle = dbOptionsHandle;
        _names = names;
        _columnFamilyOptions = columnFamilyOptions;
        _count = count;
        _perColumnFamily = perColumnFamily;

        DatabaseOptions = databaseOptions;
        ColumnFamilyNames = columnFamilyNames;
    }

    /// <summary>The database-wide options the database was last opened with.</summary>
    /// <remarks>
    /// <b>Database-wide only.</b> RocksDb builds this from the file's DBOptions
    /// combined with a default set of column family options, so anything
    /// column-family scoped — the write buffer size, compression, the comparator
    /// — reads back as its default here and is not what the database was using.
    /// Those live in <see cref="ColumnFamilyOptions"/>, one set per family.
    /// </remarks>
    public DbOptions DatabaseOptions { get; }

    /// <summary>The column families the database was last opened with, in order.</summary>
    public IReadOnlyList<string> ColumnFamilyNames { get; }

    /// <summary>The options for the column family at <paramref name="index"/>.</summary>
    /// <remarks>
    /// Indexed the same as <see cref="ColumnFamilyNames"/>, and owned by this
    /// object like <see cref="DatabaseOptions"/>. This is where every
    /// column-family setting is: the write buffer size, compression, the
    /// comparator, the merge operator by name. Reading them from
    /// <see cref="DatabaseOptions"/> gives defaults instead.
    /// </remarks>
    public DbOptions ColumnFamilyOptions(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _perColumnFamily.Length);

        return _perColumnFamily[index];
    }

    /// <summary>
    /// Reads the latest <c>OPTIONS-</c> file from the database directory at
    /// <paramref name="dbPath"/>.
    /// </summary>
    /// <param name="dbPath">A database directory, which must already exist.</param>
    /// <param name="env">
    /// The environment to read the file through. The process default when null.
    /// </param>
    /// <param name="cache">
    /// The block cache the loaded options should use. One is created when null.
    /// RocksDb needs it at load time rather than after, because the table
    /// factory it reconstructs from the file holds it.
    /// </param>
    /// <param name="ignoreUnknownOptions">
    /// Whether an option RocksDb no longer recognises is skipped rather than
    /// failing the load. Useful when reading a database written by a newer
    /// version than this one.
    /// </param>
    /// <exception cref="RocksDbException">
    /// The directory has no options file, or the file cannot be parsed.
    /// </exception>
    public static unsafe LoadedOptions LoadLatest(
        string dbPath,
        Env? env = null,
        Cache? cache = null,
        bool ignoreUnknownOptions = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(dbPath);

        // Both are dereferenced by RocksDb without a null check —
        // `config_opts.env = env->rep` and `&cache->rep` in db/c.cc — so a null
        // for either is an access violation rather than a default. Supplied
        // here so that reading an options file does not require the caller to
        // construct two objects it has no opinion about.
        //
        // Safe to let go of afterwards. The default env is the process-wide one
        // and its destroy is a no-op, and RocksDb copies the cache's shared
        // pointer, so the loaded options keep it alive on their own.
        using Env? ownedEnv = env is null ? Env.Create() : null;
        using Cache? ownedCache = cache is null ? Cache.CreateLru(8 * 1024 * 1024) : null;

        Env effectiveEnv = env ?? ownedEnv!;
        Cache effectiveCache = cache ?? ownedCache!;

        nint dbOptions = default;
        nuint count = 0;
        byte** names = null;
        nint* cfOptions = null;
        nint err = default;

        NativeMethods.rocksdb_load_latest_options(
            dbPath,
            effectiveEnv.Handle,
            (byte)(ignoreUnknownOptions ? 1 : 0),
            effectiveCache.Handle,
            &dbOptions,
            &count,
            &names,
            &cfOptions,
            ref err);

        NativeMethods.ThrowOnError(err);

        // Read the names before anything can throw, so a failure below still
        // has the pointers it needs to free.
        int n = checked((int)count);
        var columnFamilyNames = new string[n];
        var perColumnFamily = new DbOptions[n];

        for (int i = 0; i < n; i++)
        {
            columnFamilyNames[i] = Marshal.PtrToStringUTF8((nint)names[i]) ?? string.Empty;
            perColumnFamily[i] = DbOptions.Borrowed(cfOptions[i]);
        }

        return new LoadedOptions(
            dbOptions,
            names,
            cfOptions,
            count,
            DbOptions.Borrowed(dbOptions),
            columnFamilyNames,
            perColumnFamily);
    }

    /// <summary>Releases the options, the names and the per-family options together.</summary>
    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Marks the wrappers unusable without freeing anything: each was
        // created with ownership transferred away, so its DisposeHandle is
        // skipped and only the disposed flag is set. That turns a use after
        // this point into an ObjectDisposedException instead of a read of
        // freed memory.
        DatabaseOptions.Dispose();

        foreach (DbOptions options in _perColumnFamily)
        {
            options.Dispose();
        }

        NativeMethods.rocksdb_load_latest_options_destroy(
            _dbOptionsHandle, _names, _columnFamilyOptions, _count);
    }
}
