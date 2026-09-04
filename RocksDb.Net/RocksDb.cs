using System.Diagnostics.CodeAnalysis;
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

    // Always non-null, so a column family created after open can be registered
    // whether or not the database was opened with any.
    private readonly Dictionary<string, ColumnFamilyHandle> _columnFamilyHandles = [];

    // Cached so that repeated calls do not each leak a wrapper struct.
    private ColumnFamilyHandle? _defaultColumnFamily;
    private readonly DbOptions _ownedOptions;

    // The descriptors a database was opened with, held for the database's
    // lifetime.
    //
    // ColumnFamilyDescriptor disposes the options it created from its own
    // finalizer. After Open returned, the descriptor list was unreachable, so
    // that finalizer ran at the next collection and destroyed the comparator,
    // compaction filter and logger attached to a column family's options while
    // the database was still calling them. Holding the descriptors here stops
    // that, because they cannot become unreachable before the database does.
    //
    // Held, and deliberately never disposed from here. A descriptor and the
    // options it owns belong to the caller, who may hand the same list to a
    // second database: creating one, closing it, then reopening read-only with
    // the same descriptors is ordinary code. Disposing those options as a side
    // effect of closing one database would destroy objects the caller still
    // owns and is about to reuse.
    //
    // That was tried, and it faulted with an access violation. The cause is
    // exactly the above: a disposed DbOptions reports a null handle, RocksDb
    // requires every pointer argument to be non-null, so the second open
    // dereferenced null. It looked like a teardown-ordering problem because
    // the crash landed in whichever test ran next, which is why the Open
    // overloads now reject disposed options outright rather than passing a
    // null pointer into native code.
    //
    // So these options are released when the descriptors are themselves
    // collected. Not deterministic, but correct: only the caller knows when a
    // descriptor is finished with.
    private readonly List<ColumnFamilyDescriptor> _descriptors = [];

    private RocksDb(nint handle, DbOptions options)
        : base(handle)
    {
        _ownedOptions = options;
    }

    /// <summary>Name RocksDb gives the column family that always exists.</summary>
    private const string DefaultColumnFamilyName = "default";

    private RocksDb(nint handle, nint[] cfHandles, DbOptions options,
        IReadOnlyList<ColumnFamilyDescriptor>? descriptors = null)
        : base(handle)
    {
        _ownedOptions = options;

        if (descriptors is not null)
        {
            _descriptors.AddRange(descriptors);
        }

        foreach (var cf in cfHandles)
        {
            ColumnFamilyHandle cfh = new(cf);
            cfh.SetParent(this);
            _columnFamilyHandles[cfh.Name] = cfh;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Open / static management
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Opens (or creates) a database at <paramref name="path"/>.</summary>
    /// <remarks>
    /// The returned database takes ownership of <paramref name="options"/> and
    /// disposes it when the database is disposed. Do not dispose it yourself and
    /// do not reuse it for a second open.
    /// </remarks>
    public static RocksDb Open(DbOptions options, string path)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        nint handle = NativeMethods.rocksdb_open(options.Handle, path, ref err);
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle, options);
    }

    /// <summary>
    /// Opens the database with an explicit set of column families.
    /// The <c>"default"</c> column family must always be included.
    /// </summary>
    /// <remarks>
    /// Returns the database only. The handles are registered internally rather
    /// than returned, so reach them with
    /// <see cref="GetColumnFamily"/>; the database disposes them for you.
    /// <para>
    /// The returned database takes ownership of <paramref name="options"/> and
    /// disposes it when the database is disposed.
    /// </para>
    /// </remarks>
    public static unsafe RocksDb Open(DbOptions options, string path, IReadOnlyList<ColumnFamilyDescriptor> columnFamilies)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(columnFamilies);

        int count = columnFamilies.Count;
        nint[] cfHandles = new nint[count];
        foreach (ColumnFamilyDescriptor descriptor in columnFamilies)
        {
            ArgumentNullException.ThrowIfNull(descriptor);

            // A disposed DbOptions reports a null handle, and RocksDb requires
            // every pointer argument to be non-null, so passing one through
            // dereferences null inside the native open. Reusing a descriptor list
            // for a second database is how this happens, and the access violation
            // it produced named neither the descriptor nor the reuse.
            descriptor.Options.ThrowIfDisposed();
        }

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
                    namesPtr, optsPtr, handlesPtr, ref err);
        }
        finally
        {
            for (int i = 0; i < count; i++)
                if (pins[i].IsAllocated) pins[i].Free();
        }
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle, cfHandles, options, columnFamilies);
    }

    /// <summary>Opens an existing database in read-only mode.</summary>
    /// <remarks>
    /// The returned database takes ownership of <paramref name="options"/> and
    /// disposes it when the database is disposed.
    /// </remarks>
    public static RocksDb OpenReadOnly(DbOptions options, string path, bool errorIfWalExists = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);

        nint err = default;
        nint handle = NativeMethods.rocksdb_open_for_read_only(
            options.Handle, path, errorIfWalExists ? (byte)1 : (byte)0, ref err);
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle, options);
    }

    /// <summary>
    /// Opens an existing database in read-only mode with an explicit set of
    /// column families. The <c>"default"</c> column family must always be
    /// included.
    /// </summary>
    /// <remarks>
    /// Returns the database only; reach the handles with
    /// <see cref="GetColumnFamily"/>. The returned database takes ownership of
    /// <paramref name="options"/> and disposes it.
    /// </remarks>
    public static unsafe RocksDb OpenReadOnly(DbOptions options, string path, IReadOnlyList<ColumnFamilyDescriptor> columnFamilies, bool errorIfWalExists = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(columnFamilies);

        int count = columnFamilies.Count;
        nint[] cfHandles = new nint[count];
        foreach (ColumnFamilyDescriptor descriptor in columnFamilies)
        {
            ArgumentNullException.ThrowIfNull(descriptor);

            // A disposed DbOptions reports a null handle, and RocksDb requires
            // every pointer argument to be non-null, so passing one through
            // dereferences null inside the native open. Reusing a descriptor list
            // for a second database is how this happens, and the access violation
            // it produced named neither the descriptor nor the reuse.
            descriptor.Options.ThrowIfDisposed();
        }

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
                    namesPtr, optsPtr, handlesPtr,
                    errorIfWalExists ? (byte)1 : (byte)0, ref err);
        }
        finally
        {
            for (int i = 0; i < count; i++)
                if (pins[i].IsAllocated) pins[i].Free();
        }
        NativeMethods.ThrowOnError(err);

        return new RocksDb(handle, cfHandles, options, columnFamilies);
    }

    /// <summary>
    /// Opens the database as a secondary instance that can catch up to the primary.
    /// </summary>
    public static RocksDb OpenAsSecondary(DbOptions options, string path, string secondaryPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ThrowIfDisposed();
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
        options.ThrowIfDisposed();
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

        if (err != nint.Zero)
        {
            // RocksDb allocates the array whether or not the call succeeded.
            if (list is not null)
            {
                NativeMethods.rocksdb_list_column_families_destroy(list, count);
            }

            NativeMethods.ThrowOnError(err);
        }

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
        ArgumentNullException.ThrowIfNull(cf);
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

    /// <summary>
    /// Deletes a key that was written exactly once and never updated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cheaper than <see cref="Delete(ReadOnlySpan{byte}, WriteOptions?)"/>,
    /// because RocksDb may drop the tombstone and the value together as soon as
    /// it meets them, rather than carrying the tombstone down through every
    /// level.
    /// </para>
    /// <para>
    /// <b>Only valid for a key written once.</b> If the key was ever
    /// overwritten, merged into, or deleted and rewritten, the result is
    /// undefined: an older version may reappear. RocksDb does not detect the
    /// misuse, so use ordinary <see cref="Delete(ReadOnlySpan{byte}, WriteOptions?)"/>
    /// unless the write-once property is guaranteed by the application.
    /// </para>
    /// </remarks>
    public unsafe void SingleDelete(ReadOnlySpan<byte> key, WriteOptions? options = null)
    {
        nint err = default;
        fixed (byte* k = key)
            NativeMethods.rocksdb_singledelete(Handle, (options ?? _defaultWriteOptions).Handle,
                k, (nuint)key.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <inheritdoc cref="SingleDelete(ReadOnlySpan{byte}, WriteOptions?)"/>
    public unsafe void SingleDelete(ReadOnlySpan<byte> key, ColumnFamilyHandle cf, WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        fixed (byte* k = key)
            NativeMethods.rocksdb_singledelete_cf(Handle, (options ?? _defaultWriteOptions).Handle, cf.Handle,
                k, (nuint)key.Length, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <inheritdoc cref="SingleDelete(ReadOnlySpan{byte}, WriteOptions?)"/>
    public void SingleDelete(string key, WriteOptions? options = null)
        => SingleDelete(Encoding.UTF8.GetBytes(key), options);

    /// <summary>Deletes the entry for <paramref name="key"/> from <paramref name="cf"/>.</summary>
    public unsafe void Delete(ReadOnlySpan<byte> key, ColumnFamilyHandle cf, WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);
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
        ArgumentNullException.ThrowIfNull(cf);
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
        ArgumentNullException.ThrowIfNull(cf);

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

    /// <summary>
    /// Atomically applies all operations in an indexed <paramref name="batch"/>.
    /// </summary>
    /// <remarks>
    /// Until this existed, a <see cref="WriteBatchWithIndex"/> could be built
    /// and inspected but never applied, which made the type unusable for its
    /// purpose. Applying it does not clear it; the batch may be reused or
    /// applied again.
    /// </remarks>
    public void Write(WriteBatchWithIndex batch, WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        nint err = default;
        NativeMethods.rocksdb_write_writebatch_wi(Handle, (options ?? _defaultWriteOptions).Handle, batch.Handle, ref err);
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

    /// <summary>
    /// Creates one iterator per column family, all sharing a single consistent
    /// view of the database.
    /// </summary>
    /// <param name="columnFamilies">The families to iterate.</param>
    /// <param name="options">Read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The iterators, in the same order as <paramref name="columnFamilies"/>.</returns>
    /// <remarks>
    /// <para>
    /// This is the difference that matters: opening iterators one at a time gives
    /// no guarantee they see the same state, so a write landing between two calls
    /// is visible to one iterator and not the other. Created together, they all
    /// see the same point in time.
    /// </para>
    /// <para>
    /// Dispose every returned iterator. If the call fails, none are created.
    /// </para>
    /// </remarks>
    public unsafe IReadOnlyList<Iterator> NewIterators(
        IReadOnlyList<ColumnFamilyHandle> columnFamilies, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(columnFamilies);

        if (columnFamilies.Count == 0)
        {
            return [];
        }

        nint[] cfHandles = new nint[columnFamilies.Count];
        for (int i = 0; i < columnFamilies.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(columnFamilies[i]);
            cfHandles[i] = columnFamilies[i].Handle;
        }

        nint[] iterators = new nint[columnFamilies.Count];
        nint err = default;

        fixed (nint* cfs = cfHandles)
        fixed (nint* its = iterators)
            NativeMethods.rocksdb_create_iterators(
                Handle, (options ?? _defaultReadOptions).Handle, cfs, its,
                (nuint)columnFamilies.Count, ref err);

        // On failure RocksDb returns before creating any iterator, so there is
        // nothing to clean up.
        NativeMethods.ThrowOnError(err);

        var wrapped = new Iterator[columnFamilies.Count];
        for (int i = 0; i < columnFamilies.Count; i++)
        {
            wrapped[i] = Iterator.FromHandle(iterators[i], this, options);
        }

        return wrapped;
    }

    // ── Reads that avoid a copy ──────────────────────────────────────────────

    /// <summary>
    /// Reads the value for <paramref name="key"/> without copying it into
    /// managed memory, or returns <see langword="null"/> if the key is absent.
    /// </summary>
    /// <remarks>
    /// Dispose the result promptly: it pins the block the value came from, which
    /// cannot be evicted from the block cache while it lives. See
    /// <see cref="PinnableSlice"/>.
    /// </remarks>
    public unsafe PinnableSlice? GetPinned(ReadOnlySpan<byte> key, ReadOptions? options = null)
    {
        nint err = default;
        nint slice;
        fixed (byte* k = key)
            slice = NativeMethods.rocksdb_get_pinned(Handle, (options ?? _defaultReadOptions).Handle,
                k, (nuint)key.Length, ref err);

        // A null return means either "not found" or "failed", so the error has to
        // be checked before deciding which.
        NativeMethods.ThrowOnError(err);

        return slice == nint.Zero ? null : new PinnableSlice(slice, this);
    }

    /// <inheritdoc cref="GetPinned(ReadOnlySpan{byte}, ReadOptions?)"/>
    public unsafe PinnableSlice? GetPinned(ReadOnlySpan<byte> key, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        nint slice;
        fixed (byte* k = key)
            slice = NativeMethods.rocksdb_get_pinned_cf(Handle, (options ?? _defaultReadOptions).Handle,
                cf.Handle, k, (nuint)key.Length, ref err);

        NativeMethods.ThrowOnError(err);

        return slice == nint.Zero ? null : new PinnableSlice(slice, this);
    }

    /// <summary>
    /// Reads the value for <paramref name="key"/> into a caller-owned buffer,
    /// allocating nothing.
    /// </summary>
    /// <param name="key">The key to read.</param>
    /// <param name="destination">Buffer to copy the value into.</param>
    /// <param name="valueLength">
    /// The value's full length when the key was found, whether or not it fitted,
    /// so a caller given <see langword="false"/> can size a buffer and retry.
    /// Zero when the key was absent.
    /// </param>
    /// <param name="options">Read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>
    /// <see langword="true"/> only when the key was found <em>and</em> the value
    /// fitted. <see langword="false"/> means either the key was absent, which
    /// leaves <paramref name="valueLength"/> zero, or the buffer was too small,
    /// which sets it to the length required.
    /// </returns>
    /// <remarks>
    /// The counterpart to <see cref="GetPinned(ReadOnlySpan{byte}, ReadOptions?)"/>:
    /// this copies once into memory you already own and pins nothing, so there is
    /// no lifetime to manage. Prefer it when the values are small or a buffer can
    /// be reused across reads.
    /// </remarks>
    public unsafe bool TryGetInto(
        ReadOnlySpan<byte> key, Span<byte> destination, out int valueLength, ReadOptions? options = null)
    {
        nint err = default;
        byte copied;
        byte found;
        nuint length;

        fixed (byte* k = key)
        fixed (byte* dest = destination)
            copied = NativeMethods.rocksdb_get_into_buffer(Handle, (options ?? _defaultReadOptions).Handle,
                k, (nuint)key.Length, dest, (nuint)destination.Length, out length, &found, ref err);

        NativeMethods.ThrowOnError(err);

        valueLength = found != 0 ? checked((int)length) : 0;
        return copied != 0;
    }

    /// <inheritdoc cref="TryGetInto(ReadOnlySpan{byte}, Span{byte}, out int, ReadOptions?)"/>
    public unsafe bool TryGetInto(
        ReadOnlySpan<byte> key, ColumnFamilyHandle cf, Span<byte> destination, out int valueLength,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);

        nint err = default;
        byte copied;
        byte found;
        nuint length;

        fixed (byte* k = key)
        fixed (byte* dest = destination)
            copied = NativeMethods.rocksdb_get_into_buffer_cf(Handle, (options ?? _defaultReadOptions).Handle,
                cf.Handle, k, (nuint)key.Length, dest, (nuint)destination.Length, out length, &found, ref err);

        NativeMethods.ThrowOnError(err);

        valueLength = found != 0 ? checked((int)length) : 0;
        return copied != 0;
    }

    /// <summary>Returns the value for <paramref name="key"/> in <paramref name="cf"/>, or <c>null</c>.</summary>
    public unsafe byte[]? Get(ReadOnlySpan<byte> key, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);
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
        ArgumentNullException.ThrowIfNull(cf);
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
    /// Reads several keys from the default column family in one call.
    /// </summary>
    /// <remarks>
    /// A missing key yields <see langword="null"/> in the corresponding
    /// position, so the result always has one entry per key.
    /// </remarks>
    public byte[]?[] MultiGet(IReadOnlyList<byte[]> keys, ReadOptions? options = null)
        => MultiGetCore(keys, columnFamilies: null, options);

    /// <summary>
    /// Reads several keys from <paramref name="cf"/> in one call.
    /// </summary>
    /// <inheritdoc cref="MultiGet(IReadOnlyList{byte[]}, ReadOptions?)" path="/remarks"/>
    public byte[]?[] MultiGet(IReadOnlyList<byte[]> keys, ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(cf);

        nint[] handles = new nint[keys.Count];
        Array.Fill(handles, cf.Handle);

        return MultiGetCore(keys, handles, options);
    }

    /// <summary>
    /// Reads several keys in one call, each from the column family at the same
    /// position in <paramref name="columnFamilies"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason this overload exists: RocksDb takes one column family per key,
    /// so a caller can fetch across several families in a single round trip.
    /// Restricting the API to one family per call would throw that away.
    /// </para>
    /// <para>
    /// Two parallel lists rather than a list of pairs, because a second
    /// list-shaped overload would make <c>MultiGet([])</c> ambiguous at the call
    /// site for every existing caller.
    /// </para>
    /// <para>
    /// A missing key yields <see langword="null"/> in the corresponding position.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The two lists are of different lengths.</exception>
    public byte[]?[] MultiGet(
        IReadOnlyList<byte[]> keys, IReadOnlyList<ColumnFamilyHandle> columnFamilies, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(columnFamilies);

        if (keys.Count != columnFamilies.Count)
        {
            throw new ArgumentException(
                $"Expected one column family per key, but got {keys.Count} keys and " +
                $"{columnFamilies.Count} column families.",
                nameof(columnFamilies));
        }

        nint[] handles = new nint[columnFamilies.Count];
        for (int i = 0; i < columnFamilies.Count; i++)
        {
            ColumnFamilyHandle cf = columnFamilies[i];
            ArgumentNullException.ThrowIfNull(cf);
            handles[i] = cf.Handle;
        }

        return MultiGetCore(keys, handles, options);
    }

    /// <summary>
    /// Reads several keys from <paramref name="cf"/> in one call, without copying
    /// the values into managed memory.
    /// </summary>
    /// <param name="keys">The keys to read.</param>
    /// <param name="cf">The column family to read from.</param>
    /// <param name="sortedInput">
    /// Set this when <paramref name="keys"/> is already in the database's sort
    /// order, which lets RocksDb skip sorting them. Passing
    /// <see langword="true"/> for unsorted keys gives wrong results, so leave it
    /// alone unless the order is guaranteed.
    /// </param>
    /// <param name="options">Read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>
    /// One entry per key, <see langword="null"/> where the key was absent. Every
    /// non-null entry must be disposed; see <see cref="PinnableSlice"/>.
    /// </returns>
    /// <remarks>
    /// The batched counterpart to
    /// <see cref="GetPinned(ReadOnlySpan{byte}, ColumnFamilyHandle, ReadOptions?)"/>.
    /// It avoids a copy per key, which is what makes it worth the disposal
    /// burden on a large batch. RocksDb offers this only per column family, so
    /// there is no cross-family or default-family overload.
    /// </remarks>
    public unsafe PinnableSlice?[] MultiGetPinned(
        IReadOnlyList<byte[]> keys, ColumnFamilyHandle cf, bool sortedInput = false, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(cf);

        int n = keys.Count;
        if (n == 0)
        {
            return [];
        }

        byte*[] keyPtrs = new byte*[n];
        nuint[] keySizes = new nuint[n];
        nint[] slices = new nint[n];
        nint[] errs = new nint[n];

        var pins = new GCHandle[n];
        try
        {
            for (int i = 0; i < n; i++)
            {
                ArgumentNullException.ThrowIfNull(keys[i]);
                pins[i] = GCHandle.Alloc(keys[i], GCHandleType.Pinned);
                keyPtrs[i] = (byte*)pins[i].AddrOfPinnedObject();
                keySizes[i] = (nuint)keys[i].Length;
            }

            fixed (byte** kp = keyPtrs)
            fixed (nuint* ks = keySizes)
            fixed (nint* sp = slices)
            fixed (nint* ep = errs)
                NativeMethods.rocksdb_batched_multi_get_cf(Handle, (options ?? _defaultReadOptions).Handle,
                    cf.Handle, (nuint)n, kp, ks, sp, (byte**)ep, sortedInput ? (byte)1 : (byte)0);
        }
        finally
        {
            for (int i = 0; i < n; i++)
            {
                if (pins[i].IsAllocated)
                {
                    pins[i].Free();
                }
            }
        }

        // Wrap everything before throwing, so a failure in one key does not leak
        // the slices RocksDb allocated for the others.
        var results = new PinnableSlice?[n];
        for (int i = 0; i < n; i++)
        {
            if (slices[i] != nint.Zero)
            {
                results[i] = new PinnableSlice(slices[i], this);
            }
        }

        try
        {
            ThrowFirstError(errs);
        }
        catch
        {
            foreach (PinnableSlice? slice in results)
            {
                slice?.Dispose();
            }

            throw;
        }

        return results;
    }

    /// <summary>
    /// Shared implementation. A null <paramref name="columnFamilies"/> reads from
    /// the default family; otherwise it holds one handle per key.
    /// </summary>
    private unsafe byte[]?[] MultiGetCore(
        IReadOnlyList<byte[]> keys, nint[]? columnFamilies, ReadOptions? options)
    {
        ArgumentNullException.ThrowIfNull(keys);

        int n = keys.Count;
        if (n == 0)
        {
            return [];
        }

        byte*[] keyPtrs = new byte*[n];
        nuint[] keySizes = new nuint[n];
        byte*[] valPtrs = new byte*[n];
        nuint[] valSizes = new nuint[n];
        nint[] errs = new nint[n];

        var pins = new GCHandle[n];
        try
        {
            for (int i = 0; i < n; i++)
            {
                ArgumentNullException.ThrowIfNull(keys[i]);
                pins[i] = GCHandle.Alloc(keys[i], GCHandleType.Pinned);
                keyPtrs[i] = (byte*)pins[i].AddrOfPinnedObject();
                keySizes[i] = (nuint)keys[i].Length;
            }

            fixed (byte** kp = keyPtrs)
            fixed (nuint* ks = keySizes)
            fixed (byte** vp = valPtrs)
            fixed (nuint* vs = valSizes)
            fixed (nint* ep = errs)
            fixed (nint* cfp = columnFamilies)
            {
                if (columnFamilies is null)
                {
                    NativeMethods.rocksdb_multi_get(Handle, (options ?? _defaultReadOptions).Handle,
                        (nuint)n, kp, ks, vp, vs, (byte**)ep);
                }
                else
                {
                    NativeMethods.rocksdb_multi_get_cf(Handle, (options ?? _defaultReadOptions).Handle,
                        cfp, (nuint)n, kp, ks, vp, vs, (byte**)ep);
                }
            }
        }
        finally
        {
            for (int i = 0; i < n; i++)
            {
                if (pins[i].IsAllocated)
                {
                    pins[i].Free();
                }
            }
        }

        // Copy and free every value before considering the errors. Throwing from
        // inside this loop, which is what the single-family version used to do,
        // leaked the values and error strings for every key after the first
        // failure.
        var results = new byte[]?[n];
        for (int i = 0; i < n; i++)
        {
            if (valPtrs[i] is not null)
            {
                results[i] = new ReadOnlySpan<byte>(valPtrs[i], checked((int)valSizes[i])).ToArray();
                NativeMethods.rocksdb_free((nint)valPtrs[i]);
            }
        }

        ThrowFirstError(errs);
        return results;
    }

    /// <summary>
    /// Throws for the first per-key error, having freed all of them.
    /// </summary>
    /// <remarks>
    /// RocksDb allocates one message per failing key and the caller owns each.
    /// Only the first becomes the exception, but every one has to be released.
    /// </remarks>
    private static void ThrowFirstError(nint[] errs)
    {
        nint first = nint.Zero;

        for (int i = 0; i < errs.Length; i++)
        {
            if (errs[i] == nint.Zero)
            {
                continue;
            }

            if (first == nint.Zero)
            {
                first = errs[i];
            }
            else
            {
                NativeMethods.rocksdb_free(errs[i]);
            }
        }

        // Frees the message it reports.
        NativeMethods.ThrowOnError(first);
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
        ArgumentNullException.ThrowIfNull(cf);
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
        return Iterator.FromHandle(handle, this, options);
    }

    /// <summary>Creates a new iterator over <paramref name="cf"/>.</summary>
    public Iterator NewIterator(ColumnFamilyHandle cf, ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);
        nint handle = NativeMethods.rocksdb_create_iterator_cf(
            Handle, (options ?? _defaultReadOptions).Handle, cf.Handle);
        return Iterator.FromHandle(handle, this, options);
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
        var snapshot = new Snapshot(handle, this);
        snapshot.SetParent(this);
        return snapshot;
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

        return RegisterColumnFamily(name, new ColumnFamilyHandle(handle));
    }

    /// <summary>
    /// Creates a column family from files previously exported with
    /// <see cref="Checkpoint.ExportColumnFamily(ColumnFamilyHandle, string)"/>.
    /// </summary>
    /// <param name="name">Name for the new column family. It must not already exist.</param>
    /// <param name="options">Options for the new column family.</param>
    /// <param name="metadata">Metadata returned by the export.</param>
    /// <param name="importOptions">
    /// How the files are taken from the export directory, or
    /// <see langword="null"/> to copy them.
    /// </param>
    /// <remarks>
    /// <para>
    /// The receiving column family must use the same comparator the export was
    /// written with, since the files are ordered by it, and the files must still
    /// be where the metadata says they are.
    /// </para>
    /// <para>
    /// The returned handle is registered like any other, so
    /// <see cref="GetColumnFamily"/> finds it and the database disposes it.
    /// </para>
    /// </remarks>
    public ColumnFamilyHandle CreateColumnFamilyWithImport(
        string name,
        DbOptions options,
        ExportImportFilesMetadata metadata,
        ImportColumnFamilyOptions? importOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);

        using ImportColumnFamilyOptions? owned = importOptions is null ? new ImportColumnFamilyOptions() : null;

        nint err = default;
        nint handle = NativeMethods.rocksdb_create_column_family_with_import(
            Handle, options.Handle, name, (importOptions ?? owned!).Handle, metadata.Handle, ref err);
        NativeMethods.ThrowOnError(err);

        return RegisterColumnFamily(name, new ColumnFamilyHandle(handle));
    }

    /// <summary>
    /// Creates several column families in one call.
    /// </summary>
    /// <param name="options">Options applied to every family created here.</param>
    /// <param name="names">Names for the new families. None may already exist.</param>
    /// <returns>The handles, in the same order as <paramref name="names"/>.</returns>
    /// <remarks>
    /// Cheaper than a call each, because RocksDb writes one manifest record
    /// rather than one per family. The handles are registered like any other, so
    /// <see cref="GetColumnFamily"/> finds them and the database disposes them.
    /// </remarks>
    public unsafe IReadOnlyList<ColumnFamilyHandle> CreateColumnFamilies(
        DbOptions options, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(names);

        if (names.Count == 0)
        {
            return [];
        }

        byte[][] nameBytes = new byte[names.Count][];

        for (int i = 0; i < names.Count; i++)
        {
            ArgumentException.ThrowIfNullOrEmpty(names[i]);

            // NUL-terminated, because RocksDb reads these with
            // std::string(const char*), which is strlen-terminated. Passing
            // an unterminated buffer reads past the end of the pinned array
            // into whatever managed memory follows it. Every other open and
            // create path in this file already appends the terminator.
            nameBytes[i] = Encoding.UTF8.GetBytes(names[i] + '\0');
        }

        nint err = default;
        nint* list;

        // Not an array of name lengths, despite the plural. RocksDb writes
        // the number of handles it created into it, and never reads the
        // value it was given.
        nuint createdCount = 0;

        var pins = new GCHandle[names.Count];
        var namePtrs = new byte*[names.Count];

        try
        {
            for (int i = 0; i < names.Count; i++)
            {
                pins[i] = GCHandle.Alloc(nameBytes[i], GCHandleType.Pinned);
                namePtrs[i] = (byte*)pins[i].AddrOfPinnedObject();
            }

            fixed (byte** np = namePtrs)
                list = NativeMethods.rocksdb_create_column_families(
                    Handle, options.Handle, names.Count, np, &createdCount, ref err);
        }
        finally
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (pins[i].IsAllocated)
                {
                    pins[i].Free();
                }
            }
        }

        if (err != nint.Zero)
        {
            // RocksDb keeps the handles it created before the one that failed,
            // and allocates the array either way, so throwing without this
            // leaks both.
            if (list is not null)
            {
                for (nuint i = 0; i < createdCount; i++)
                {
                    NativeMethods.rocksdb_column_family_handle_destroy(list[i]);
                }

                NativeMethods.rocksdb_create_column_families_destroy(list);
            }

            NativeMethods.ThrowOnError(err);
        }

        if (list is null)
        {
            return [];
        }

        try
        {
            // The count RocksDb reported, not the count asked for.
            int count = checked((int)createdCount);
            var created = new ColumnFamilyHandle[count];

            for (int i = 0; i < count; i++)
            {
                created[i] = RegisterColumnFamily(names[i], new ColumnFamilyHandle(list[i]));
            }

            return created;
        }
        finally
        {
            // The array is a separate allocation from the handles it holds, and
            // only the array is released here.
            NativeMethods.rocksdb_create_column_families_destroy(list);
        }
    }

    /// <summary>Creates a new column family with TTL.</summary>
    public ColumnFamilyHandle CreateColumnFamilyWithTtl(DbOptions options, string name, int ttlSeconds)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(name);

        nint err = default;
        nint handle = NativeMethods.rocksdb_create_column_family_with_ttl(Handle, options.Handle, name, ttlSeconds, ref err);
        NativeMethods.ThrowOnError(err);

        return RegisterColumnFamily(name, new ColumnFamilyHandle(handle));
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
        // Cached, because each call allocates a fresh rocksdb_column_family_handle_t
        // and the wrapper is non-owning, so every call used to leak one.
        if (_defaultColumnFamily is not null)
        {
            return _defaultColumnFamily;
        }

        nint h = NativeMethods.rocksdb_get_default_column_family_handle(Handle);
        var cf = new ColumnFamilyHandle(h);
        cf.TransferOwnership(); // The database owns this handle.
        cf.SetParent(this);

        _defaultColumnFamily = cf;
        return cf;
    }

    /// <summary>
    /// Returns the handle for the column family called <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// Covers families opened with the database and families created since,
    /// through <see cref="CreateColumnFamily"/> or
    /// <see cref="CreateColumnFamilyWithTtl"/>.
    /// </remarks>
    /// <exception cref="KeyNotFoundException">
    /// No column family of that name is known to this database. Previously this
    /// returned null from a non-nullable signature, so the mistake surfaced as a
    /// NullReferenceException somewhere else. Use
    /// <see cref="TryGetColumnFamily"/> when absence is expected.
    /// </exception>
    public ColumnFamilyHandle GetColumnFamily(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return TryGetColumnFamily(name, out ColumnFamilyHandle? cfh)
            ? cfh
            : throw new KeyNotFoundException(
                $"No column family named '{name}' is known to this database. " +
                $"Known families: {string.Join(", ", ColumnFamilyNames)}.");
    }

    /// <summary>
    /// Looks up the handle for the column family called <paramref name="name"/>,
    /// returning false rather than throwing when there is none.
    /// </summary>
    public bool TryGetColumnFamily(string name, [NotNullWhen(true)] out ColumnFamilyHandle? columnFamily)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (_columnFamilyHandles.TryGetValue(name, out ColumnFamilyHandle? cfh))
        {
            columnFamily = cfh;
            return true;
        }

        // Every database has a default family, even one opened without naming
        // any, so resolve it on demand rather than reporting it as unknown.
        if (name == DefaultColumnFamilyName)
        {
            columnFamily = GetDefaultColumnFamily();
            return true;
        }

        columnFamily = null;
        return false;
    }

    /// <summary>Names of the column families this database knows about.</summary>
    public IReadOnlyCollection<string> ColumnFamilyNames
        => _columnFamilyHandles.Count > 0
            ? [.. _columnFamilyHandles.Keys]
            : [DefaultColumnFamilyName];

    /// <summary>
    /// Tracks a newly created column family so that
    /// <see cref="GetColumnFamily"/> can find it, and ties its lifetime to this
    /// database.
    /// </summary>
    private ColumnFamilyHandle RegisterColumnFamily(string name, ColumnFamilyHandle cf)
    {
        cf.SetParent(this);
        _columnFamilyHandles.Add(name, cf);
        return cf;
    }

    /// <summary>Returns metadata for the default column family.</summary>
    public ColumnFamilyMetadata? GetColumnFamilyMetadata()
    {
        nint meta = NativeMethods.rocksdb_get_column_family_metadata(Handle);
        return meta == nint.Zero ? null : ColumnFamilyMetadata.ReadAndDestroy(meta);
    }

    /// <summary>Returns metadata for <paramref name="cf"/>.</summary>
    public ColumnFamilyMetadata? GetColumnFamilyMetadata(ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        nint meta = NativeMethods.rocksdb_get_column_family_metadata_cf(Handle, cf.Handle);
        return meta == nint.Zero ? null : ColumnFamilyMetadata.ReadAndDestroy(meta);
    }

    /// <summary>
    /// Returns metadata for the default column family, restricted to the level
    /// and key range in <paramref name="options"/>.
    /// </summary>
    public ColumnFamilyMetadata? GetColumnFamilyMetadata(ColumnFamilyMetadataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        nint meta = NativeMethods.rocksdb_get_column_family_metadata_with_options(Handle, options.Handle);
        return meta == nint.Zero ? null : ColumnFamilyMetadata.ReadAndDestroy(meta);
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
        return meta == nint.Zero ? null : ColumnFamilyMetadata.ReadAndDestroy(meta);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Flush / Compact
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Flushes the memtable of the <c>"default"</c> column family to storage.
    /// </summary>
    /// <remarks>
    /// The default family only, despite taking no column family argument. The
    /// native call this maps to targets the default family, so other families
    /// keep their unflushed memtables. To flush several, pass them to
    /// <see cref="Flush(IReadOnlyList{ColumnFamilyHandle}, FlushOptions)"/>.
    /// </remarks>
    public void Flush(FlushOptions? options = null)
    {
        nint err = default;
        NativeMethods.rocksdb_flush(Handle, (options ?? _defaultFlushOptions).Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Flushes the specified column family.</summary>
    public void Flush(ColumnFamilyHandle cf, FlushOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cf);
        nint err = default;
        NativeMethods.rocksdb_flush_cf(Handle, (options ?? _defaultFlushOptions).Handle, cf.Handle, ref err);
        NativeMethods.ThrowOnError(err);
    }

    /// <summary>Flushes the specified column families.</summary>
    /// <remarks>
    /// An empty list is not "flush nothing": it falls through to
    /// <see cref="Flush(FlushOptions)"/> and so flushes the <c>"default"</c>
    /// family. If you mean to flush nothing, do not call this.
    /// </remarks>
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
        ArgumentNullException.ThrowIfNull(cf);
        fixed (byte* s = startKey)
        fixed (byte* e = limitKey)
            NativeMethods.rocksdb_compact_range_cf(Handle, cf.Handle,
                startKey.IsEmpty ? null : s, (nuint)startKey.Length,
                limitKey.IsEmpty ? null : e, (nuint)limitKey.Length);
    }

    /// <summary>
    /// Triggers compaction on the key range
    /// [<paramref name="startKey"/>, <paramref name="limitKey"/>) using the
    /// given options.
    /// </summary>
    /// <remarks>
    /// Omitting both bounds, or passing empty spans, compacts the whole
    /// key-space. An empty <paramref name="startKey"/> means "from the first
    /// key" and an empty <paramref name="limitKey"/> means "to the last".
    /// </remarks>
    public unsafe void CompactRange(CompactRangeOptions options,
        ReadOnlySpan<byte> startKey = default, ReadOnlySpan<byte> limitKey = default)
    {
        fixed (byte* s = startKey)
        fixed (byte* e = limitKey)
            NativeMethods.rocksdb_compact_range_opt(Handle, options.Handle,
                startKey.IsEmpty ? null : s, (nuint)startKey.Length,
                limitKey.IsEmpty ? null : e, (nuint)limitKey.Length);
    }

    /// <summary>
    /// Marks the files overlapping the given key range for compaction, and asks
    /// RocksDb to schedule one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A suggestion, not a command. Unlike <see cref="CompactRange(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/> this
    /// returns immediately and the compaction happens on a background thread, or
    /// not at all.
    /// </para>
    /// <para>
    /// Two conditions have to hold for anything to happen. Auto compactions must
    /// be enabled, since a marked file is still only a reason for the automatic
    /// picker to act. And only levels below the highest non-empty level are
    /// marked, so a database whose data is all in level 0 has nothing to mark.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Marks the files overlapping the given key range for compaction in the
    /// given column family, and asks RocksDb to schedule one.
    /// </summary>
    /// <remarks>
    /// Carries the same conditions as
    /// <see cref="SuggestCompactRange(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>.
    /// </remarks>
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

    /// <summary>
    /// Stops the background flush and compaction threads.
    /// </summary>
    /// <param name="wait">
    /// When true, blocks until the running jobs finish. When false, signals
    /// them to stop and returns.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is a one-way door, not a pause. It puts the database into the same
    /// state as the start of a close, so afterwards reads still work and
    /// <see cref="SetOptions(IEnumerable{KeyValuePair{string, string}})"/> still
    /// works, but any operation needing background threads fails with
    /// "Shutdown in progress". <see cref="Flush(FlushOptions)"/> is the one callers hit.
    /// </para>
    /// <para>
    /// To suspend background work temporarily and resume it, use
    /// <see cref="PauseBackgroundWork"/> and
    /// <see cref="ContinueBackgroundWork"/> instead.
    /// </para>
    /// </remarks>
    public void CancelAllBackgroundWork(bool wait = false)
    {
        NativeMethods.rocksdb_cancel_all_background_work(Handle, wait ? (byte)1 : (byte)0);
    }

    /// <summary>Waits for pending compaction work, optionally using custom options.</summary>
    public void WaitForCompact(WaitForCompactOptions? options = null)
    {
        nint err = default;

        // Only dispose what this method created. Disposing a caller-supplied
        // instance left it unusable, so a second call with the same options
        // passed a zero handle into native code.
        WaitForCompactOptions? owned = options is null ? new WaitForCompactOptions() : null;
        try
        {
            NativeMethods.rocksdb_wait_for_compact(Handle, (options ?? owned!).Handle, ref err);
        }
        finally
        {
            owned?.Dispose();
        }

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

    /// <summary>
    /// Applies one or more runtime options to the default column family.
    /// </summary>
    /// <remarks>
    /// Column-family options, not database-wide ones, despite taking no column
    /// family: it is the overload below with the default family filled in. Use
    /// <see cref="SetDbOptions(IEnumerable{KeyValuePair{string, string}})"/> for
    /// settings that belong to the database.
    /// </remarks>
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
        ArgumentException.ThrowIfNullOrEmpty(propName);

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
        ArgumentNullException.ThrowIfNull(cf);
        ArgumentException.ThrowIfNullOrEmpty(propName);

        nint ptr = NativeMethods.rocksdb_property_value_cf(Handle, cf.Handle, propName);
        if (ptr == nint.Zero) return null;

        string? result = Marshal.PtrToStringUTF8(ptr);
        NativeMethods.rocksdb_free(ptr);
        return result;
    }

    /// <summary>Returns an integer property for a specific column family.</summary>
    public unsafe ulong? GetPropertyInt(string propName, ColumnFamilyHandle cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
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

    /// <summary>
    /// Returns <see langword="true"/> when RocksDb's estimated key count is
    /// zero. An estimate, and one that can read zero for a database that still
    /// holds keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RocksDb computes the estimate as the entry count minus <em>twice</em> the
    /// deletion count, clamped at zero. The doubling is there because a
    /// deletion is itself an entry, so it usually cancels out and the estimate
    /// is close to right. It stops cancelling when keys are deleted that were
    /// never present: each such deletion subtracts two from the estimate while
    /// removing nothing, and enough of them drive it to zero while the real keys
    /// are all still there.
    /// </para>
    /// <para>
    /// Measured, so it is not hypothetical: 100 keys written and flushed, then
    /// 100 deletions of keys that never existed, and this property reports
    /// empty while every one of the 100 keys still reads back. Treat it as a
    /// cheap hint and iterate when you need an answer you can rely on.
    /// </para>
    /// </remarks>
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
    /// <remarks>
    /// Read in full before returning, so the result needs no disposal and stays
    /// valid for as long as you hold it. It used to hand back a disposable
    /// container whose elements read through it on every property access, which
    /// meant they were only valid while it was alive.
    /// </remarks>
    public IReadOnlyList<LiveFileMetadata> GetLiveFiles()
    {
        nint handle = NativeMethods.rocksdb_livefiles(Handle);
        return handle == nint.Zero ? [] : LiveFileMetadata.ReadAndDestroy(handle);
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

                        FileChecksum = ReadChecksum(info, i, checksum),
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

    /// <summary>Name RocksDb's only C-reachable checksum generator reports.</summary>
    private const string Crc32cChecksumFuncName = "FileChecksumCrc32c";

    /// <summary>
    /// Width of a CRC32C checksum, which <c>Finalize</c> writes with
    /// <c>PutFixed32</c>.
    /// </summary>
    private const int Crc32cChecksumLength = 4;

    /// <summary>
    /// Reads a file checksum, whose length the C API does not report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>rocksdb_livefiles_storage_info_file_checksum</c> returns
    /// <c>file_checksum.c_str()</c> and no length, unlike
    /// <c>replacement_contents</c> immediately beside it, which does report one.
    /// A checksum is raw binary, so reading up to the first NUL is wrong: it
    /// yields nothing at all when the leading byte is zero, and silently
    /// truncates when an interior byte is.
    /// </para>
    /// <para>
    /// That was not theoretical. CRC32C is four bytes written by
    /// <c>PutFixed32</c>, so roughly one checksum in 256 was read as empty and
    /// one in sixty as too short, which is what made a test asserting on the
    /// checksum fail intermittently in CI while passing everywhere else.
    /// </para>
    /// <para>
    /// The length therefore has to come from the algorithm. The function name
    /// says which was used, and CRC32C is the only generator the C API can
    /// install, so it is the only width that can be known here. Any other name
    /// means the database was written by an application using a generator this
    /// wrapper cannot identify, and there is no length it could safely assume.
    /// </para>
    /// </remarks>
    private static unsafe byte[] ReadChecksum(nint info, nuint index, byte* checksum)
    {
        if (checksum is null)
        {
            return [];
        }

        string? funcName = Marshal.PtrToStringUTF8(
            (nint)NativeMethods.rocksdb_livefiles_storage_info_file_checksum_func_name(info, index));

        return funcName == Crc32cChecksumFuncName
            ? new ReadOnlySpan<byte>(checksum, Crc32cChecksumLength).ToArray()
            : [];
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
    /// Where to start. <b>Inclusive:</b> the batch containing this sequence
    /// number is returned, so passing <see cref="LatestSequenceNumber"/> replays
    /// the most recent write again. To resume after a known point, pass one more
    /// than the last sequence number already consumed. Zero means the oldest
    /// record still retained.
    /// </param>
    /// <param name="options">Read options for the log, or <see langword="null"/> for the defaults.</param>
    /// <remarks>
    /// <para>
    /// The basis for replication and change-data-capture: each step yields the
    /// batch that was written and the sequence number it started at. Read what
    /// is inside a batch with <see cref="WriteBatch.Entries"/>.
    /// </para>
    /// <para>
    /// Only records still in the write-ahead log are visible, so a sequence
    /// number older than the oldest retained log fails rather than returning
    /// nothing.
    /// </para>
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
    /// Deletes whole SST files that lie entirely within the given key range, in
    /// the default column family.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This deletes files, not keys, and it is not a substitute for deleting
    /// keys. Two consequences follow, and both tend to surprise callers.
    /// </para>
    /// <para>
    /// Keys inside a deleted file are gone, with no tombstone and no way to
    /// recover them. Keys inside the range that happen to live in a file which
    /// also extends outside the range stay, because that file is not fully
    /// contained and so is left alone.
    /// </para>
    /// <para>
    /// Level 0 files are never deleted, whatever the range. Data still in level
    /// 0 therefore survives this call; run <see cref="CompactRange(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/> first if
    /// it needs to be considered.
    /// </para>
    /// <para>
    /// Snapshots taken before this call may not see the deleted data.
    /// </para>
    /// </remarks>
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
    /// Deletes whole SST files that lie entirely within the given key range, in
    /// the given column family.
    /// </summary>
    /// <remarks>
    /// Carries the same caveats as
    /// <see cref="DeleteFilesInRange(string, string)"/>: keys in deleted files
    /// are lost outright, partially covered files are left alone, and level 0 is
    /// never touched.
    /// </remarks>
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
    /// Stops RocksDb from deleting obsolete files, so that a consistent set of
    /// files stays on disk while something outside the database copies them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the primitive behind external backup tools. Compactions and
    /// flushes carry on and keep producing new files; what stops is the cleanup
    /// of the files they supersede, so disk usage grows until deletions are
    /// enabled again.
    /// </para>
    /// <para>
    /// Always pair this with <see cref="EnableFileDeletions"/>, which performs
    /// the deferred cleanup. A database left with deletions disabled never
    /// reclaims space.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Stops manual compactions from running, and cancels any in progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Narrower than <see cref="PauseBackgroundWork"/>, which stops automatic
    /// work too. This leaves automatic compaction alone and only refuses the
    /// explicit kind, which is useful while something else needs the disk.
    /// </para>
    /// <para>
    /// Reversible, unlike <see cref="CancelAllBackgroundWork(bool)"/>: call
    /// <see cref="EnableManualCompaction"/> to allow them again.
    /// </para>
    /// </remarks>
    public void DisableManualCompaction() => NativeMethods.rocksdb_disable_manual_compaction(Handle);

    /// <summary>Allows manual compactions again after <see cref="DisableManualCompaction"/>.</summary>
    public void EnableManualCompaction() => NativeMethods.rocksdb_enable_manual_compaction(Handle);

    /// <summary>
    /// Re-enables file deletions after <see cref="DisableFileDeletions"/>, and
    /// deletes the obsolete files that accumulated in the meantime.
    /// </summary>
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

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_close(Handle);
    }

    protected override void DisposeUnmanagedResources()
    {
        // Column family handles, iterators and snapshots must all be destroyed
        // before the database handle is closed, because their native destructors
        // reach into database internals. Every one of them registered this as
        // its parent, so the base releases them, newest first, before closing.
        // This used to be a loop over the column families here, which released
        // them ahead of the iterators reading from them and left the iterators
        // and snapshots to their own finalizers.
        base.DisposeUnmanagedResources();

        // Dispose the options after rocksdb_close — sub-objects (CompactionFilter,
        // Comparator, MergeOperator, etc.) must outlive the DB handle.
        _ownedOptions.Dispose();
    }
}
