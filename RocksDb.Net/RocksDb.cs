using System.Runtime.InteropServices;
using System.Text;

namespace RocksDbNet;

/// <summary>
/// A RocksDb embedded key-value database.
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
    private readonly DbOptions _ownedOptions;

    private RocksDb(nint handle, DbOptions options)
        : base(handle)
    {
        _ownedOptions = options;
    }

    private RocksDb(nint handle, nint[] cfHandles, DbOptions options)
        : base(handle)
    {
        _ownedOptions = options;
        _columnFamilyHandles = [];
        foreach (var cf in cfHandles)
        {
            ColumnFamilyHandle cfh = new(cf);
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

        return new RocksDb(handle, options);
    }

    /// <summary>
    /// Opens the database with an explicit set of column families.
    /// The <c>"default"</c> column family must always be included.
    /// Returns the database and one <see cref="ColumnFamilyHandle"/> per descriptor.
    /// </summary>
    public static unsafe RocksDb Open(DbOptions options, string path, IReadOnlyList<ColumnFamilyDescriptor> columnFamilies)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(columnFamilies);

        int count = columnFamilies.Count;
        nint[] cfHandles = new nint[count];
        nint[] cfOptions = [.. columnFamilies.Select(cf => cf.Options.Handle)];
        byte[][] cfNameBytes = [.. columnFamilies.Select(cf => Encoding.UTF8.GetBytes(cf.Name + '\0'))];

        nint handle;
        nint err = default;
        var pins = new GCHandle[count];
        var namePtrs = new byte*[count];
        try
        {
            for (int i = 0; i < count; i++)
            {
                pins[i] = GCHandle.Alloc(cfNameBytes[i], GCHandleType.Pinned);
                namePtrs[i] = (byte*)pins[i].AddrOfPinnedObject();
            }

            fixed (byte** namesPtr = namePtrs)
            fixed (nint* optsPtr = cfOptions)
            fixed (nint* handlesPtr = cfHandles)
                handle = NativeMethods.rocksdb_open_column_families(
                    options.Handle, path, count,
                    namesPtr, (nint)optsPtr, handlesPtr, ref err);
        }
        finally
        {
            for (int i = 0; i < count; i++)
                if (pins[i].IsAllocated) pins[i].Free();
        }
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle, cfHandles, options);
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

        return new RocksDb(handle, options);
    }

    /// <summary>Opens an existing database in read-only mode.</summary>
    public static unsafe RocksDb OpenReadOnly(DbOptions options, string path, IReadOnlyList<ColumnFamilyDescriptor> columnFamilies, bool errorIfWalExists = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(columnFamilies);

        int count = columnFamilies.Count;
        nint[] cfHandles = new nint[count];
        nint[] cfOptions = [.. columnFamilies.Select(cf => cf.Options.Handle)];
        byte[][] cfNameBytes = [.. columnFamilies.Select(cf => Encoding.UTF8.GetBytes(cf.Name + '\0'))];

        nint handle;
        nint err = default;
        var pins = new GCHandle[count];
        var namePtrs = new byte*[count];
        try
        {
            for (int i = 0; i < count; i++)
            {
                pins[i] = GCHandle.Alloc(cfNameBytes[i], GCHandleType.Pinned);
                namePtrs[i] = (byte*)pins[i].AddrOfPinnedObject();
            }

            fixed (byte** namesPtr = namePtrs)
            fixed (nint* optsPtr = cfOptions)
            fixed (nint* handlesPtr = cfHandles)
                handle = NativeMethods.rocksdb_open_for_read_only_column_families(
                    options.Handle, path, count,
                    namesPtr, (nint)optsPtr, handlesPtr,
                    errorIfWalExists ? (byte)1 : (byte)0, ref err);
        }
        finally
        {
            for (int i = 0; i < count; i++)
                if (pins[i].IsAllocated) pins[i].Free();
        }
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle, cfHandles, options);
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

        return new RocksDb(handle, options);
    }

    /// <summary>Opens the database with a TTL (time-to-live) compaction filter.</summary>
    public static RocksDb OpenWithTtl(DbOptions options, string path, int ttlSeconds)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        nint handle = NativeMethods.rocksdb_open_with_ttl(options.Handle, path, ttlSeconds, ref err);
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle, options);
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
        nuint count;
        byte** list = NativeMethods.rocksdb_list_column_families(options.Handle, path, &count, ref err);
        NativeMethods.ThrowOnError(err);

        var result = new string[(int)count];
        for (int i = 0; i < (int)count; i++)
            result[i] = Marshal.PtrToStringUTF8((nint)list[i]) ?? string.Empty;

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

    /// <summary>
    /// Deletes all keys in the range [<paramref name="startKey"/>, <paramref name="endKey"/>)
    /// from the default column family.
    /// </summary>
    /// <remarks>
    /// The <c>DeleteRange</c> API is more efficient than issuing individual deletes for each key in the range,
    /// but it does not immediately remove the keys from storage. Instead, it adds a range tombstone that marks
    /// the keys as deleted. The actual removal of the keys happens during compaction, so the space is not reclaimed
    /// until then. Also, range tombstones can affect read performance for keys in the deleted range until compaction occurs.
    /// Use <c>DeleteRange</c> when you need to delete large contiguous ranges of keys and can tolerate the delayed cleanup and potential read performance impact.
    /// </remarks>
    public unsafe void DeleteRange(ReadOnlySpan<byte> startKey, ReadOnlySpan<byte> endKey, WriteOptions? options = null)
        => DeleteRange(startKey, endKey, GetDefaultColumnFamily(), options);

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

    /// <summary>Applies a merge operation to <paramref name="key"/> in the default column family.</summary>
    public void Merge(string key, string value, WriteOptions? options = null)
        => Merge(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), options);

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

    /// <summary>Applies a merge operation to <paramref name="key"/> in <paramref name="cf"/>.</summary>
    public void Merge(string key, string value, ColumnFamilyHandle cf, WriteOptions? options = null)
        => Merge(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), cf, options);


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
        nint valNint;
        nuint vallen;
        fixed (byte* k = key)
        {
            valNint = NativeMethods.rocksdb_get(Handle, (options ?? _defaultReadOptions).Handle,
                k, (nuint)key.Length, out vallen, ref err);
        }
        NativeMethods.ThrowOnError(err);
        if (valNint == nint.Zero) return null;

        byte* valPtr = (byte*)valNint;
        byte[] result = new ReadOnlySpan<byte>(valPtr, checked((int)vallen)).ToArray();
        NativeMethods.rocksdb_free(valNint);
        return result;
    }

    /// <summary>Returns the value for <paramref name="key"/> in <paramref name="cf"/>, or <c>null</c>.</summary>
    public unsafe byte[]? Get(ReadOnlySpan<byte> key, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        nint err = default;
        nuint vallen;
        nint valNint;
        fixed (byte* k = key)
            valNint = NativeMethods.rocksdb_get_cf(Handle, (options ?? _defaultReadOptions).Handle, cf.Handle,
                k, (nuint)key.Length, out vallen, ref err);
        NativeMethods.ThrowOnError(err);
        if (valNint == nint.Zero) return null;

        byte* valPtr = (byte*)valNint;
        byte[] result = new ReadOnlySpan<byte>(valPtr, checked((int)vallen)).ToArray();
        NativeMethods.rocksdb_free(valNint);
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
        if (n == 0)
        {
            return [];
        }

        // Stack-allocate pointer arrays for small batches to avoid heap pressure.
        byte*[] keyPtrs = new byte*[n];
        nuint[] keySizes = new nuint[n];
        byte*[] valPtrs = new byte*[n];
        nuint[] valSizes = new nuint[n];
        nint[] errs = new nint[n];

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
            fixed (nint* ep = errs)
                NativeMethods.rocksdb_multi_get(Handle, (options ?? _defaultReadOptions).Handle,
                    (nuint)n, kp, ks, vp, vs, (byte**)ep);
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
                k, (nuint)key.Length, (byte**)null, out nuint dummyValLen, (byte*)null, 0, (byte*)null) != 0;
    }

    /// <summary>
    /// Returns <c>true</c> if the key <em>may</em> exist in <paramref name="cf"/>
    /// (Bloom-filter optimized). A <c>false</c> result guarantees the key is absent;
    /// a <c>true</c> result requires a real Get to confirm existence.
    /// </summary>
    public unsafe bool KeyMayExist(ReadOnlySpan<byte> key, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        fixed (byte* k = key)
            return NativeMethods.rocksdb_key_may_exist_cf(Handle, (options ?? _defaultReadOptions).Handle,
                cf.Handle, k, (nuint)key.Length, (byte**)null, out nuint dummyValLen,
                (byte*)null, 0, (byte*)null) != 0;
    }

    /// <summary>
    /// Returns <c>true</c> if the UTF-8 encoded key <em>may</em> exist.
    /// </summary>
    public bool KeyMayExist(string key, ReadOptions? options = null)
        => KeyMayExist(Encoding.UTF8.GetBytes(key), options);

    /// <summary>
    /// Returns <c>true</c> if the UTF-8 encoded key <em>may</em> exist in <paramref name="cf"/>.
    /// </summary>
    public bool KeyMayExist(string key, ColumnFamilyHandle cf, ReadOptions? options = null)
        => KeyMayExist(Encoding.UTF8.GetBytes(key), cf, options);

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
        ColumnFamilyHandle cf = new ColumnFamilyHandle(h);
        cf.TransferOwnership(); // Prevent double-free since the DB owns this handle.
        return cf;
    }

    /// <summary>Returns the column family handle for <paramref name="name"/> opened at database creation.</summary>
    public ColumnFamilyHandle GetColumnFamily(string name)
    {
        return _columnFamilyHandles != null && _columnFamilyHandles.TryGetValue(name, out ColumnFamilyHandle? cfh) ? cfh : null!;
    }

    /// <summary>Returns metadata for the default column family.</summary>
    public ColumnFamilyMetadata? GetColumnFamilyMetadata()
    {
        nint meta = NativeMethods.rocksdb_get_column_family_metadata(Handle);
        return meta == nint.Zero ? null : new ColumnFamilyMetadata(meta);
    }

    /// <summary>Returns metadata for <paramref name="cf"/>.</summary>
    public ColumnFamilyMetadata? GetColumnFamilyMetadata(ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        nint meta = NativeMethods.rocksdb_get_column_family_metadata_cf(Handle, cf.Handle);
        return meta == nint.Zero ? null : new ColumnFamilyMetadata(meta);
    }

    /// <summary>
    /// Returns metadata for the default column family, restricted to the level
    /// and key range in <paramref name="options"/>.
    /// </summary>
    public ColumnFamilyMetadata? GetColumnFamilyMetadata(ColumnFamilyMetadataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        nint meta = NativeMethods.rocksdb_get_column_family_metadata_with_options(Handle, options.Handle);
        return meta == nint.Zero ? null : new ColumnFamilyMetadata(meta);
    }

    /// <summary>
    /// Returns metadata for <paramref name="cf"/>, restricted to the level and
    /// key range in <paramref name="options"/>.
    /// </summary>
    public ColumnFamilyMetadata? GetColumnFamilyMetadata(ColumnFamilyHandle cf, ColumnFamilyMetadataOptions options)
    {
        ArgumentNullException.ThrowIfNull(cf);
        ArgumentNullException.ThrowIfNull(options);
        nint meta = NativeMethods.rocksdb_get_column_family_metadata_cf_with_options(Handle, cf.Handle, options.Handle);
        return meta == nint.Zero ? null : new ColumnFamilyMetadata(meta);
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

    /// <summary>Flushes the specified column families.</summary>
    public unsafe void Flush(IReadOnlyList<ColumnFamilyHandle> columnFamilies, FlushOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(columnFamilies);
        if (columnFamilies.Count == 0)
        {
            Flush(options);
            return;
        }

        int count = columnFamilies.Count;
        nint[] handles = new nint[count];
        for (int i = 0; i < count; i++)
        {
            if (columnFamilies[i] is null)
                throw new ArgumentException("Column family handles cannot be null.", nameof(columnFamilies));
            handles[i] = columnFamilies[i].Handle;
        }

        nint err = default;
        unsafe
        {
            fixed (nint* ptr = handles)
                NativeMethods.rocksdb_flush_cfs(Handle, (options ?? _defaultFlushOptions).Handle, ptr, count, ref err);
        }
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Flushes the WAL buffer to disk.</summary>
    public void FlushWal(bool sync = false)
    {
        nint err = default;
        NativeMethods.rocksdb_flush_wal(Handle, sync ? (byte)1 : (byte)0, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Flushes the WAL buffer to disk, with control over the rate limiter
    /// priority as well as syncing.
    /// </summary>
    public void FlushWal(FlushWalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        nint err = default;
        NativeMethods.rocksdb_flush_wal_with_options(Handle, options.Handle, ref err);
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

    /// <summary>Suggests compaction for the specified key range.</summary>
    public unsafe void SuggestCompactRange(ReadOnlySpan<byte> startKey, ReadOnlySpan<byte> limitKey)
    {
        nint err = default;
        fixed (byte* s = startKey)
        fixed (byte* e = limitKey)
            NativeMethods.rocksdb_suggest_compact_range(Handle,
                startKey.IsEmpty ? null : s, (nuint)startKey.Length,
                limitKey.IsEmpty ? null : e, (nuint)limitKey.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Suggests compaction for the specified column family and key range.</summary>
    public unsafe void SuggestCompactRange(ColumnFamilyHandle cf, ReadOnlySpan<byte> startKey, ReadOnlySpan<byte> limitKey)
    {
        ArgumentNullException.ThrowIfNull(cf);
        nint err = default;
        fixed (byte* s = startKey)
        fixed (byte* e = limitKey)
            NativeMethods.rocksdb_suggest_compact_range_cf(Handle, cf.Handle,
                startKey.IsEmpty ? null : s, (nuint)startKey.Length,
                limitKey.IsEmpty ? null : e, (nuint)limitKey.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Cancels or waits for all background work.</summary>
    public void CancelAllBackgroundWork(bool wait = false)
    {
        NativeMethods.rocksdb_cancel_all_background_work(Handle, wait ? (byte)1 : (byte)0);
    }

    /// <summary>Waits for pending compaction work, optionally using custom options.</summary>
    public void WaitForCompact(WaitForCompactOptions? options = null)
    {
        nint err = default;
        using var compactOptions = options ?? new WaitForCompactOptions();
        NativeMethods.rocksdb_wait_for_compact(Handle, compactOptions.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // Pinning a key/value array pair for the native set-options calls is the
    // same 30 lines three times over, so it lives in one place. The delegate
    // takes the pinned pointers as parameters rather than closing over them,
    // since a lambda cannot capture a pointer local.
    private unsafe delegate void SetOptionsCall(int count, byte** keys, byte** values, ref nint errptr);

    private unsafe void ApplyOptions(IEnumerable<KeyValuePair<string, string>> options, SetOptionsCall call)
    {
        ArgumentNullException.ThrowIfNull(options);

        var entries = options.ToList();
        if (entries.Count == 0)
            return;

        var keys = new byte*[entries.Count];
        var values = new byte*[entries.Count];
        var keyPins = new GCHandle[entries.Count];
        var valuePins = new GCHandle[entries.Count];

        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                byte[] keyBytes = Encoding.UTF8.GetBytes(entries[i].Key + '\0');
                byte[] valueBytes = Encoding.UTF8.GetBytes(entries[i].Value + '\0');
                keyPins[i] = GCHandle.Alloc(keyBytes, GCHandleType.Pinned);
                valuePins[i] = GCHandle.Alloc(valueBytes, GCHandleType.Pinned);
                keys[i] = (byte*)keyPins[i].AddrOfPinnedObject();
                values[i] = (byte*)valuePins[i].AddrOfPinnedObject();
            }

            nint err = default;
            fixed (byte** k = keys)
            fixed (byte** v = values)
                call(entries.Count, k, v, ref err);
            NativeMethods.ThrowOnError(err);
        }
        finally
        {
            for (int i = 0; i < keyPins.Length; i++)
            {
                if (keyPins[i].IsAllocated) keyPins[i].Free();
                if (valuePins[i].IsAllocated) valuePins[i].Free();
            }
        }
    }

    /// <summary>Applies one or more runtime options to the database.</summary>
    public unsafe void SetOptions(IEnumerable<KeyValuePair<string, string>> options)
        => ApplyOptions(options, (int count, byte** keys, byte** values, ref nint err)
            => NativeMethods.rocksdb_set_options(Handle, count, keys, values, ref err));

    /// <summary>Applies one or more runtime options to a specific column family.</summary>
    public unsafe void SetOptions(ColumnFamilyHandle cf, IEnumerable<KeyValuePair<string, string>> options)
    {
        ArgumentNullException.ThrowIfNull(cf);

        ApplyOptions(options, (int count, byte** keys, byte** values, ref nint err)
            => NativeMethods.rocksdb_set_options_cf(Handle, cf.Handle, count, keys, values, ref err));
    }

    /// <summary>
    /// Applies one or more database-wide runtime options.
    /// </summary>
    /// <remarks>
    /// This is the counterpart to <see cref="SetOptions(IEnumerable{KeyValuePair{string, string}})"/>
    /// for options that live on the database rather than on a column family, and
    /// it is the only way to change them after the database is open. Most
    /// <see cref="DbOptions"/> values are read once at open time and ignored
    /// afterwards.
    /// </remarks>
    public unsafe void SetDbOptions(IEnumerable<KeyValuePair<string, string>> options)
        => ApplyOptions(options, (int count, byte** keys, byte** values, ref nint err)
            => NativeMethods.rocksdb_set_db_options(Handle, count, keys, values, ref err));

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
    public unsafe ulong? GetPropertyInt(string propName)
    {
        ulong val;
        int rc = NativeMethods.rocksdb_property_int(Handle, propName, &val);
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
    public unsafe ulong? GetPropertyInt(string propName, ColumnFamilyHandle cf)
    {
        ulong val;
        int rc = NativeMethods.rocksdb_property_int_cf(Handle, cf.Handle, propName, &val);
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

    /// <summary>Returns <c>true</c> when the database has no known keys.</summary>
    public bool IsEmpty => GetPropertyInt("rocksdb.estimate-num-keys").GetValueOrDefault() == 0;

    /// <summary>Returns the unique identity of this database instance.</summary>
    public unsafe string GetDbIdentity()
    {
        nint ptr = NativeMethods.rocksdb_get_db_identity(Handle, out nuint len);
        if (ptr == nint.Zero) return string.Empty;

        string id = NativeMethods.PtrToStringUTF8((byte*)ptr, len) ?? string.Empty;
        NativeMethods.rocksdb_free(ptr);
        return id;
    }

    /// <summary>Returns metadata about the currently live SST files in the database.</summary>
    public LiveFiles? GetLiveFiles()
    {
        nint liveFilesHandle = NativeMethods.rocksdb_livefiles(Handle);
        return liveFilesHandle == nint.Zero ? null : new LiveFiles(liveFilesHandle);
    }

    /// <summary>
    /// Returns a consistent snapshot of every live file, described in enough
    /// detail to copy the database elsewhere.
    /// </summary>
    /// <param name="options">
    /// Settings for the call, or <c>null</c> for RocksDb's defaults.
    /// </param>
    /// <remarks>
    /// More useful than <see cref="GetLiveFiles"/> for building a backup by hand:
    /// each entry carries the target filename, the live byte count, the storage
    /// temperature, optionally a checksum, and for small metadata files the
    /// content to write rather than copy.
    /// <para>
    /// This flushes memtables by default, because
    /// <see cref="LiveFilesStorageInfoOptions.WalSizeForFlush"/> defaults to 0,
    /// meaning "always flush". Raise it to avoid that.
    /// </para>
    /// </remarks>
    public unsafe IReadOnlyList<LiveFileStorageInfo> GetLiveFilesStorageInfo(LiveFilesStorageInfoOptions? options = null)
    {
        LiveFilesStorageInfoOptions effective = options ?? new LiveFilesStorageInfoOptions();
        try
        {
            nint err = default;
            nint info = NativeMethods.rocksdb_get_livefiles_storage_info(Handle, effective.Handle, ref err);
            NativeMethods.ThrowOnError(err);

            if (info == nint.Zero)
            {
                return [];
            }

            try
            {
                nuint count = NativeMethods.rocksdb_livefiles_storage_info_count(info);
                var results = new List<LiveFileStorageInfo>(checked((int)count));

                for (nuint i = 0; i < count; i++)
                {
                    byte* checksum = NativeMethods.rocksdb_livefiles_storage_info_file_checksum(info, i);
                    byte* replacement = NativeMethods.rocksdb_livefiles_storage_info_replacement_contents(info, i, out nuint replacementLen);

                    results.Add(new LiveFileStorageInfo
                    {
                        RelativeFilename = Marshal.PtrToStringUTF8((nint)NativeMethods.rocksdb_livefiles_storage_info_relative_filename(info, i)),
                        Directory = Marshal.PtrToStringUTF8((nint)NativeMethods.rocksdb_livefiles_storage_info_directory(info, i)),
                        Size = NativeMethods.rocksdb_livefiles_storage_info_size(info, i),
                        FileType = (FileType)NativeMethods.rocksdb_livefiles_storage_info_file_type(info, i),
                        FileNumber = NativeMethods.rocksdb_livefiles_storage_info_file_number(info, i),
                        Temperature = (Temperature)NativeMethods.rocksdb_livefiles_storage_info_temperature(info, i),
                        TrimToSize = NativeMethods.rocksdb_livefiles_storage_info_trim_to_size(info, i) != 0,

                        // The checksum is a NUL-terminated string on the native
                        // side but holds raw bytes, so read it as such.
                        FileChecksum = checksum is null ? [] : ReadNulTerminatedBytes(checksum),
                        FileChecksumFuncName = Marshal.PtrToStringUTF8((nint)NativeMethods.rocksdb_livefiles_storage_info_file_checksum_func_name(info, i)),
                        ReplacementContents = replacement is null || replacementLen == 0
                            ? []
                            : new ReadOnlySpan<byte>(replacement, checked((int)replacementLen)).ToArray(),
                    });
                }

                return results;
            }
            finally
            {
                NativeMethods.rocksdb_livefiles_storage_info_destroy(info);
            }
        }
        finally
        {
            if (options is null)
            {
                effective.Dispose();
            }
        }
    }

    private static unsafe byte[] ReadNulTerminatedBytes(byte* ptr)
    {
        int length = 0;
        while (ptr[length] != 0)
        {
            length++;
        }

        return length == 0 ? [] : new ReadOnlySpan<byte>(ptr, length).ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Write-ahead log
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns every write-ahead log file RocksDb still retains, oldest first.
    /// </summary>
    /// <remarks>
    /// Includes both archived logs and the live one. The values are copied out,
    /// so nothing here has to be disposed and the result outlives the call.
    /// </remarks>
    public IReadOnlyList<WalFile> GetSortedWalFiles()
    {
        nint err = default;
        nint files = NativeMethods.rocksdb_get_sorted_wal_files(Handle, ref err);
        NativeMethods.ThrowOnError(err);

        if (files == nint.Zero)
        {
            return [];
        }

        try
        {
            nuint count = NativeMethods.rocksdb_wal_files_count(files);
            var results = new List<WalFile>(checked((int)count));

            for (nuint i = 0; i < count; i++)
            {
                // Each entry points into a vector owned by `files`, so it must
                // not be destroyed individually. Copying sidesteps that.
                if (WalFile.Copy(NativeMethods.rocksdb_wal_files_get_wal_file(files, i)) is { } file)
                {
                    results.Add(file);
                }
            }

            return results;
        }
        finally
        {
            NativeMethods.rocksdb_wal_files_destroy(files);
        }
    }

    /// <summary>
    /// Returns the write-ahead log file currently being written to.
    /// </summary>
    public WalFile? GetCurrentWalFile()
    {
        nint err = default;
        nint file = NativeMethods.rocksdb_get_current_wal_file(Handle, ref err);
        NativeMethods.ThrowOnError(err);

        if (file == nint.Zero)
        {
            return null;
        }

        try
        {
            // Unlike the entries from GetSortedWalFiles, this one is ours.
            return WalFile.Copy(file);
        }
        finally
        {
            NativeMethods.rocksdb_wal_file_destroy(file);
        }
    }

    /// <summary>
    /// Returns an iterator over write-ahead log records written at or after
    /// <paramref name="sequenceNumber"/>.
    /// </summary>
    /// <param name="sequenceNumber">
    /// Where to start. 0 means the oldest record still retained.
    /// </param>
    /// <param name="options">
    /// Read settings, or <c>null</c> for RocksDb's defaults.
    /// </param>
    /// <remarks>
    /// The basis for replication and change-data-capture: each step yields the
    /// batch that was written and the sequence number it started at. Only
    /// records still in the WAL are visible, so a sequence number older than the
    /// oldest retained log fails rather than returning nothing.
    /// </remarks>
    /// <exception cref="RocksDbException">
    /// The requested sequence number is no longer available, or reading the log
    /// failed.
    /// </exception>
    public WalIterator GetUpdatesSince(ulong sequenceNumber, WalReadOptions? options = null)
    {
        nint err = default;
        nint iter = NativeMethods.rocksdb_get_updates_since(
            Handle, sequenceNumber, options?.Handle ?? nint.Zero, ref err);
        NativeMethods.ThrowOnError(err);

        return new WalIterator(iter);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Background work
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stops RocksDb starting new flushes and compactions, and waits for the
    /// ones already running to finish.
    /// </summary>
    /// <remarks>
    /// Writes continue to be accepted, so pausing for long enough will build up
    /// memtables and eventually stall the writer. Calls nest: each
    /// <see cref="PauseBackgroundWork"/> needs a matching
    /// <see cref="ContinueBackgroundWork"/> before work resumes.
    /// </remarks>
    public void PauseBackgroundWork()
    {
        nint err = default;
        NativeMethods.rocksdb_pause_background_work(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Undoes one <see cref="PauseBackgroundWork"/>, letting flushes and
    /// compactions run again once every pause has been matched.
    /// </summary>
    public void ContinueBackgroundWork()
    {
        nint err = default;
        NativeMethods.rocksdb_continue_background_work(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CompactFiles
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compacts an explicit list of files into <paramref name="outputLevel"/>,
    /// and returns the names of the files produced.
    /// </summary>
    /// <param name="options">
    /// Compaction settings, or <c>null</c> for RocksDb's defaults.
    /// </param>
    /// <param name="inputFileNames">
    /// File names as reported by <see cref="GetLiveFiles"/>, or by the input and
    /// output lists on <see cref="CompactionJobInfo"/>.
    /// </param>
    /// <param name="outputLevel">The level to write the results into.</param>
    /// <param name="outputPathId">
    /// Index into the configured database paths, for a database spread over
    /// several. 0 for the usual single-path case.
    /// </param>
    /// <remarks>
    /// Unlike <see cref="CompactRange(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    /// this names the files itself, so the caller controls exactly what gets
    /// rewritten. RocksDb rejects a set of files it cannot legally compact
    /// together.
    /// </remarks>
    public string[] CompactFiles(
        CompactFilesOptions? options,
        IReadOnlyList<string> inputFileNames,
        int outputLevel,
        int outputPathId = 0)
        => CompactFilesCore(options, inputFileNames, outputLevel, outputPathId, cf: null, jobInfo: null);

    /// <summary>Compacts an explicit list of files in a specific column family.</summary>
    public string[] CompactFiles(
        ColumnFamilyHandle cf,
        CompactFilesOptions? options,
        IReadOnlyList<string> inputFileNames,
        int outputLevel,
        int outputPathId = 0)
    {
        ArgumentNullException.ThrowIfNull(cf);
        return CompactFilesCore(options, inputFileNames, outputLevel, outputPathId, cf, jobInfo: null);
    }

    /// <summary>
    /// Compacts an explicit list of files and also reports what the compaction
    /// did, through <paramref name="jobInfo"/>.
    /// </summary>
    /// <remarks>
    /// This is the only way to obtain a fully populated
    /// <see cref="CompactionJobInfo"/> synchronously. An
    /// <see cref="EventListener"/> gets the same information, but only when
    /// RocksDb happens to fire the event.
    /// </remarks>
    public string[] CompactFiles(
        CompactFilesOptions? options,
        IReadOnlyList<string> inputFileNames,
        int outputLevel,
        out CompactionJobInfo? jobInfo,
        int outputPathId = 0)
    {
        // The info object is ours to create and destroy; RocksDb only fills it in.
        nint infoHandle = NativeMethods.rocksdb_compactionjobinfo_create();
        try
        {
            string[] outputs = CompactFilesCore(options, inputFileNames, outputLevel, outputPathId, cf: null, infoHandle);
            jobInfo = EventListener.ReadCompactionJobInfo(infoHandle);
            return outputs;
        }
        finally
        {
            NativeMethods.rocksdb_compactionjobinfo_destroy(infoHandle);
        }
    }

    private unsafe string[] CompactFilesCore(
        CompactFilesOptions? options,
        IReadOnlyList<string> inputFileNames,
        int outputLevel,
        int outputPathId,
        ColumnFamilyHandle? cf,
        nint? jobInfo)
    {
        ArgumentNullException.ThrowIfNull(inputFileNames);
        if (inputFileNames.Count == 0)
        {
            throw new ArgumentException("At least one input file is required.", nameof(inputFileNames));
        }

        var namePtrs = new byte*[inputFileNames.Count];
        var pins = new GCHandle[inputFileNames.Count];

        try
        {
            for (int i = 0; i < inputFileNames.Count; i++)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(inputFileNames[i] + '\0');
                pins[i] = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                namePtrs[i] = (byte*)pins[i].AddrOfPinnedObject();
            }

            byte** outputNames = null;
            nuint outputCount = 0;
            nint err = default;

            fixed (byte** inputs = namePtrs)
            {
                if (cf is null)
                {
                    NativeMethods.rocksdb_compact_files(
                        Handle,
                        options?.Handle ?? nint.Zero,
                        inputs,
                        (nuint)inputFileNames.Count,
                        outputLevel,
                        outputPathId,
                        &outputNames,
                        &outputCount,
                        jobInfo ?? nint.Zero,
                        ref err);
                }
                else
                {
                    NativeMethods.rocksdb_compact_files_cf(
                        Handle,
                        cf.Handle,
                        options?.Handle ?? nint.Zero,
                        inputs,
                        (nuint)inputFileNames.Count,
                        outputLevel,
                        outputPathId,
                        &outputNames,
                        &outputCount,
                        jobInfo ?? nint.Zero,
                        ref err);
                }
            }

            NativeMethods.ThrowOnError(err);

            try
            {
                if (outputNames is null || outputCount == 0)
                {
                    return [];
                }

                var results = new string[checked((int)outputCount)];
                for (nuint i = 0; i < outputCount; i++)
                {
                    results[i] = Marshal.PtrToStringUTF8((nint)outputNames[i]) ?? string.Empty;
                }

                return results;
            }
            finally
            {
                // RocksDb allocated this array, so it has to free it.
                if (outputNames is not null)
                {
                    NativeMethods.rocksdb_compact_files_output_file_names_destroy(outputNames, outputCount);
                }
            }
        }
        finally
        {
            for (int i = 0; i < pins.Length; i++)
            {
                if (pins[i].IsAllocated) pins[i].Free();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Integrity checks
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads every live SST and blob file and verifies its block checksums.
    /// </summary>
    /// <remarks>
    /// This reads the whole database, so it is a maintenance operation rather
    /// than something to run on a request path. Use
    /// <see cref="VerifyFileChecksums()"/> for the cheaper whole-file check.
    /// </remarks>
    /// <exception cref="RocksDbException">A checksum did not match.</exception>
    public void VerifyChecksum()
    {
        nint err = default;
        NativeMethods.rocksdb_verify_checksum(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Verifies block checksums, using <paramref name="options"/> for the reads
    /// it performs.
    /// </summary>
    /// <exception cref="RocksDbException">A checksum did not match.</exception>
    public void VerifyChecksum(ReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        nint err = default;
        NativeMethods.rocksdb_verify_checksum_with_options(Handle, options.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Verifies each file's whole-file checksum against the checksum recorded
    /// for it in the manifest.
    /// </summary>
    /// <remarks>
    /// Cheaper than <see cref="VerifyChecksum()"/>, but it requires a file
    /// checksum generator to have been configured through
    /// <see cref="DbOptions.SetFileChecksumGenFactory"/>. Without one RocksDb
    /// has recorded nothing to compare against and fails the call rather than
    /// reporting success, so this is not a drop-in substitute.
    /// </remarks>
    /// <exception cref="RocksDbException">
    /// A checksum did not match, or no file checksum generator was configured.
    /// </exception>
    public void VerifyFileChecksums()
    {
        nint err = default;
        NativeMethods.rocksdb_verify_file_checksums(Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>
    /// Verifies whole-file checksums, using <paramref name="options"/> for the
    /// reads it performs.
    /// </summary>
    /// <exception cref="RocksDbException">A checksum did not match.</exception>
    public void VerifyFileChecksums(ReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        nint err = default;
        NativeMethods.rocksdb_verify_file_checksums_with_options(Handle, options.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    // Pinning the start and limit key arrays for the approximate-size calls is
    // the same 40 lines four times over, so it lives in one place. As with
    // ApplyOptions, the delegate receives the pinned pointers as parameters
    // because a lambda cannot capture a pointer local.
    private unsafe delegate void ApproximateSizesCall(
        int numRanges,
        byte** startKeys,
        nuint* startLengths,
        byte** limitKeys,
        nuint* limitLengths,
        ulong* sizes,
        ref nint errptr);

    private unsafe ulong[] ApproximateSizesCore(
        IEnumerable<(string Start, string Limit)> ranges,
        ApproximateSizesCall call)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        var rangeList = ranges.ToList();
        if (rangeList.Count == 0)
        {
            return [];
        }

        var startKeys = new byte*[rangeList.Count];
        var startLengths = new nuint[rangeList.Count];
        var limitKeys = new byte*[rangeList.Count];
        var limitLengths = new nuint[rangeList.Count];
        var sizes = new ulong[rangeList.Count];
        var startPins = new GCHandle[rangeList.Count];
        var limitPins = new GCHandle[rangeList.Count];

        try
        {
            for (int i = 0; i < rangeList.Count; i++)
            {
                var (startKey, limitKey) = rangeList[i];

                // A null pointer with zero length means "unbounded" on that side.
                if (!string.IsNullOrEmpty(startKey))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(startKey);
                    startPins[i] = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    startKeys[i] = (byte*)startPins[i].AddrOfPinnedObject();
                    startLengths[i] = (nuint)bytes.Length;
                }

                if (!string.IsNullOrEmpty(limitKey))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(limitKey);
                    limitPins[i] = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    limitKeys[i] = (byte*)limitPins[i].AddrOfPinnedObject();
                    limitLengths[i] = (nuint)bytes.Length;
                }
            }

            nint err = default;
            fixed (byte** startKeyPtr = startKeys)
            fixed (nuint* startLenPtr = startLengths)
            fixed (byte** limitKeyPtr = limitKeys)
            fixed (nuint* limitLenPtr = limitLengths)
            fixed (ulong* sizePtr = sizes)
            {
                call(rangeList.Count, startKeyPtr, startLenPtr, limitKeyPtr, limitLenPtr, sizePtr, ref err);
            }

            NativeMethods.ThrowOnError(err);
            return sizes;
        }
        finally
        {
            for (int i = 0; i < startPins.Length; i++)
            {
                if (startPins[i].IsAllocated) startPins[i].Free();
                if (limitPins[i].IsAllocated) limitPins[i].Free();
            }
        }
    }

    /// <summary>Returns approximate size information for one or more key ranges.</summary>
    public unsafe ulong[] ApproximateSizes(IEnumerable<(string Start, string Limit)> ranges)
        => ApproximateSizesCore(ranges,
            (int n, byte** sk, nuint* sl, byte** lk, nuint* ll, ulong* sizes, ref nint err)
                => NativeMethods.rocksdb_approximate_sizes(Handle, n, sk, sl, lk, ll, sizes, ref err));

    /// <summary>Returns approximate size information for one or more key ranges in a specific column family.</summary>
    public unsafe ulong[] ApproximateSizes(ColumnFamilyHandle cf, IEnumerable<(string Start, string Limit)> ranges)
    {
        ArgumentNullException.ThrowIfNull(cf);

        return ApproximateSizesCore(ranges,
            (int n, byte** sk, nuint* sl, byte** lk, nuint* ll, ulong* sizes, ref nint err)
                => NativeMethods.rocksdb_approximate_sizes_cf(Handle, cf.Handle, n, sk, sl, lk, ll, sizes, ref err));
    }

    /// <summary>
    /// Returns approximate size information for one or more key ranges, with
    /// control over what the estimate includes.
    /// </summary>
    public unsafe ulong[] ApproximateSizes(SizeApproximationOptions options, IEnumerable<(string Start, string Limit)> ranges)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ApproximateSizesCore(ranges,
            (int n, byte** sk, nuint* sl, byte** lk, nuint* ll, ulong* sizes, ref nint err)
                => NativeMethods.rocksdb_approximate_sizes_with_options(Handle, options.Handle, n, sk, sl, lk, ll, sizes, ref err));
    }

    /// <summary>
    /// Returns approximate size information for one or more key ranges in a
    /// specific column family, with control over what the estimate includes.
    /// </summary>
    public unsafe ulong[] ApproximateSizes(ColumnFamilyHandle cf, SizeApproximationOptions options, IEnumerable<(string Start, string Limit)> ranges)
    {
        ArgumentNullException.ThrowIfNull(cf);
        ArgumentNullException.ThrowIfNull(options);

        return ApproximateSizesCore(ranges,
            (int n, byte** sk, nuint* sl, byte** lk, nuint* ll, ulong* sizes, ref nint err)
                => NativeMethods.rocksdb_approximate_sizes_cf_with_options(Handle, cf.Handle, options.Handle, n, sk, sl, lk, ll, sizes, ref err));
    }


    /// <summary>
    /// Deletes files in the specified key range from the default column family.
    /// This is a maintenance operation and does not remove the keys from the database.
    /// </summary>
    public unsafe void DeleteFilesInRange(string startKey, string limitKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(startKey);
        ArgumentException.ThrowIfNullOrEmpty(limitKey);

        byte[] startBytes = Encoding.UTF8.GetBytes(startKey + '\0');
        byte[] limitBytes = Encoding.UTF8.GetBytes(limitKey + '\0');

        fixed (byte* startPtr = startBytes)
        fixed (byte* limitPtr = limitBytes)
        {
            nint err = default;
            NativeMethods.rocksdb_delete_file_in_range(Handle, startPtr, (nuint)startBytes.Length - 1, limitPtr, (nuint)limitBytes.Length - 1, ref err);
            NativeMethods.ThrowOnError(err);
        }
    }

    /// <summary>
    /// Deletes files in the specified key range from the given column family.
    /// </summary>
    public unsafe void DeleteFilesInRange(ColumnFamilyHandle cf, string startKey, string limitKey)
    {
        ArgumentNullException.ThrowIfNull(cf);
        ArgumentException.ThrowIfNullOrEmpty(startKey);
        ArgumentException.ThrowIfNullOrEmpty(limitKey);

        byte[] startBytes = Encoding.UTF8.GetBytes(startKey + '\0');
        byte[] limitBytes = Encoding.UTF8.GetBytes(limitKey + '\0');

        fixed (byte* startPtr = startBytes)
        fixed (byte* limitPtr = limitBytes)
        {
            nint err = default;
            NativeMethods.rocksdb_delete_file_in_range_cf(Handle, cf.Handle, startPtr, (nuint)startBytes.Length - 1, limitPtr, (nuint)limitBytes.Length - 1, ref err);
            NativeMethods.ThrowOnError(err);
        }
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
    public unsafe void IngestExternalFile(IReadOnlyList<string> filePaths, IngestExternalFileOptions options)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(options);

        int count = filePaths.Count;
        byte[][] pathBytes = [.. filePaths.Select(p => Encoding.UTF8.GetBytes(p + '\0'))];
        var pins = new GCHandle[count];
        var pathPtrs = new byte*[count];
        try
        {
            for (int i = 0; i < count; i++)
            {
                pins[i] = GCHandle.Alloc(pathBytes[i], GCHandleType.Pinned);
                pathPtrs[i] = (byte*)pins[i].AddrOfPinnedObject();
            }

            nint err = default;
            fixed (byte** pp = pathPtrs)
                NativeMethods.rocksdb_ingest_external_file(Handle, pp, (nuint)count, options.Handle, ref err);
            NativeMethods.ThrowOnError(err);
        }
        finally
        {
            for (int i = 0; i < count; i++)
                if (pins[i].IsAllocated) pins[i].Free();
        }
    }

    /// <summary>
    /// Ingests a list of pre-built SST files into <paramref name="cf"/>.
    /// </summary>
    public unsafe void IngestExternalFile(IReadOnlyList<string> filePaths, ColumnFamilyHandle cf, IngestExternalFileOptions options)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(cf);
        ArgumentNullException.ThrowIfNull(options);

        int count = filePaths.Count;
        byte[][] pathBytes = [.. filePaths.Select(p => Encoding.UTF8.GetBytes(p + '\0'))];
        var pins = new GCHandle[count];
        var pathPtrs = new byte*[count];
        try
        {
            for (int i = 0; i < count; i++)
            {
                pins[i] = GCHandle.Alloc(pathBytes[i], GCHandleType.Pinned);
                pathPtrs[i] = (byte*)pins[i].AddrOfPinnedObject();
            }

            nint err = default;
            fixed (byte** pp = pathPtrs)
                NativeMethods.rocksdb_ingest_external_file_cf(Handle, cf.Handle, pp, (nuint)count, options.Handle, ref err);
            NativeMethods.ThrowOnError(err);
        }
        finally
        {
            for (int i = 0; i < count; i++)
                if (pins[i].IsAllocated) pins[i].Free();
        }
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

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_close(Handle);
    }

    public override void DisposeUnmanagedResources()
    {
        // All column family handles must be destroyed before primary database handle is closed
        if (_columnFamilyHandles != null)
        {
            foreach (var cfh in _columnFamilyHandles.Values)
            {
                cfh.Dispose();
            }
        }

        base.DisposeUnmanagedResources();

        // Dispose the options after rocksdb_close — sub-objects (CompactionFilter,
        // Comparator, MergeOperator, etc.) must outlive the DB handle.
        _ownedOptions.Dispose();
    }
}
