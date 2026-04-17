using RocksDbNet.Native;
using System.Runtime.InteropServices;
using System.Text;

namespace RocksDbNet;

/// <summary>
/// A RocksDB embedded key-value database.
/// Thread-safe: all operations may be called concurrently from multiple threads.
/// </summary>
public sealed class RocksDb : RocksDbHandle
{
    // Shared default options used when the caller passes null — avoids creating
    // a new native options object on every call.
    private static readonly ReadOptions _defaultReadOptions = new();
    private static readonly WriteOptions _defaultWriteOptions = new();
    private static readonly FlushOptions _defaultFlushOptions = new();

    private readonly Dictionary<string, ColumnFamilyHandle>? _columnFamilyHandles;

    private RocksDb(nint handle)
    {
        Handle = handle;
    }

    private RocksDb(nint handle, nint[] cfHandles)
    {
        Handle = handle;
        
        _columnFamilyHandles = new();
        foreach (var cf in cfHandles)
        {
            ColumnFamilyHandle cfh =  new ColumnFamilyHandle(cf);
            _columnFamilyHandles[cfh.Name] = cfh;
        }
    }
    
    // ─────────────────────────────────────────────────────────────────────────
    // Open / static management
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Opens (or creates) a database at <paramref name="path"/>.</summary>
    public static RocksDb Open(DbOptions options, string path)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        nint handle = NativeMethods.rocksdb_open(options.Handle, path, ref err);
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle);
    }

    /// <summary>
    /// Opens the database with an explicit set of column families.
    /// The <c>"default"</c> column family must always be included.
    /// Returns the database and one <see cref="ColumnFamilyHandle"/> per descriptor.
    /// </summary>
    public static RocksDb Open(DbOptions options, string path, IReadOnlyList<ColumnFamilyDescriptor> columnFamilies)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(columnFamilies);

        nint[] cfHandles = new nint[columnFamilies.Count];

        nint err = default;
        nint handle = NativeMethods.rocksdb_open_column_families(
            options.Handle, path,
            columnFamilies.Count,
            columnFamilies.Select(cf => cf.Name).ToArray(),
            columnFamilies.Select(cf => cf.Options.Handle).ToArray(),
            cfHandles, ref err);
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle, cfHandles);
    }

    /// <summary>Opens an existing database in read-only mode.</summary>
    public static RocksDb OpenReadOnly(DbOptions options, string path, bool errorIfWalExists = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        nint handle = NativeMethods.rocksdb_open_for_read_only(
            options.Handle, path, errorIfWalExists ? (byte)1 : (byte)0, ref err);
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle);
    }

    /// <summary>Opens an existing database in read-only mode.</summary>
    public static RocksDb OpenReadOnly(DbOptions options, string path, IReadOnlyList<ColumnFamilyDescriptor> columnFamilies, bool errorIfWalExists = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(columnFamilies);

        nint[] cfHandles = new nint[columnFamilies.Count];

        nint err = default;
        nint handle = NativeMethods.rocksdb_open_for_read_only_column_families(
            options.Handle, path,
            columnFamilies.Count,
            columnFamilies.Select(cf => cf.Name).ToArray(),
            columnFamilies.Select(cf => cf.Options.Handle).ToArray(),
            cfHandles,
            errorIfWalExists ? (byte)1 : (byte)0,
            ref err);
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle, cfHandles);
    }

    /// <summary>
    /// Opens the database as a secondary instance that can catch up to the primary.
    /// </summary>
    public static RocksDb OpenAsSecondary(DbOptions options, string path, string secondaryPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(secondaryPath);

        nint err = default;
        nint handle = NativeMethods.rocksdb_open_as_secondary(options.Handle, path, secondaryPath, ref err);
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle);
    }

    /// <summary>Opens the database with a TTL (time-to-live) compaction filter.</summary>
    public static RocksDb OpenWithTtl(DbOptions options, string path, int ttlSeconds)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        nint handle = NativeMethods.rocksdb_open_with_ttl(options.Handle, path, ttlSeconds, ref err);
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle);
    }

    /// <summary>Destroys the database files at <paramref name="path"/>. Irreversible.</summary>
    public static void Destroy(DbOptions options, string path)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        NativeMethods.rocksdb_destroy_db(options.Handle, path, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Attempts to repair a damaged database at <paramref name="path"/>.</summary>
    public static void Repair(DbOptions options, string path)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        NativeMethods.rocksdb_repair_db(options.Handle, path, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Lists the column family names present in the database at <paramref name="path"/>.</summary>
    public static unsafe IReadOnlyList<string> ListColumnFamilies(DbOptions options, string path)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        nint* list = NativeMethods.rocksdb_list_column_families(options.Handle, path, out nuint count, ref err);
        NativeMethods.ThrowOnError(err);

        var result = new string[(int)count];
        for (int i = 0; i < (int)count; i++)
            result[i] = Marshal.PtrToStringUTF8(list[i]) ?? string.Empty;

        NativeMethods.rocksdb_list_column_families_destroy(list, count);
        return Array.AsReadOnly(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Write operations
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/> in the default column family.</summary>
    public unsafe void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WriteOptions? options = null)
    {
        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_put(Handle, (options ?? _defaultWriteOptions).Handle,
                k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/> in <paramref name="cf"/>.</summary>
    public unsafe void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ColumnFamilyHandle cf, WriteOptions? options = null)
    {
        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_put_cf(Handle, (options ?? _defaultWriteOptions).Handle, cf.Handle,
                k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Convenience overload using UTF-8 string key and value.</summary>
    public void Put(string key, string value, WriteOptions? options = null)
        => Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), options);

    /// <summary>Convenience overload using UTF-8 string key and value in a column family.</summary>
    public void Put(string key, string value, ColumnFamilyHandle cf, WriteOptions? options = null)
        => Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), cf, options);

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Deletes the entry for <paramref name="key"/> from the default column family.</summary>
    public unsafe void Delete(ReadOnlySpan<byte> key, WriteOptions? options = null)
    {
        nint err = default;
        fixed (byte* k = key)
            NativeMethods.rocksdb_delete(Handle, (options ?? _defaultWriteOptions).Handle, k, (nuint)key.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Deletes the entry for <paramref name="key"/> from <paramref name="cf"/>.</summary>
    public unsafe void Delete(ReadOnlySpan<byte> key, ColumnFamilyHandle cf, WriteOptions? options = null)
    {
        nint err = default;
        fixed (byte* k = key)
            NativeMethods.rocksdb_delete_cf(Handle, (options ?? _defaultWriteOptions).Handle, cf.Handle,
                k, (nuint)key.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Convenience overload using a UTF-8 string key.</summary>
    public void Delete(string key, WriteOptions? options = null)
        => Delete(Encoding.UTF8.GetBytes(key), options);

    /// <summary>Convenience overload using a UTF-8 string key in a column family.</summary>
    public void Delete(string key, ColumnFamilyHandle cf, WriteOptions? options = null)
        => Delete(Encoding.UTF8.GetBytes(key), cf, options);

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes all keys in the range [<paramref name="startKey"/>, <paramref name="endKey"/>)
    /// from <paramref name="cf"/>.
    /// </summary>
    public unsafe void DeleteRange(ReadOnlySpan<byte> startKey, ReadOnlySpan<byte> endKey,
        ColumnFamilyHandle cf, WriteOptions? options = null)
    {
        nint err = default;
        fixed (byte* s = startKey)
        fixed (byte* e = endKey)
            NativeMethods.rocksdb_delete_range_cf(Handle, (options ?? _defaultWriteOptions).Handle, cf.Handle,
                s, (nuint)startKey.Length, e, (nuint)endKey.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Applies a merge operation to <paramref name="key"/> in the default column family.</summary>
    public unsafe void Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WriteOptions? options = null)
    {
        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_merge(Handle, (options ?? _defaultWriteOptions).Handle,
                k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Applies a merge operation to <paramref name="key"/> in <paramref name="cf"/>.</summary>
    public unsafe void Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        ColumnFamilyHandle cf, WriteOptions? options = null)
    {
        nint err = default;
        fixed (byte* k = key)
        fixed (byte* v = value)
            NativeMethods.rocksdb_merge_cf(Handle, (options ?? _defaultWriteOptions).Handle, cf.Handle,
                k, (nuint)key.Length, v, (nuint)value.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Atomically applies all operations in <paramref name="batch"/>.</summary>
    public void Write(WriteBatch batch, WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        nint err = default;
        NativeMethods.rocksdb_write(Handle, (options ?? _defaultWriteOptions).Handle, batch.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Read operations
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the value associated with <paramref name="key"/> in the default column family,
    /// or <c>null</c> if the key does not exist.
    /// </summary>
    public byte[]? Get(ReadOnlySpan<byte> key, ReadOptions? options = null)
    {
        return GetInternal(key, options);
    }

    private unsafe byte[]? GetInternal(ReadOnlySpan<byte> key, ReadOptions? options)
    {
        nint err = default;
        byte* valPtr;
        nuint vallen;
        fixed (byte* k = key)
            valPtr = NativeMethods.rocksdb_get(Handle, (options ?? _defaultReadOptions).Handle,
                k, (nuint)key.Length, out vallen, ref err);
        NativeMethods.ThrowOnError(err);
        if (valPtr == null) return null;

        byte[] result = new ReadOnlySpan<byte>(valPtr, checked((int)vallen)).ToArray();
        NativeMethods.rocksdb_free((nint)valPtr);
        return result;
    }

    /// <summary>Returns the value for <paramref name="key"/> in <paramref name="cf"/>, or <c>null</c>.</summary>
    public unsafe byte[]? Get(ReadOnlySpan<byte> key, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        nint err = default;
        nuint vallen;
        byte* valPtr;
        fixed (byte* k = key)
            valPtr = NativeMethods.rocksdb_get_cf(Handle, (options ?? _defaultReadOptions).Handle, cf.Handle,
                k, (nuint)key.Length, out vallen, ref err);
        NativeMethods.ThrowOnError(err);
        if (valPtr == null) return null;

        byte[] result = new ReadOnlySpan<byte>(valPtr, checked((int)vallen)).ToArray();
        NativeMethods.rocksdb_free((nint)valPtr);
        return result;
    }

    /// <summary>Convenience overload using a UTF-8 string key; returns the value as a string or <c>null</c>.</summary>
    public string? GetString(string key, ReadOptions? options = null)
    {
        byte[]? val = GetInternal(Encoding.UTF8.GetBytes(key), options);
        return val == null ? null : Encoding.UTF8.GetString(val);
    }

    /// <summary>Convenience overload using a UTF-8 string key in a column family.</summary>
    public string? GetString(string key, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        byte[]? val = Get(Encoding.UTF8.GetBytes(key), cf, options);
        return val == null ? null : Encoding.UTF8.GetString(val);
    }

    /// <summary>
    /// Tries to retrieve <paramref name="key"/>. Returns <c>true</c> and sets
    /// <paramref name="value"/> if the key exists; otherwise returns <c>false</c>.
    /// </summary>
    public bool TryGet(ReadOnlySpan<byte> key, out byte[]? value, ReadOptions? options = null)
    {
        value = GetInternal(key, options);
        return value != null;
    }

    /// <summary>Returns the value for a string key, or <c>null</c> if not found.</summary>
    public byte[]? Get(string key, ReadOptions? options = null)
        => GetInternal(Encoding.UTF8.GetBytes(key), options);

    // ─────────────────────────────────────────────────────────────────────────
    // MultiGet
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves multiple keys in a single call from the default column family.
    /// Returns one result per key; results are <c>null</c> for keys that do not exist.
    /// Throws <see cref="RocksDbException"/> on the first key-level error.
    /// </summary>
    public unsafe byte[]?[] MultiGet(IReadOnlyList<byte[]> keys, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(keys);

        int n = keys.Count;
        if (n == 0) return Array.Empty<byte[]?>();

        // Stack-allocate pointer arrays for small batches to avoid heap pressure.
        byte*[] keyPtrs  = new byte*[n];
        nuint[] keySizes = new nuint[n];
        byte*[] valPtrs  = new byte*[n];
        nuint[] valSizes = new nuint[n];
        nint[]  errs     = new nint[n];

        // Pin all key arrays and populate pointer arrays.
        var handles = new GCHandle[n];
        try
        {
            for (int i = 0; i < n; i++)
            {
                handles[i] = GCHandle.Alloc(keys[i], GCHandleType.Pinned);
                keyPtrs[i] = (byte*)handles[i].AddrOfPinnedObject();
                keySizes[i] = (nuint)keys[i].Length;
            }

            fixed (byte** kp = keyPtrs)
            fixed (nuint* ks = keySizes)
            fixed (byte** vp = valPtrs)
            fixed (nuint* vs = valSizes)
            fixed (nint*  ep = errs)
                NativeMethods.rocksdb_multi_get(Handle, (options ?? _defaultReadOptions).Handle,
                    (nuint)n, kp, ks, vp, vs, ep);
        }
        finally
        {
            for (int i = 0; i < n; i++)
                if (handles[i].IsAllocated)
                    handles[i].Free();
        }

        var results = new byte[]?[n];
        for (int i = 0; i < n; i++)
        {
            if (errs[i] != nint.Zero)
            {
                NativeMethods.ThrowOnError(errs[i]);
            }
            else if (valPtrs[i] != null)
            {
                results[i] = new ReadOnlySpan<byte>(valPtrs[i], checked((int)valSizes[i])).ToArray();
                NativeMethods.rocksdb_free((nint)valPtrs[i]);
            }
        }
        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Key existence check
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the key <em>may</em> exist (Bloom-filter optimized).
    /// A <c>false</c> result guarantees the key is absent; a <c>true</c> result
    /// requires a real Get to confirm existence.
    /// </summary>
    public unsafe bool KeyMayExist(ReadOnlySpan<byte> key, ReadOptions? options = null)
    {
        fixed (byte* k = key)
            return NativeMethods.rocksdb_key_may_exist(Handle, (options ?? _defaultReadOptions).Handle,
                k, (nuint)key.Length, (byte**)null, (nuint*)null, (byte*)null, 0, (byte*)null) != 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Iterator
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a new iterator over the default column family.</summary>
    public Iterator NewIterator(ReadOptions? options = null)
    {
        nint handle = NativeMethods.rocksdb_create_iterator(
            Handle, (options ?? _defaultReadOptions).Handle);
        return Iterator.FromHandle(handle);
    }

    /// <summary>Creates a new iterator over <paramref name="cf"/>.</summary>
    public Iterator NewIterator(ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        nint handle = NativeMethods.rocksdb_create_iterator_cf(
            Handle, (options ?? _defaultReadOptions).Handle, cf.Handle);
        return Iterator.FromHandle(handle);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Snapshot
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an immutable snapshot of the current DB state.
    /// The snapshot must be disposed before the database is closed.
    /// </summary>
    public Snapshot NewSnapshot()
    {
        nint handle = NativeMethods.rocksdb_create_snapshot(Handle);
        return new Snapshot(handle, this);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Column families
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a new column family and returns a handle to it.</summary>
    public ColumnFamilyHandle CreateColumnFamily(DbOptions options, string name)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(name);

        nint err = default;
        nint handle = NativeMethods.rocksdb_create_column_family(Handle, options.Handle, name, ref err);
        NativeMethods.ThrowOnError(err);
        return new ColumnFamilyHandle(handle);
    }

    /// <summary>Creates a new column family with TTL.</summary>
    public ColumnFamilyHandle CreateColumnFamilyWithTtl(DbOptions options, string name, int ttlSeconds)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(name);

        nint err = default;
        nint handle = NativeMethods.rocksdb_create_column_family_with_ttl(Handle, options.Handle, name, ttlSeconds, ref err);
        NativeMethods.ThrowOnError(err);
        return new ColumnFamilyHandle(handle);
    }

    /// <summary>Drops <paramref name="cf"/> from the database. The handle is invalidated after this call.</summary>
    public void DropColumnFamily(ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        NativeMethods.rocksdb_drop_column_family(Handle, cf.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Returns a non-owning wrapper around the default column family handle.
    /// Do <em>not</em> call Dispose on the
    /// returned handle — its lifetime is managed by the database.
    /// </summary>
    public ColumnFamilyHandle GetDefaultColumnFamily()
    {
        nint h = NativeMethods.rocksdb_get_default_column_family_handle(Handle);
        return new ColumnFamilyHandle(h, owned: false);
    }

    public ColumnFamilyHandle GetColumnFamily(string name)
    {
        if (_columnFamilyHandles != null && _columnFamilyHandles.TryGetValue(name, out var cfh))
        {
            return cfh;
        }
        return null!;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Flush / Compact
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Flushes all memtables for all column families to storage.</summary>
    public void Flush(FlushOptions? options = null)
    {
        nint err = default;
        NativeMethods.rocksdb_flush(Handle, (options ?? _defaultFlushOptions).Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Flushes the specified column family.</summary>
    public void Flush(ColumnFamilyHandle cf, FlushOptions? options = null)
    {
        nint err = default;
        NativeMethods.rocksdb_flush_cf(Handle, (options ?? _defaultFlushOptions).Handle, cf.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Flushes the WAL buffer to disk.</summary>
    public void FlushWal(bool sync = false)
    {
        nint err = default;
        NativeMethods.rocksdb_flush_wal(Handle, sync ? (byte)1 : (byte)0, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Triggers compaction on the key range [<paramref name="startKey"/>, <paramref name="limitKey"/>).</summary>
    public unsafe void CompactRange(ReadOnlySpan<byte> startKey = default, ReadOnlySpan<byte> limitKey = default)
    {
        fixed (byte* s = startKey)
        fixed (byte* e = limitKey)
            NativeMethods.rocksdb_compact_range(Handle,
                startKey.IsEmpty ? null : s, (nuint)startKey.Length,
                limitKey.IsEmpty ? null : e, (nuint)limitKey.Length);
    }

    /// <summary>Triggers compaction on a specific column family.</summary>
    public unsafe void CompactRange(ColumnFamilyHandle cf,
        ReadOnlySpan<byte> startKey = default, ReadOnlySpan<byte> limitKey = default)
    {
        fixed (byte* s = startKey)
        fixed (byte* e = limitKey)
            NativeMethods.rocksdb_compact_range_cf(Handle, cf.Handle,
                startKey.IsEmpty ? null : s, (nuint)startKey.Length,
                limitKey.IsEmpty ? null : e, (nuint)limitKey.Length);
    }

    /// <summary>Compacts the entire key-space using specified options.</summary>
    public unsafe void CompactRange(CompactRangeOptions options,
        ReadOnlySpan<byte> startKey = default, ReadOnlySpan<byte> limitKey = default)
    {
        fixed (byte* s = startKey)
        fixed (byte* e = limitKey)
            NativeMethods.rocksdb_compact_range_opt(Handle, options.Handle,
                startKey.IsEmpty ? null : s, (nuint)startKey.Length,
                limitKey.IsEmpty ? null : e, (nuint)limitKey.Length);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the value of an internal property (e.g. <c>"rocksdb.stats"</c>),
    /// or <c>null</c> if the property is unknown.
    /// </summary>
    public string? GetProperty(string propName)
    {
        nint ptr = NativeMethods.rocksdb_property_value(Handle, propName);
        if (ptr == nint.Zero) return null;

        string? result = Marshal.PtrToStringUTF8(ptr);
        NativeMethods.rocksdb_free(ptr);
        return result;
    }

    /// <summary>Returns an integer property value, or <c>null</c> if unavailable.</summary>
    public ulong? GetPropertyInt(string propName)
    {
        int rc = NativeMethods.rocksdb_property_int(Handle, propName, out ulong val);
        return rc == 0 ? val : null;
    }

    /// <summary>Returns a string property for a specific column family.</summary>
    public string? GetProperty(string propName, ColumnFamilyHandle cf)
    {
        nint ptr = NativeMethods.rocksdb_property_value_cf(Handle, cf.Handle, propName);
        if (ptr == nint.Zero) return null;

        string? result = Marshal.PtrToStringUTF8(ptr);
        NativeMethods.rocksdb_free(ptr);
        return result;
    }

    /// <summary>Returns an integer property for a specific column family.</summary>
    public ulong? GetPropertyInt(string propName, ColumnFamilyHandle cf)
    {
        int rc = NativeMethods.rocksdb_property_int_cf(Handle, cf.Handle, propName, out ulong val);
        return rc == 0 ? val : null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Misc
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Returns the latest sequence number (write counter) for the database.</summary>
    public ulong LatestSequenceNumber
    {
        get
        {
            return NativeMethods.rocksdb_get_latest_sequence_number(Handle);
        }
    }

    /// <summary>Returns the unique identity of this database instance.</summary>
    public unsafe string GetDbIdentity()
    {
        nint ptr = NativeMethods.rocksdb_get_db_identity(Handle, out nuint len);
        if (ptr == nint.Zero) return string.Empty;

        string id = NativeMethods.PtrToStringUTF8((byte*)ptr, len) ?? string.Empty;
        NativeMethods.rocksdb_free(ptr);
        return id;
    }

    /// <summary>
    /// Disables file deletions. Call <see cref="EnableFileDeletions"/> to re-enable.
    /// </summary>
    public void DisableFileDeletions()
    {
        nint err = default;
        NativeMethods.rocksdb_disable_file_deletions(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // External SST file ingestion
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ingests a list of pre-built SST files into the default column family.
    /// </summary>
    public void IngestExternalFile(IReadOnlyList<string> filePaths, IngestExternalFileOptions options)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(options);

        string[] paths = [.. filePaths];
        nint err = default;
        NativeMethods.rocksdb_ingest_external_file(Handle, paths, (nuint)paths.Length, options.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Ingests a list of pre-built SST files into <paramref name="cf"/>.
    /// </summary>
    public void IngestExternalFile(IReadOnlyList<string> filePaths, ColumnFamilyHandle cf, IngestExternalFileOptions options)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(cf);
        ArgumentNullException.ThrowIfNull(options);

        string[] paths = [.. filePaths];
        nint err = default;
        NativeMethods.rocksdb_ingest_external_file_cf(Handle, cf.Handle, paths, (nuint)paths.Length, options.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Re-enables file deletions after a previous <see cref="DisableFileDeletions"/> call.</summary>
    public void EnableFileDeletions()
    {
        nint err = default;
        NativeMethods.rocksdb_enable_file_deletions(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// For secondary instances: catches up with the primary by reading from the WAL.
    /// </summary>
    public void TryCatchUpWithPrimary()
    {
        nint err = default;
        NativeMethods.rocksdb_try_catch_up_with_primary(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    public override void DisposeUnmanagedResources()
    {
        if (_columnFamilyHandles != null)
        {
            foreach (var cfh in _columnFamilyHandles.Values)
            {
                cfh.Dispose();
            }
        }

        NativeMethods.rocksdb_close(Handle);
    }
}
