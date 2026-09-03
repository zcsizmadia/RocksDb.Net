using System.Runtime.InteropServices;
using System.Text;

namespace RocksDbNet;

/// <summary>
/// An atomic write batch. Apply to the database with <see cref="RocksDb.Write(WriteBatch, WriteOptions)"/>.
/// Maps to <c>rocksdb_writebatch_t</c>.
/// </summary>
public sealed unsafe class WriteBatch : RocksDbHandle
{
    /// <summary>Creates an empty write batch.</summary>
    public WriteBatch()
        : base(NativeMethods.rocksdb_writebatch_create())
    {
    }

    /// <summary>
    /// Wraps a batch RocksDb allocated, such as the one
    /// <see cref="WalIterator"/> produces for each WAL record.
    /// </summary>
    internal WriteBatch(nint handle)
        : base(handle)
    {
    }

    /// <summary>
    /// Wraps a batch owned by RocksDb, for the two batches handed to a
    /// <see cref="WalFilter"/> during recovery. With <paramref name="owned"/>
    /// <c>false</c> disposing this instance detaches from the pointer instead of
    /// destroying the batch.
    /// </summary>
    internal WriteBatch(nint handle, bool owned)
        : base(handle)
    {
        Owned = owned;
    }

    /// <summary>Returns the number of operations in the batch.</summary>
    public int Count => NativeMethods.rocksdb_writebatch_count(Handle);

    /// <summary>Clears all operations from the batch.</summary>
    public WriteBatch Clear()
    {
        NativeMethods.rocksdb_writebatch_clear(Handle);
        return this;
    }

    // ── Put ──────────────────────────────────────────────────────────────────

