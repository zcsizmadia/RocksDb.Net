namespace RocksDbNet;

/// <summary>
/// When a transaction's data reaches the memtable, mapped from
/// <c>rocksdb::TxnDBWritePolicy</c> in <c>utilities/transaction_db.h</c>.
/// </summary>
public enum TransactionDbWritePolicy
{
    /// <summary>
    /// Data is written when the transaction commits. The default, and the only
    /// value RocksDb describes as mature.
    /// </summary>
    WriteCommitted = 0,

    /// <summary>
    /// Data is written after the prepare phase of two-phase commit.
    /// </summary>
    /// <remarks>
    /// RocksDb marks this experimental: less validated than
    /// <see cref="WriteCommitted"/> and less compatible with other features.
    /// </remarks>
    WritePrepared = 1,

    /// <summary>
    /// Data is written before the prepare phase of two-phase commit.
    /// </summary>
    /// <remarks>
    /// RocksDb marks this experimental, with the same caveats as
    /// <see cref="WritePrepared"/>.
    /// </remarks>
    WriteUnprepared = 2,
}

/// <summary>
/// Settings for opening a <see cref="TransactionDb"/>. Maps to
/// <c>rocksdb_transactiondb_options_t</c>.
/// </summary>
/// <remarks>
/// These configure the lock manager shared by every transaction on the
/// database. Per-transaction settings live on <see cref="TransactionOptions"/>.
/// RocksDb copies these when the database opens, so this object may be disposed
/// straight afterwards.
/// </remarks>
public sealed class TransactionDbOptions : RocksDbHandle
{
    /// <summary>Creates options with RocksDb's defaults.</summary>
    public TransactionDbOptions()
        : base(NativeMethods.rocksdb_transactiondb_options_create())
    {
    }

    /// <summary>
    /// When a transaction's data becomes visible in the memtable. Default is
    /// <see cref="TransactionDbWritePolicy.WriteCommitted"/>.
    /// </summary>
    public TransactionDbWritePolicy WritePolicy
    {
        get => (TransactionDbWritePolicy)NativeMethods.rocksdb_transactiondb_options_get_write_policy(Handle);
        set => NativeMethods.rocksdb_transactiondb_options_set_write_policy(Handle, (int)value);
    }

    /// <summary>
    /// How long a transaction waits for a lock before failing, in
    /// milliseconds. Negative means wait forever; zero means fail immediately.
    /// </summary>
    /// <remarks>
    /// The value used by transactions that do not set one of their own. It is
    /// not a ceiling: a non-negative
    /// <see cref="TransactionOptions.LockTimeout"/> replaces this outright, and
    /// may be longer than it.
    /// </remarks>
    public long TransactionLockTimeout
    {
        get => NativeMethods.rocksdb_transactiondb_options_get_transaction_lock_timeout(Handle);
        set => NativeMethods.rocksdb_transactiondb_options_set_transaction_lock_timeout(Handle, value);
    }

    /// <summary>
    /// How long a write made outside any transaction waits for a lock, in
    /// milliseconds. Negative means wait forever.
    /// </summary>
    /// <remarks>
    /// Writes through <see cref="TransactionDb"/> itself, rather than through a
    /// <see cref="Transaction"/>, still take locks. This is their timeout.
    /// </remarks>
    public long DefaultLockTimeout
    {
        get => NativeMethods.rocksdb_transactiondb_options_get_default_lock_timeout(Handle);
        set => NativeMethods.rocksdb_transactiondb_options_set_default_lock_timeout(Handle, value);
    }

    /// <summary>
    /// Maximum number of locks the manager will hold, or a negative value for
    /// no limit, which is the default.
    /// </summary>
    /// <remarks>
    /// A transaction that would exceed the limit fails rather than blocking.
    /// Worth setting when transactions might touch an unbounded number of keys,
    /// since each locked key costs memory.
    /// </remarks>
    public long MaxNumLocks
    {
        get => NativeMethods.rocksdb_transactiondb_options_get_max_num_locks(Handle);
        set => NativeMethods.rocksdb_transactiondb_options_set_max_num_locks(Handle, value);
    }

    /// <summary>
    /// How many stripes the lock table is divided into. More stripes reduce
    /// contention between transactions touching unrelated keys.
    /// </summary>
    public nuint NumStripes
    {
        get => NativeMethods.rocksdb_transactiondb_options_get_num_stripes(Handle);
        set => NativeMethods.rocksdb_transactiondb_options_set_num_stripes(Handle, value);
    }

    /// <summary>
    /// How many deadlocks to keep in the reportable history buffer.
    /// </summary>
    public uint MaxNumDeadlocks
    {
        get => NativeMethods.rocksdb_transactiondb_options_get_max_num_deadlocks(Handle);
        set => NativeMethods.rocksdb_transactiondb_options_set_max_num_deadlocks(Handle, value);
    }

    /// <summary>
    /// Whether rolling back a transaction also rolls back merge operands it
    /// wrote.
    /// </summary>
    public bool RollbackMergeOperands
    {
        get => NativeMethods.rocksdb_transactiondb_options_get_rollback_merge_operands(Handle) != 0;
        set => NativeMethods.rocksdb_transactiondb_options_set_rollback_merge_operands(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether to skip locking and conflict detection entirely.
    /// </summary>
    /// <remarks>
    /// This removes the guarantee the database exists to provide. Only safe when
    /// the application already serialises its writes by other means, and wants
    /// transactions purely for atomicity.
    /// </remarks>
    public bool SkipConcurrencyControl
    {
        get => NativeMethods.rocksdb_transactiondb_options_get_skip_concurrency_control(Handle) != 0;
        set => NativeMethods.rocksdb_transactiondb_options_set_skip_concurrency_control(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Default size in bytes at which a transaction's pending writes are
    /// flushed to the memtable early, or zero to disable.
    /// </summary>
    public long DefaultWriteBatchFlushThreshold
    {
        get => NativeMethods.rocksdb_transactiondb_options_get_default_write_batch_flush_threshold(Handle);
        set => NativeMethods.rocksdb_transactiondb_options_set_default_write_batch_flush_threshold(Handle, value);
    }

    /// <summary>
    /// Whether to validate user-defined timestamps during conflict checking.
    /// </summary>
    public bool EnableUdtValidation
    {
        get => NativeMethods.rocksdb_transactiondb_options_get_enable_udt_validation(Handle) != 0;
        set => NativeMethods.rocksdb_transactiondb_options_set_enable_udt_validation(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether to use the per-key point lock manager rather than the striped
    /// one.
    /// </summary>
    public bool UsePerKeyPointLockManager
    {
        get => NativeMethods.rocksdb_transactiondb_options_get_use_per_key_point_lock_mgr(Handle) != 0;
        set => NativeMethods.rocksdb_transactiondb_options_set_use_per_key_point_lock_mgr(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Size in bytes above which a commit writes its data directly rather than
    /// through the memtable, or zero to disable.
    /// </summary>
    public uint CommitBypassMemtableThreshold
    {
        get => NativeMethods.rocksdb_transactiondb_options_get_txn_commit_bypass_memtable_threshold(Handle);
        set => NativeMethods.rocksdb_transactiondb_options_set_txn_commit_bypass_memtable_threshold(Handle, value);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_transactiondb_options_destroy(Handle);
    }
}
