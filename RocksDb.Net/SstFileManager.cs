namespace RocksDbNet;

/// <summary>
/// Governs how much disk space a database may use and how fast it may delete
/// files. Maps to <c>rocksdb_sst_file_manager_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two problems this solves. A database with no space limit fills the disk and
/// then fails in whatever way the filesystem chooses; with a limit, writes fail
/// with an error the application can handle while the disk still has room.
/// And deleting a large amount of data at once can saturate the disk, starving
/// everything else on it, which the delete rate limits.
/// </para>
/// <para>
/// Attach it with <see cref="DbOptions.SstFileManager"/>. RocksDb takes a
/// shared reference, so this object may be disposed once assigned, and the same
/// instance may be shared between databases to give them a common budget.
/// </para>
/// </remarks>
public sealed class SstFileManager : RocksDbHandle
{
    private SstFileManager(nint handle)
        : base(handle)
    {
    }

    /// <summary>Creates a manager using <paramref name="env"/> for file operations.</summary>
    /// <param name="env">
    /// The environment whose filesystem the manager acts on. Pass
    /// <see langword="null"/> for the default.
    /// </param>
    public static SstFileManager Create(Env? env = null)
    {
        using Env? owned = env is null ? Env.Create() : null;

        return new SstFileManager(NativeMethods.rocksdb_sst_file_manager_create((env ?? owned!).Handle));
    }

    /// <summary>Total size of the files the manager is tracking, in bytes.</summary>
    public ulong TotalSize => NativeMethods.rocksdb_sst_file_manager_get_total_size(Handle);

    /// <summary>
    /// Total size of files deleted but not yet removed from disk, in bytes.
    /// </summary>
    /// <remarks>
    /// Non-zero when the delete rate is throttling removal. This is space the
    /// database has finished with but the disk has not yet reclaimed.
    /// </remarks>
    public ulong TotalTrashSize => NativeMethods.rocksdb_sst_file_manager_get_total_trash_size(Handle);

    /// <summary>
    /// How fast files may be deleted, in bytes per second. Zero or negative
    /// means no limit.
    /// </summary>
    /// <remarks>
    /// Deleting a large file is cheap for the database and expensive for the
    /// disk. Rate-limiting it stops a bulk delete from starving reads.
    /// </remarks>
    public long DeleteRateBytesPerSecond
    {
        get => NativeMethods.rocksdb_sst_file_manager_get_delete_rate_bytes_per_second(Handle);
        set => NativeMethods.rocksdb_sst_file_manager_set_delete_rate_bytes_per_second(Handle, value);
    }

    /// <summary>
    /// The largest fraction of the database that pending deletions may occupy
    /// before the rate limit is abandoned and files are deleted immediately.
    /// </summary>
    /// <remarks>
    /// The escape hatch for the delete rate. Without it, a slow rate combined
    /// with a large delete could let trash grow without bound.
    /// </remarks>
    public double MaxTrashDbRatio
    {
        get => NativeMethods.rocksdb_sst_file_manager_get_max_trash_db_ratio(Handle);
        set => NativeMethods.rocksdb_sst_file_manager_set_max_trash_db_ratio(Handle, value);
    }

    /// <summary>
    /// Caps the total space the database may use, in bytes.
    /// </summary>
    /// <remarks>
    /// Once reached, writes fail rather than consuming more space. Zero removes
    /// the cap.
    /// </remarks>
    public void SetMaxAllowedSpaceUsage(ulong bytes)
        => NativeMethods.rocksdb_sst_file_manager_set_max_allowed_space_usage(Handle, bytes);

    /// <summary>
    /// Space reserved above the cap for compaction output, in bytes.
    /// </summary>
    /// <remarks>
    /// A compaction writes its output before deleting its inputs, so it
    /// temporarily needs more room than the data occupies. Without this reserve
    /// a database at its limit could not compact, and so could never shrink.
    /// </remarks>
    public void SetCompactionBufferSize(ulong bytes)
        => NativeMethods.rocksdb_sst_file_manager_set_compaction_buffer_size(Handle, bytes);

    /// <summary>Whether the space cap has been reached.</summary>
    /// <param name="includingCompactions">
    /// When true, also counts the space in-flight compactions are expected to
    /// need, so it reports the limit as reached sooner.
    /// </param>
    public bool IsMaxAllowedSpaceReached(bool includingCompactions = false)
        => includingCompactions
            ? NativeMethods.rocksdb_sst_file_manager_is_max_allowed_space_reached_including_compactions(Handle) != 0
            : NativeMethods.rocksdb_sst_file_manager_is_max_allowed_space_reached(Handle) != 0;

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_sst_file_manager_destroy(Handle);
    }
}
