namespace RocksDbNet;

/// <summary>
/// When an optimistic transaction checks that the keys it read have not
/// changed, mapped from <c>rocksdb::OccValidationPolicy</c> in
/// <c>utilities/optimistic_transaction_db.h</c>.
/// </summary>
public enum OccValidationPolicy
{
    /// <summary>
    /// Validate one transaction at a time, after entering the write group.
    /// </summary>
    /// <remarks>
    /// Simple, and the cheaper choice when commits are rare. Validation happens
    /// single-threaded inside the write group, so under a busy commit path it
    /// contends on the database mutex.
    /// </remarks>
    ValidateSerial = 0,

    /// <summary>
    /// Validate before entering the write group, so transactions validate
    /// alongside one another. This is the default.
    /// </summary>
    /// <remarks>
    /// Each transaction takes locks for its own write set in a fixed order,
    /// which keeps the validation off the database mutex. The reason it is the
    /// default: mutex contention was the practical limit on commit throughput
    /// under <see cref="ValidateSerial"/>.
    /// </remarks>
    ValidateParallel = 1,
}

/// <summary>
/// A fixed set of lock buckets that several optimistic transaction databases
/// can share. Maps to <c>rocksdb_optimistictransactiondb_lock_buckets_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="OccValidationPolicy.ValidateParallel"/> uses these. The
/// buckets are what a committing transaction locks its write set in, so more
/// of them means fewer transactions colliding on the same bucket by accident,
/// at the cost of memory.
/// </para>
/// <para>
/// Sharing one instance across databases is the reason this is a separate
/// object rather than a number: a process running many small databases would
/// otherwise pay for a full set of buckets per database. Give it to each
/// through
/// <see cref="OptimisticTransactionDbOptions.SetSharedLockBuckets(OccLockBuckets?)"/>.
/// </para>
/// <para>
/// RocksDb holds its own reference, so this may be disposed once every database
/// that shares it has been opened.
/// </para>
/// </remarks>
public sealed class OccLockBuckets : RocksDbHandle
{
    /// <summary>Creates a set of lock buckets.</summary>
    /// <param name="bucketCount">
    /// How many buckets. Rounded up to a power of two by RocksDb.
    /// </param>
    /// <param name="cacheAligned">
    /// Pad each bucket onto its own cache line. Costs memory and buys back the
    /// false sharing between threads locking neighbouring buckets.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bucketCount"/> is zero.</exception>
    public OccLockBuckets(nuint bucketCount, bool cacheAligned = false)
        : base(Create(bucketCount, cacheAligned))
    {
    }

    private static nint Create(nuint bucketCount, bool cacheAligned)
    {
        ArgumentOutOfRangeException.ThrowIfZero(bucketCount);

        return NativeMethods.rocksdb_optimistictransactiondb_lock_buckets_create(
            bucketCount, cacheAligned ? (byte)1 : (byte)0);
    }

    /// <summary>Roughly how much memory the buckets occupy, in bytes.</summary>
    public nuint ApproximateMemoryUsage
        => NativeMethods.rocksdb_optimistictransactiondb_lock_buckets_approximate_memory_usage(Handle);

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_optimistictransactiondb_lock_buckets_destroy(Handle);
    }
}

/// <summary>
/// Settings for opening an <see cref="OptimisticTransactionDb"/>. Maps to
/// <c>rocksdb_optimistictransactiondb_options_t</c>.
/// </summary>
/// <remarks>
/// These are copied at open, so an instance may be disposed as soon as the
/// database is open. That is unlike <see cref="DbOptions"/>, which the database
/// takes ownership of.
/// </remarks>
public sealed class OptimisticTransactionDbOptions : RocksDbHandle
{
    /// <summary>Creates options with RocksDb's defaults.</summary>
    public OptimisticTransactionDbOptions()
        : base(NativeMethods.rocksdb_optimistictransactiondb_options_create())
    {
    }

    /// <summary>When a committing transaction validates what it read.</summary>
    public OccValidationPolicy ValidatePolicy
    {
        get => (OccValidationPolicy)NativeMethods.rocksdb_optimistictransactiondb_options_get_validate_policy(Handle);
        set => NativeMethods.rocksdb_optimistictransactiondb_options_set_validate_policy(Handle, (int)value);
    }

    /// <summary>
    /// How many lock buckets to allocate for this database, when it is not
    /// sharing a set.
    /// </summary>
    /// <remarks>
    /// Only <see cref="OccValidationPolicy.ValidateParallel"/> uses these.
    /// Setting <see cref="SetSharedLockBuckets(OccLockBuckets?)"/> supersedes
    /// this, since a shared set brings its own count.
    /// </remarks>
    public uint OccLockBucketCount
    {
        get => NativeMethods.rocksdb_optimistictransactiondb_options_get_occ_lock_buckets(Handle);
        set => NativeMethods.rocksdb_optimistictransactiondb_options_set_occ_lock_buckets(Handle, value);
    }

    /// <summary>
    /// Uses <paramref name="lockBuckets"/> instead of allocating a set for this
    /// database alone, or passes <see langword="null"/> to go back to a private
    /// set.
    /// </summary>
    /// <remarks>
    /// RocksDb takes its own reference, so the caller keeps ownership and may
    /// dispose the buckets once every database sharing them is open.
    /// </remarks>
    public OptimisticTransactionDbOptions SetSharedLockBuckets(OccLockBuckets? lockBuckets)
    {
        ThrowIfDisposed();

        NativeMethods.rocksdb_optimistictransactiondb_options_set_shared_lock_buckets(
            Handle, lockBuckets?.Handle ?? nint.Zero);

        return this;
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_optimistictransactiondb_options_destroy(Handle);
    }
}

/// <summary>
/// Per-transaction settings for
/// <see cref="OptimisticTransactionDb.BeginTransaction"/>. Maps to
/// <c>rocksdb_optimistictransaction_options_t</c>.
/// </summary>
/// <remarks>
/// Deliberately smaller than <see cref="TransactionOptions"/>. There are no
/// lock timeouts or deadlock settings here because an optimistic transaction
/// takes no locks while it runs, which is the point of it.
/// </remarks>
public sealed class OptimisticTransactionOptions : RocksDbHandle
{
    /// <summary>Creates options with RocksDb's defaults.</summary>
    public OptimisticTransactionOptions()
        : base(NativeMethods.rocksdb_optimistictransaction_options_create())
    {
    }

    /// <summary>
    /// Pin a snapshot when the transaction begins, so its reads see the
    /// database as it was at that moment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This changes what conflicts. Without it, validation compares against the
    /// state at each read, so a transaction that read a key early and committed
    /// late tolerates a change in between. With it, the comparison is against
    /// the moment the transaction began, and any change to a key it read is a
    /// conflict.
    /// </para>
    /// <para>
    /// The stricter behaviour is what a read-modify-write usually wants, and is
    /// the reason to reach for this rather than leave it off.
    /// </para>
    /// </remarks>
    public bool SetSnapshot
    {
        get => NativeMethods.rocksdb_optimistictransaction_options_get_set_snapshot(Handle) != 0;
        set => NativeMethods.rocksdb_optimistictransaction_options_set_set_snapshot(Handle, value ? (byte)1 : (byte)0);
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_optimistictransaction_options_destroy(Handle);
    }
}
