using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RocksDbNet;

public sealed class Env : RocksDbHandle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Env"/> class, representing the environment in which RocksDb operates.
    /// This constructor creates a default RocksDb environment, which is used for managing background threads and controlling thread priorities.
    /// The <see cref="Env"/> class provides methods to configure the number of background threads for compaction and flush operations,
    /// as well as to lower the CPU and IO priorities of these threads. It also allows you to block the calling thread until all background threads have completed their work,
    /// ensuring that all pending operations are finished before shutting down the database or performing other critical tasks.
    /// </summary>
    public Env()
    {
        Handle = NativeMethods.rocksdb_create_default_env();
    }

    /// <summary>
    /// Represents the environment in which RocksDb operates, providing methods to manage background threads and control thread priorities. The <see cref="Env"/> class allows you to configure the number of background threads used for compaction and flush operations, as well as to lower the CPU and IO priorities of these threads. It also provides a method to block the calling thread until all background threads have completed their work. This class is essential for optimizing the performance of RocksDb by managing its threading behavior effectively.
    /// </summary>
    /// <param name="handle"></param>
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
    /// Gets or sets the number of background threads used by RocksDb for compaction and flush operations. 
    /// </summary>
    public int BackgroundThreads
    {
        get => NativeMethods.rocksdb_env_get_background_threads(Handle);
        set => NativeMethods.rocksdb_env_set_background_threads(Handle, value);
    }

    /// <summary>
    /// Gets or sets the number of high-priority background threads used by RocksDb for compaction and flush operations.
    /// </summary>
    public int HighPriorityBackgroundThreads
    {
        get => NativeMethods.rocksdb_env_get_high_priority_background_threads(Handle);
        set => NativeMethods.rocksdb_env_set_high_priority_background_threads(Handle, value);
    }

    /// <summary>
    /// Gets or sets the number of low-priority background threads used by RocksDb for compaction and flush operations.
    /// </summary>
    public int LowPriorityBackgroundThreads
    {
        get => NativeMethods.rocksdb_env_get_low_priority_background_threads(Handle);
        set => NativeMethods.rocksdb_env_set_low_priority_background_threads(Handle, value);
    }

    /// <summary>
    /// Gets or sets the number of bottom-priority background threads used by RocksDb for compaction and flush operations.
    /// </summary>
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
    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_env_destroy(Handle);
    }
}