    /// <summary>Queues a Put into the default column family.</summary>
    public unsafe WriteBatch Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_writebatch_put(Handle, k, (nuint)key.Length, v, (nuint)value.Length);
        return this;
    }

    /// <summary>Queues a Put into the specified column family.</summary>
    public unsafe WriteBatch Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_writebatch_put_cf(Handle, cf.Handle, k, (nuint)key.Length, v, (nuint)value.Length);
        return this;
    }

    /// <summary>Convenience overload using UTF-8 string key and value.</summary>
    public WriteBatch Put(string key, string value)
        => Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value));

    /// <summary>Convenience overload using UTF-8 string key and value in a specific column family.</summary>
    public WriteBatch Put(string key, string value, ColumnFamilyHandle cf)
        => Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), cf);

    // ── Merge ────────────────────────────────────────────────────────────────

    /// <summary>Queues a Merge into the default column family.</summary>
    public unsafe WriteBatch Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_writebatch_merge(Handle, k, (nuint)key.Length, v, (nuint)value.Length);
        return this;
    }

    /// <summary>Queues a Merge into the specified column family.</summary>
    public unsafe WriteBatch Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_writebatch_merge_cf(Handle, cf.Handle, k, (nuint)key.Length, v, (nuint)value.Length);
        return this;
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    /// <summary>Queues a Delete from the default column family.</summary>
    public unsafe WriteBatch Delete(ReadOnlySpan<byte> key)
    {
        fixed (byte* k = key)
            NativeMethods.rocksdb_writebatch_delete(Handle, k, (nuint)key.Length);
        return this;
    }

    /// <summary>Queues a Delete from the specified column family.</summary>
    public unsafe WriteBatch Delete(ReadOnlySpan<byte> key, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        fixed (byte* k = key)
            NativeMethods.rocksdb_writebatch_delete_cf(Handle, cf.Handle, k, (nuint)key.Length);
        return this;
    }

    /// <summary>Convenience overload using a UTF-8 string key.</summary>
    public WriteBatch Delete(string key) => Delete(Encoding.UTF8.GetBytes(key));

    /// <summary>Convenience overload using a UTF-8 string key in a column family.</summary>
    public WriteBatch Delete(string key, ColumnFamilyHandle cf) => Delete(Encoding.UTF8.GetBytes(key), cf);

    // ── SingleDelete ─────────────────────────────────────────────────────────

    /// <summary>
    /// Queues a SingleDelete. Only valid when exactly one Put exists for the key
    /// (no prior Puts remain in the database for that key).
    /// </summary>
    public unsafe WriteBatch SingleDelete(ReadOnlySpan<byte> key)
    {
        fixed (byte* k = key)
            NativeMethods.rocksdb_writebatch_singledelete(Handle, k, (nuint)key.Length);
        return this;
    }

    /// <summary>Queues a SingleDelete in the specified column family.</summary>
    public unsafe WriteBatch SingleDelete(ReadOnlySpan<byte> key, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        fixed (byte* k = key)
            NativeMethods.rocksdb_writebatch_singledelete_cf(Handle, cf.Handle, k, (nuint)key.Length);
        return this;
    }

    // ── DeleteRange ──────────────────────────────────────────────────────────

    /// <summary>Queues a DeleteRange (deletes all keys in [startKey, endKey)) in the default column family.</summary>
    public unsafe WriteBatch DeleteRange(ReadOnlySpan<byte> startKey, ReadOnlySpan<byte> endKey)
    {
        fixed (byte* s = startKey)
        fixed (byte* e = endKey)
            NativeMethods.rocksdb_writebatch_delete_range(Handle, s, (nuint)startKey.Length, e, (nuint)endKey.Length);
        return this;
    }

    /// <summary>Queues a DeleteRange in the specified column family.</summary>
    public unsafe WriteBatch DeleteRange(ReadOnlySpan<byte> startKey, ReadOnlySpan<byte> endKey, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        fixed (byte* s = startKey)
        fixed (byte* e = endKey)
            NativeMethods.rocksdb_writebatch_delete_range_cf(Handle, cf.Handle, s, (nuint)startKey.Length, e, (nuint)endKey.Length);
        return this;
    }

    // ── Log data ─────────────────────────────────────────────────────────────

    /// <summary>Inserts a blob of user-defined log data into the WAL.</summary>
    public unsafe WriteBatch PutLogData(ReadOnlySpan<byte> blob)
    {
        fixed (byte* b = blob)
            NativeMethods.rocksdb_writebatch_put_log_data(Handle, b, (nuint)blob.Length);
        return this;
    }

    // ── Save points ──────────────────────────────────────────────────────────

    /// <summary>Marks a save point inside the batch.</summary>
    public WriteBatch SetSavePoint()
    {
        NativeMethods.rocksdb_writebatch_set_save_point(Handle);
        return this;
    }

    /// <summary>Rolls back to the most recent save point.</summary>
    public WriteBatch RollbackToSavePoint()
    {
        nint err = default;
        NativeMethods.rocksdb_writebatch_rollback_to_save_point(Handle, ref err);
        NativeMethods.ThrowOnError(err);
        return this;
    }

    /// <summary>Removes the most recent save point.</summary>
    public WriteBatch PopSavePoint()
    {
        nint err = default;
        NativeMethods.rocksdb_writebatch_pop_save_point(Handle, ref err);
        NativeMethods.ThrowOnError(err);
        return this;
    }

    // ── Raw data ─────────────────────────────────────────────────────────────

    /// <summary>Returns the serialized batch data as a byte array.</summary>
    public unsafe byte[] GetData()
    {
        byte* ptr = NativeMethods.rocksdb_writebatch_data(Handle, out nuint size);
        return new ReadOnlySpan<byte>(ptr, checked((int)size)).ToArray();
    }

    /// <summary>
    /// Verifies the batch's internal checksums, catching corruption before the
    /// batch is written.
    /// </summary>
    /// <remarks>
    /// Only meaningful when per-key protection was enabled for the batch, via
    /// <see cref="WriteOptions.ProtectionBytesPerKey"/>. Without it there is no
    /// checksum to check and the call simply succeeds.
    /// </remarks>
    /// <exception cref="RocksDbException">The batch is corrupt.</exception>
    public void VerifyChecksum()
    {
        nint err = default;
        NativeMethods.rocksdb_writebatch_verify_checksum(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ── Reading the batch back ───────────────────────────────────────────────

    /// <summary>
    /// Returns the operations in this batch, in the order they were queued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The missing half of change-data-capture.
    /// <see cref="RocksDb.GetUpdatesSince(ulong, WalReadOptions)"/> hands back a batch of
    /// everything written since a sequence number, and without this there was
    /// no way to see what was in it. With it, a batch from the write-ahead log
    /// can be inspected, filtered or translated rather than only replayed
    /// wholesale.
    /// </para>
    /// <para>
    /// Column families appear as numeric ids, because that is what the batch
    /// records. Compare them against <see cref="ColumnFamilyHandle.Id"/>.
    /// </para>
    /// <para>
    /// The entries are copied out during the call, so the result stays valid
    /// after the batch is modified or disposed. RocksDb hands the callbacks
    /// pointers into its own memory that are only valid for the duration of
    /// iteration, so materialising is the only safe shape; it also keeps a
    /// caller's exception from having to cross the native boundary.
    /// </para>
    /// </remarks>
    public unsafe IReadOnlyList<WriteBatchEntry> Entries()
    {
        ThrowIfDisposed();

        var collected = new List<WriteBatchEntry>();

        // All four callbacks are always installed. RocksDb invokes the put,
        // delete and merge handlers without a null check, so omitting one would
        // terminate the process on a batch containing that kind of record. Only
        // the log-data handler is checked.
        GCHandle state = GCHandle.Alloc(collected);
        try
        {
            NativeMethods.rocksdb_writebatch_iterate_cf_ld(
                Handle,
                GCHandle.ToIntPtr(state),
                Marshal.GetFunctionPointerForDelegate(_putCollector),
                Marshal.GetFunctionPointerForDelegate(_deleteCollector),
                Marshal.GetFunctionPointerForDelegate(_mergeCollector),
                Marshal.GetFunctionPointerForDelegate(_logDataCollector));
        }
        finally
        {
            state.Free();
        }

        return collected;
    }

    // Held in static fields so the delegates are not collected while native
    // code holds pointers to them.
    private static readonly PutCfCollector _putCollector = CollectPut;
    private static readonly DeleteCfCollector _deleteCollector = CollectDelete;
    private static readonly PutCfCollector _mergeCollector = CollectMerge;
    private static readonly LogDataCollector _logDataCollector = CollectLogData;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void PutCfCollector(nint state, uint cfId, byte* key, nuint keyLen, byte* value, nuint valueLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void DeleteCfCollector(nint state, uint cfId, byte* key, nuint keyLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void LogDataCollector(nint state, byte* blob, nuint blobLen);

    private static unsafe void CollectPut(nint state, uint cfId, byte* key, nuint keyLen, byte* value, nuint valueLen)
        => Collect(state, WriteBatchEntryKind.Put, cfId, key, keyLen, value, valueLen);

    private static unsafe void CollectMerge(nint state, uint cfId, byte* key, nuint keyLen, byte* value, nuint valueLen)
        => Collect(state, WriteBatchEntryKind.Merge, cfId, key, keyLen, value, valueLen);

    private static unsafe void CollectDelete(nint state, uint cfId, byte* key, nuint keyLen)
        => Collect(state, WriteBatchEntryKind.Delete, cfId, key, keyLen, value: null, valueLen: 0);

    private static unsafe void CollectLogData(nint state, byte* blob, nuint blobLen)
        => Collect(state, WriteBatchEntryKind.LogData, cfId: 0, key: null, keyLen: 0, blob, blobLen);

    private static unsafe void Collect(
        nint state, WriteBatchEntryKind kind, uint cfId, byte* key, nuint keyLen, byte* value, nuint valueLen)
    {
        try
        {
            if (GCHandle.FromIntPtr(state).Target is not List<WriteBatchEntry> collected)
            {
                return;
            }

            byte[] keyBytes = key is null ? [] : new ReadOnlySpan<byte>(key, checked((int)keyLen)).ToArray();
            byte[]? valueBytes = kind == WriteBatchEntryKind.Delete
                ? null
                : value is null ? [] : new ReadOnlySpan<byte>(value, checked((int)valueLen)).ToArray();

            collected.Add(new WriteBatchEntry(kind, cfId, keyBytes, valueBytes));
        }
        catch (Exception ex)
        {
            // A managed exception must not reach native code. Dropping the entry
            // is the only fallback available: RocksDb gives these callbacks no
            // way to report failure and does not check a result.
            RocksDbCallbacks.Report(nameof(Entries), ex, state);
        }
    }

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_writebatch_destroy(Handle);
    }
}
