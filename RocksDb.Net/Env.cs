using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RocksDbNet;

/// <summary>
/// The environment RocksDb runs against: its background thread pools, and the
/// priorities those threads run at. Maps to <c>rocksdb_env_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// There are three pools, and each does different work rather than sharing
/// one queue. Compactions run on the low-priority pool, flushes on the
/// high-priority pool, and compactions into the bottommost level on the
/// bottom-priority pool. Sizing them is the main reason to touch this type;
/// see <see cref="LowPriorityBackgroundThreads"/>,
/// <see cref="HighPriorityBackgroundThreads"/> and
/// <see cref="BottomPriorityBackgroundThreads"/>.
/// </para>
/// <para>
/// Attach one to a database with <see cref="DbOptions.Env"/>. An
/// environment may be shared by several databases, in which case they share
/// its threads, and it must outlive every database using it.
/// </para>
/// </remarks>
public sealed class Env : RocksDbHandle
{
    /// <summary>Creates the default environment for the current platform.</summary>
    public Env()
    {
        Handle = NativeMethods.rocksdb_create_default_env();
    }

    /// <summary>Wraps an environment handle created by RocksDb.</summary>
    /// <param name="handle">The native <c>rocksdb_env_t</c> to take over.</param>
    private Env(nint handle)
        : base(handle)
    {
    }

    /// <summary>
    /// Creates a new instance of the <see cref="Env"/> class using the default RocksDb environment.
    /// This method is a convenient way to obtain an environment instance without needing to specify any parameters,
    /// and it will use the default settings provided by RocksDb.
    /// </summary>
    /// <returns>
    /// A new instance of the <see cref="Env"/> class initialized with the default RocksDb environment.
    /// </returns>
    public static Env Create()
    {
        return new Env(NativeMethods.rocksdb_create_default_env());
    }

    /// <summary>
    /// Creates a new instance of the <see cref="Env"/> class that uses an in-memory environment.
    /// This is useful for testing or scenarios where you want to avoid disk I/O and keep all data in memory.
    /// The in-memory environment allows RocksDb to operate without writing to disk, which can significantly improve performance for certain workloads, but it also means that all data will be lost when the process exits.
    /// </summary>
    /// <returns>
    /// A new instance of the <see cref="Env"/> class initialized with an in-memory environment.
    /// </returns>
    public static Env CreateInMemory()
    {
        return new Env(NativeMethods.rocksdb_create_mem_env());
    }

    /// <summary>
    /// Gets or sets the size of the low-priority thread pool, which is the pool
    /// that runs compactions.
    /// </summary>
    /// <remarks>
    /// This does not size a combined compaction-and-flush pool. The C API sends
    /// it to the default pool, which is the low-priority one, so it is the same
    /// setting as <see cref="LowPriorityBackgroundThreads"/>. Flushes run on the
    /// high-priority pool; see <see cref="HighPriorityBackgroundThreads"/>.
    /// </remarks>
    public int BackgroundThreads
    {
        get => NativeMethods.rocksdb_env_get_background_threads(Handle);
        set => NativeMethods.rocksdb_env_set_background_threads(Handle, value);
    }

    /// <summary>
    /// Gets or sets the size of the high-priority thread pool, which is the pool
    /// that runs flushes.
    /// </summary>
    /// <remarks>
    /// Compactions do not use this pool. If it is set to zero, flushes fall back
    /// to the low-priority pool and compete with compaction there.
    /// </remarks>
    public int HighPriorityBackgroundThreads
    {
        get => NativeMethods.rocksdb_env_get_high_priority_background_threads(Handle);
        set => NativeMethods.rocksdb_env_set_high_priority_background_threads(Handle, value);
    }

    /// <summary>
    /// Gets or sets the size of the low-priority thread pool, which is the pool
    /// that runs compactions.
    /// </summary>
    /// <remarks>
    /// The same pool as <see cref="BackgroundThreads"/>, named explicitly.
    /// Flushes do not use it unless the high-priority pool has no threads.
    /// </remarks>
    public int LowPriorityBackgroundThreads
    {
        get => NativeMethods.rocksdb_env_get_low_priority_background_threads(Handle);
        set => NativeMethods.rocksdb_env_set_low_priority_background_threads(Handle, value);
    }

    /// <summary>
    /// Gets or sets the size of the bottom-priority thread pool, which runs
    /// compactions into the bottommost level.
    /// </summary>
    /// <remarks>
    /// Separating the bottommost level matters because those compactions are the
    /// largest and longest-running; giving them their own pool stops them
    /// starving the smaller compactions that keep write amplification in check.
    /// Flushes never use this pool.
    /// </remarks>
    public int BottomPriorityBackgroundThreads
    {
        get => NativeMethods.rocksdb_env_get_bottom_priority_background_threads(Handle);
        set => NativeMethods.rocksdb_env_set_bottom_priority_background_threads(Handle, value);
    }

    /// <summary>
    /// Blocks the calling thread until all background threads have completed their work. This is useful for ensuring that all pending operations are finished before shutting down the database or performing other critical tasks.
    /// </summary>
    public void JoinAllThreads()
    {
        NativeMethods.rocksdb_env_join_all_threads(Handle);
    }

    /// <summary>
    /// Lowers the CPU priority of the high-priority thread pool.
    /// </summary>
    public void LowerHighPriorityThreadPoolCpuPriority()
    {
        NativeMethods.rocksdb_env_lower_high_priority_thread_pool_cpu_priority(Handle);
    }
    
    /// <summary>
    /// Lowers the IO priority of the high-priority thread pool.
    /// </summary>
    public void LowerHighPriorityThreadPoolIoPriority()
    {
        NativeMethods.rocksdb_env_lower_high_priority_thread_pool_io_priority(Handle);
    }
    
    /// <summary>
    /// Lowers the CPU priority of the thread pool.
    /// </summary>
    public void LowerThreadPoolCpuPriority()
    {
        NativeMethods.rocksdb_env_lower_thread_pool_cpu_priority(Handle);
    }

    /// <summary>
    /// Lowers the IO priority of the thread pool.
    /// </summary>
    public void LowerThreadPoolIoPriority()
    {
        NativeMethods.rocksdb_env_lower_thread_pool_io_priority(Handle);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="Env"/> class.
    /// </summary>
    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_env_destroy(Handle);
    }
}
