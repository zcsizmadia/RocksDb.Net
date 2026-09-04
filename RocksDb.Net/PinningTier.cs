namespace RocksDbNet;

/// <summary>
/// Which index and filter blocks are kept pinned in the block cache, mapped
/// from <c>rocksdb::PinningTier</c> in <c>table.h</c>.
/// </summary>
/// <remarks>
/// <para>
/// A pinned block cannot be evicted, so it is always a cache hit and never
/// needs re-reading. The cost is that it occupies capacity permanently, so
/// pinning more leaves less room for data blocks.
/// </para>
/// <para>
/// These are distinct from the older boolean flags such as
/// <c>pin_top_level_index_and_filter</c>. A tier chooses <em>which</em> blocks
/// are pinned rather than switching one behaviour on and off, which is why it
/// is an enum and not a flag.
/// </para>
/// </remarks>
public enum PinningTier
{
    /// <summary>
    /// Defer to the older boolean pinning options. This is the default, kept
    /// for compatibility.
    /// </summary>
    Fallback = 0,

    /// <summary>Pin nothing.</summary>
    None = 1,

    /// <summary>
    /// Pin blocks from tables that could have come from a memtable flush, which
    /// in practice means the small, recent files in level 0.
    /// </summary>
    FlushedAndSimilar = 2,

    /// <summary>Pin blocks from every table.</summary>
    All = 3,
}

/// <summary>
/// One directory a database may store data in, with a size target. Maps to
/// <c>rocksdb_dbpath_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used with <see cref="DbOptions.SetDbPaths(System.Collections.Generic.IReadOnlyList{DbPath})"/>
/// to spread a database across several directories. The usual reason is mixed
/// storage: give a fast device a modest target so the newest, hottest levels
/// live there, and let the rest overflow onto slower, larger media.
/// </para>
/// <para>
/// The target is advisory. RocksDb fills each path up to its target in order
/// and then moves on, so the last path should be the one with room to spare.
/// </para>
/// </remarks>
public sealed class DbPath : RocksDbHandle
{
    /// <summary>Creates a path entry.</summary>
    /// <param name="path">Directory to store data in.</param>
    /// <param name="targetSizeBytes">
    /// How much data to place here before moving to the next path. Zero means
    /// no limit, which only makes sense for the last entry.
    /// </param>
    /// <remarks>
    /// The validation is in the body rather than folded into a base-constructor
    /// argument. Throwing from there would leave an allocated object whose base
    /// constructor never ran, with every inherited field at its default, and the
    /// finalizer still runs on that.
    /// </remarks>
    public DbPath(string path, ulong targetSizeBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Handle = NativeMethods.rocksdb_dbpath_create(path, targetSizeBytes);
        Path = path;
        TargetSizeBytes = targetSizeBytes;
    }

    /// <summary>The directory this entry names.</summary>
    public string Path { get; }

    /// <summary>How much data to place here before using the next path.</summary>
    public ulong TargetSizeBytes { get; }


    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_dbpath_destroy(Handle);
    }
}
