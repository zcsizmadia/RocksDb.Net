namespace RocksDbNet;

/// <summary>
/// Settings for a single <see cref="Transaction"/>. Maps to
/// <c>rocksdb_transaction_options_t</c>.
/// </summary>
/// <remarks>
/// RocksDb copies these when the transaction begins, so this object may be
/// disposed straight afterwards. Database-wide settings live on
/// <see cref="TransactionDbOptions"/>.
/// </remarks>
public sealed class TransactionOptions : RocksDbHandle
{
    /// <summary>Creates options with RocksDb's defaults.</summary>
    public TransactionOptions()
        : base(NativeMethods.rocksdb_transaction_options_create())
    {
    }

    /// <summary>
    /// Whether to take a snapshot when the transaction begins, so that its
    /// writes are validated against the state at that moment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what turns a transaction from "last writer wins on the keys I
    /// locked" into repeatable-read behaviour. Without it, a locking read sees
    /// the latest committed value rather than the value as of the transaction's
    /// start, so a read-modify-write can be based on data written by a
    /// transaction that committed after this one began.
    /// </para>
    /// <para>
    /// With it, a commit fails when a key the transaction read has changed
    /// since the snapshot, and the caller retries.
    /// </para>
    /// </remarks>
    public bool SetSnapshot
    {
        get => NativeMethods.rocksdb_transaction_options_get_set_snapshot(Handle) != 0;
        set => NativeMethods.rocksdb_transaction_options_set_set_snapshot(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether to detect deadlocks and fail immediately rather than waiting for
    /// the lock timeout.
    /// </summary>
    /// <remarks>
    /// Detection costs a graph walk on every blocked acquisition, bounded by
    /// <see cref="DeadlockDetectDepth"/>. Without it a deadlock resolves as an
    /// ordinary timeout, which is slower but cheaper to police.
    /// </remarks>
    public bool DeadlockDetect
    {
        get => NativeMethods.rocksdb_transaction_options_get_deadlock_detect(Handle) != 0;
        set => NativeMethods.rocksdb_transaction_options_set_deadlock_detect(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// How deep the deadlock detector will search, when
    /// <see cref="DeadlockDetect"/> is enabled.
    /// </summary>
    public long DeadlockDetectDepth
    {
        get => NativeMethods.rocksdb_transaction_options_get_deadlock_detect_depth(Handle);
        set => NativeMethods.rocksdb_transaction_options_set_deadlock_detect_depth(Handle, value);
    }

    /// <summary>
    /// How long this transaction waits for a lock, in milliseconds. Negative
    /// means use <see cref="TransactionDbOptions.TransactionLockTimeout"/>.
    /// </summary>
    /// <remarks>
    /// A non-negative value replaces the database-wide timeout for this
    /// transaction, in either direction: it can wait longer than the database
    /// setting allows as readily as it can give up sooner. Measured against a
    /// database configured to fail immediately, a transaction asking for three
    /// seconds waits the full three. Zero fails immediately on contention,
    /// which is what a test wanting a deterministic conflict should use.
    /// </remarks>
    public long LockTimeout
    {
        get => NativeMethods.rocksdb_transaction_options_get_lock_timeout(Handle);
        set => NativeMethods.rocksdb_transaction_options_set_lock_timeout(Handle, value);
    }

    /// <summary>
    /// How long the transaction may live before another may forcibly expire it,
    /// in milliseconds. Negative, the default, means it never expires.
    /// </summary>
    /// <remarks>
    /// Guards against a transaction that holds locks and is never committed or
    /// rolled back, for instance because the process handling it stalled.
    /// </remarks>
    public long Expiration
    {
        get => NativeMethods.rocksdb_transaction_options_get_expiration(Handle);
        set => NativeMethods.rocksdb_transaction_options_set_expiration(Handle, value);
    }

    /// <summary>
    /// Maximum size in bytes of the transaction's pending writes, or zero for no
    /// limit. A write that would exceed it fails.
    /// </summary>
    public ulong MaxWriteBatchSize
    {
        get => (ulong)NativeMethods.rocksdb_transaction_options_get_max_write_batch_size(Handle);
        set => NativeMethods.rocksdb_transaction_options_set_max_write_batch_size(Handle, checked((nuint)value));
    }

    /// <summary>
    /// Whether to skip locking and conflict detection for this transaction.
    /// </summary>
    /// <remarks>
    /// Carries the same warning as
    /// <see cref="TransactionDbOptions.SkipConcurrencyControl"/>: it removes the
    /// guarantee the transaction exists to provide.
    /// </remarks>
    public bool SkipConcurrencyControl
    {
        get => NativeMethods.rocksdb_transaction_options_get_skip_concurrency_control(Handle) != 0;
        set => NativeMethods.rocksdb_transaction_options_set_skip_concurrency_control(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether committing skips the prepare phase, for two-phase commit.
    /// </summary>
    public bool SkipPrepare
    {
        get => NativeMethods.rocksdb_transaction_options_get_skip_prepare(Handle) != 0;
        set => NativeMethods.rocksdb_transaction_options_set_skip_prepare(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// How long a blocked acquisition waits before running deadlock detection,
    /// in microseconds.
    /// </summary>
    public long DeadlockTimeoutMicros
    {
        get => NativeMethods.rocksdb_transaction_options_get_deadlock_timeout_us(Handle);
        set => NativeMethods.rocksdb_transaction_options_set_deadlock_timeout_us(Handle, value);
    }

    /// <summary>
    /// Size in bytes at which pending writes are flushed to the memtable early.
    /// </summary>
    /// <remarks>
    /// Zero disables early flushing rather than selecting a default. A negative
    /// value, which is what this starts at, is the one that defers to
    /// <see cref="TransactionDbOptions.DefaultWriteBatchFlushThreshold"/>.
    /// </remarks>
    public long WriteBatchFlushThreshold
    {
        get => NativeMethods.rocksdb_transaction_options_get_write_batch_flush_threshold(Handle);
        set => NativeMethods.rocksdb_transaction_options_set_write_batch_flush_threshold(Handle, value);
    }

    /// <summary>
    /// Whether recovery uses only the last commit-time write batch.
    /// </summary>
    public bool UseOnlyTheLastCommitTimeBatchForRecovery
    {
        get => NativeMethods.rocksdb_transaction_options_get_use_only_the_last_commit_time_batch_for_recovery(Handle) != 0;
        set => NativeMethods.rocksdb_transaction_options_set_use_only_the_last_commit_time_batch_for_recovery(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether the transaction's write batch tracks the size of user-defined
    /// timestamps.
    /// </summary>
    public bool WriteBatchTrackTimestampSize
    {
        get => NativeMethods.rocksdb_transaction_options_get_write_batch_track_timestamp_size(Handle) != 0;
        set => NativeMethods.rocksdb_transaction_options_set_write_batch_track_timestamp_size(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether a commit may write its data directly rather than through the
    /// memtable.
    /// </summary>
    public bool CommitBypassMemtable
    {
        get => NativeMethods.rocksdb_transaction_options_get_commit_bypass_memtable(Handle) != 0;
        set => NativeMethods.rocksdb_transaction_options_set_commit_bypass_memtable(Handle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Number of pending operations above which commit takes an optimised path,
    /// or zero to disable.
    /// </summary>
    public uint LargeTransactionCommitOptimizeThreshold
    {
        get => NativeMethods.rocksdb_transaction_options_get_large_txn_commit_optimize_threshold(Handle);
        set => NativeMethods.rocksdb_transaction_options_set_large_txn_commit_optimize_threshold(Handle, value);
    }

    /// <summary>
    /// Size in bytes above which commit takes an optimised path, or zero to
    /// disable.
    /// </summary>
    public ulong LargeTransactionCommitOptimizeByteThreshold
    {
        get => NativeMethods.rocksdb_transaction_options_get_large_txn_commit_optimize_byte_threshold(Handle);
        set => NativeMethods.rocksdb_transaction_options_set_large_txn_commit_optimize_byte_threshold(Handle, value);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_transaction_options_destroy(Handle);
    }
}
