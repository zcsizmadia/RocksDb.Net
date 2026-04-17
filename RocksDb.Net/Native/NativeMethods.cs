using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RocksDbNet.Native;

/// <summary>
/// P/Invoke declarations for the RocksDB C API (librocksdb).
/// All opaque handle types are represented as <see cref="nint"/>.
/// Binary key/value data uses unsafe byte* + nuint length pairs.
/// Error output uses ref nint (char**) — must be nint.Zero on entry;
/// on failure the callee sets it to a malloc'd string that we free via
/// <see cref="rocksdb_free"/>.
/// </summary>
internal static unsafe class NativeMethods
{
    /// <summary>
    /// Registers a custom DLL import resolver to locate the librocksdb native library
    /// from the runtimes/{rid}/native directory structure at startup.
    /// </summary>
    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), NativeResolver.ResolveRuntimeDll);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Memory
    // ─────────────────────────────────────────────────────────────────────────

    // Not declared in the public header but present in the compiled library;
    // used to free malloc()-ed strings returned by the C API.
    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_free(nint ptr);

    // ─────────────────────────────────────────────────────────────────────────
    // DB open / close
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_open(
        nint options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_open_with_ttl(
        nint options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int ttl,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_open_for_read_only(
        nint options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        byte error_if_wal_file_exists,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_open_as_secondary(
        nint options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string secondary_path,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_open_column_families(
        nint options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int num_column_families,
        string[] column_family_names,
        nint[] column_family_options,
        nint[] column_family_handles,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_open_for_read_only_column_families(
        nint options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int num_column_families,
        string[] column_family_names,
        nint[] column_family_options,
        nint[] column_family_handles,
        byte error_if_wal_file_exists,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_close(nint db);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_destroy_db(
        nint options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_repair_db(
        nint options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        ref nint errptr);

    // ─────────────────────────────────────────────────────────────────────────
    // DB identity / sequence number
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_get_db_identity(nint db, out nuint id_len);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_get_latest_sequence_number(nint db);

    // ─────────────────────────────────────────────────────────────────────────
    // Column families
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint* rocksdb_list_column_families(
        nint options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        out nuint lencf,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_list_column_families_destroy(nint* list, nuint len);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_create_column_family(
        nint db,
        nint column_family_options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string column_family_name,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_create_column_family_with_ttl(
        nint db,
        nint column_family_options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string column_family_name,
        int ttl,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_drop_column_family(nint db, nint handle, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_get_default_column_family_handle(nint db);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_column_family_handle_destroy(nint cfh);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint rocksdb_column_family_handle_get_id(nint handle);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte* rocksdb_column_family_handle_get_name(nint handle, out nuint name_len);

    // ─────────────────────────────────────────────────────────────────────────
    // Write / Put / Delete / Merge
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_put(
        nint db, nint options,
        byte* key, nuint keylen,
        byte* val, nuint vallen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_put_cf(
        nint db, nint options,
        nint column_family,
        byte* key, nuint keylen,
        byte* val, nuint vallen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_delete(
        nint db, nint options,
        byte* key, nuint keylen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_delete_cf(
        nint db, nint options,
        nint column_family,
        byte* key, nuint keylen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_delete_range_cf(
        nint db, nint options,
        nint column_family,
        byte* start_key, nuint start_key_len,
        byte* end_key, nuint end_key_len,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_singledelete(
        nint db, nint options,
        byte* key, nuint keylen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_singledelete_cf(
        nint db, nint options,
        nint column_family,
        byte* key, nuint keylen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_merge(
        nint db, nint options,
        byte* key, nuint keylen,
        byte* val, nuint vallen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_merge_cf(
        nint db, nint options,
        nint column_family,
        byte* key, nuint keylen,
        byte* val, nuint vallen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_write(
        nint db, nint options,
        nint batch,
        ref nint errptr);

    // ─────────────────────────────────────────────────────────────────────────
    // Read / Get
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte* rocksdb_get(
        nint db, nint options,
        byte* key, nuint keylen,
        out nuint vallen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte* rocksdb_get_cf(
        nint db, nint options,
        nint column_family,
        byte* key, nuint keylen,
        out nuint vallen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_multi_get(
        nint db, nint options,
        nuint num_keys,
        byte** keys_list, nuint* keys_list_sizes,
        byte** values_list, nuint* values_list_sizes,
        nint* errs);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_multi_get_cf(
        nint db, nint options,
        nint* column_families,
        nuint num_keys,
        byte** keys_list, nuint* keys_list_sizes,
        byte** values_list, nuint* values_list_sizes,
        nint* errs);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_key_may_exist(
        nint db, nint options,
        byte* key, nuint key_len,
        byte** value, nuint* val_len,
        byte* timestamp, nuint timestamp_len,
        byte* value_found);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_key_may_exist_cf(
        nint db, nint options,
        nint column_family,
        byte* key, nuint key_len,
        byte** value, nuint* val_len,
        byte* timestamp, nuint timestamp_len,
        byte* value_found);

    // ─────────────────────────────────────────────────────────────────────────
    // Snapshot
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_create_snapshot(nint db);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_release_snapshot(nint db, nint snapshot);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_snapshot_get_sequence_number(nint snapshot);

    // ─────────────────────────────────────────────────────────────────────────
    // Iterator
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_create_iterator(nint db, nint options);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_create_iterator_cf(nint db, nint options, nint column_family);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_create_iterators(
        nint db, nint opts,
        nint* column_families,
        nint* iterators,
        nuint size,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_iter_destroy(nint iter);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_iter_valid(nint iter);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_iter_seek_to_first(nint iter);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_iter_seek_to_last(nint iter);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_iter_seek(nint iter, byte* k, nuint klen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_iter_seek_for_prev(nint iter, byte* k, nuint klen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_iter_next(nint iter);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_iter_prev(nint iter);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte* rocksdb_iter_key(nint iter, out nuint klen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte* rocksdb_iter_value(nint iter, out nuint vlen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_iter_get_error(nint iter, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_iter_refresh(nint iter, ref nint errptr);

    // ─────────────────────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_property_value(
        nint db,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string propname);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_property_int(
        nint db,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string propname,
        out ulong out_val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_property_value_cf(
        nint db, nint column_family,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string propname);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_property_int_cf(
        nint db, nint column_family,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string propname,
        out ulong out_val);

    // ─────────────────────────────────────────────────────────────────────────
    // Flush / Compact / WAL
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_flush(nint db, nint options, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_flush_cf(nint db, nint options, nint column_family, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_flush_wal(nint db, byte sync, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compact_range(
        nint db,
        byte* start_key, nuint start_key_len,
        byte* limit_key, nuint limit_key_len);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compact_range_cf(
        nint db, nint column_family,
        byte* start_key, nuint start_key_len,
        byte* limit_key, nuint limit_key_len);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compact_range_opt(
        nint db, nint opt,
        byte* start_key, nuint start_key_len,
        byte* limit_key, nuint limit_key_len);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compact_range_cf_opt(
        nint db, nint column_family, nint opt,
        byte* start_key, nuint start_key_len,
        byte* limit_key, nuint limit_key_len);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_disable_file_deletions(nint db, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_enable_file_deletions(nint db, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_try_catch_up_with_primary(nint db, ref nint errptr);

    // ─────────────────────────────────────────────────────────────────────────
    // Options
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_options_create();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_options_create_copy(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_destroy(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_increase_parallelism(nint opt, int total_threads);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_optimize_for_point_lookup(nint opt, ulong block_cache_size_mb);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_optimize_level_style_compaction(nint opt, ulong memtable_memory_budget);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_optimize_universal_style_compaction(nint opt, ulong memtable_memory_budget);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_create_if_missing(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_create_if_missing(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_create_missing_column_families(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_create_missing_column_families(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_error_if_exists(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_error_if_exists(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_paranoid_checks(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_paranoid_checks(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_write_buffer_size(nint opt, nuint val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint rocksdb_options_get_write_buffer_size(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_db_write_buffer_size(nint opt, nuint val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint rocksdb_options_get_db_write_buffer_size(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_max_open_files(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_max_open_files(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_max_total_wal_size(nint opt, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_options_get_max_total_wal_size(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_num_levels(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_num_levels(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_level0_file_num_compaction_trigger(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_level0_file_num_compaction_trigger(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_level0_slowdown_writes_trigger(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_level0_slowdown_writes_trigger(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_level0_stop_writes_trigger(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_level0_stop_writes_trigger(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_target_file_size_base(nint opt, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_options_get_target_file_size_base(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_max_bytes_for_level_base(nint opt, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_options_get_max_bytes_for_level_base(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_max_bytes_for_level_multiplier(nint opt, double val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double rocksdb_options_get_max_bytes_for_level_multiplier(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_max_write_buffer_number(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_max_write_buffer_number(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_min_write_buffer_number_to_merge(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_min_write_buffer_number_to_merge(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_max_background_jobs(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_max_background_jobs(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_max_background_compactions(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_max_background_compactions(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_max_background_flushes(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_max_background_flushes(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_compression(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_compression(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_bottommost_compression(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_bottommost_compression(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_compaction_style(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_compaction_style(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_disable_auto_compactions(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_disable_auto_compactions(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_block_based_table_factory(nint opt, nint table_options);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_db_log_dir(
        nint opt,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_wal_dir(
        nint opt,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_WAL_ttl_seconds(nint opt, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_options_get_WAL_ttl_seconds(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_WAL_size_limit_MB(nint opt, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_options_get_WAL_size_limit_MB(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_wal_recovery_mode(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_wal_recovery_mode(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_enable_statistics(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_options_statistics_get_string(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_use_direct_reads(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_use_direct_reads(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_use_direct_io_for_flush_and_compaction(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_use_direct_io_for_flush_and_compaction(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_allow_mmap_reads(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_allow_mmap_reads(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_allow_mmap_writes(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_allow_mmap_writes(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_use_fsync(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_use_fsync(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_keep_log_file_num(nint opt, nuint val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint rocksdb_options_get_keep_log_file_num(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_max_log_file_size(nint opt, nuint val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint rocksdb_options_get_max_log_file_size(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_stats_dump_period_sec(nint opt, uint val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint rocksdb_options_get_stats_dump_period_sec(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_bytes_per_sync(nint opt, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_options_get_bytes_per_sync(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_wal_bytes_per_sync(nint opt, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_options_get_wal_bytes_per_sync(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_allow_concurrent_memtable_write(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_allow_concurrent_memtable_write(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_max_subcompactions(nint opt, uint val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint rocksdb_options_get_max_subcompactions(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_ttl(nint opt, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_options_get_ttl(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_periodic_compaction_seconds(nint opt, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_options_get_periodic_compaction_seconds(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_max_manifest_file_size(nint opt, nuint val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint rocksdb_options_get_max_manifest_file_size(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_atomic_flush(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_atomic_flush(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_manual_wal_flush(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_manual_wal_flush(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_wal_compression(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_wal_compression(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_info_log_level(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_info_log_level(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_memtable_prefix_bloom_size_ratio(nint opt, double val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double rocksdb_options_get_memtable_prefix_bloom_size_ratio(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_level_compaction_dynamic_level_bytes(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_level_compaction_dynamic_level_bytes(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_prefix_extractor(nint opt, nint slice_transform);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_row_cache(nint opt, nint cache);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_ratelimiter(nint opt, nint limiter);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_write_buffer_manager(nint opt, nint wbm);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_compaction_pri(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_compaction_pri(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_prepare_for_bulk_load(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_enable_blob_files(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_enable_blob_files(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_min_blob_size(nint opt, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_options_get_min_blob_size(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_blob_file_size(nint opt, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_options_get_blob_file_size(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_enable_blob_gc(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_options_get_enable_blob_gc(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_statistics_level(nint opt, int level);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_options_get_statistics_level(nint opt);

    // ─────────────────────────────────────────────────────────────────────────
    // Read options
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_readoptions_create();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_destroy(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_verify_checksums(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_readoptions_get_verify_checksums(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_fill_cache(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_readoptions_get_fill_cache(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_snapshot(nint opt, nint snapshot);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_iterate_upper_bound(nint opt, byte* key, nuint keylen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_iterate_lower_bound(nint opt, byte* key, nuint keylen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_read_tier(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_readoptions_get_read_tier(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_tailing(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_readoptions_get_tailing(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_readahead_size(nint opt, nuint val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint rocksdb_readoptions_get_readahead_size(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_prefix_same_as_start(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_readoptions_get_prefix_same_as_start(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_pin_data(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_readoptions_get_pin_data(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_total_order_seek(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_readoptions_get_total_order_seek(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_async_io(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_readoptions_get_async_io(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_readoptions_set_ignore_range_deletions(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_readoptions_get_ignore_range_deletions(nint opt);

    // ─────────────────────────────────────────────────────────────────────────
    // Write options
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_writeoptions_create();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writeoptions_destroy(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writeoptions_set_sync(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_writeoptions_get_sync(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writeoptions_disable_WAL(nint opt, int disable);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_writeoptions_get_disable_WAL(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writeoptions_set_no_slowdown(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_writeoptions_get_no_slowdown(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writeoptions_set_low_pri(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_writeoptions_get_low_pri(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writeoptions_set_ignore_missing_column_families(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_writeoptions_get_ignore_missing_column_families(nint opt);

    // ─────────────────────────────────────────────────────────────────────────
    // Flush options
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_flushoptions_create();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_flushoptions_destroy(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_flushoptions_set_wait(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_flushoptions_get_wait(nint opt);

    // ─────────────────────────────────────────────────────────────────────────
    // Compact options
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_compactoptions_create();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compactoptions_destroy(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compactoptions_set_exclusive_manual_compaction(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compactoptions_set_bottommost_level_compaction(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compactoptions_set_change_level(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compactoptions_set_target_level(nint opt, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compactoptions_set_max_subcompactions(nint opt, int val);

    // ─────────────────────────────────────────────────────────────────────────
    // Block-based table options
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_block_based_options_create();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_destroy(nint options);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_block_size(nint options, nuint block_size);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_block_cache(nint options, nint block_cache);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_no_block_cache(nint options, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_filter_policy(nint options, nint filter_policy);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_whole_key_filtering(nint options, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_format_version(nint options, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_index_type(nint options, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_cache_index_and_filter_blocks(nint options, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_cache_index_and_filter_blocks_with_high_priority(nint options, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_pin_l0_filter_and_index_blocks_in_cache(nint options, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_checksum(nint options, sbyte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_block_size_deviation(nint options, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_block_restart_interval(nint options, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_partition_filters(nint options, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_metadata_block_size(nint options, ulong val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_use_delta_encoding(nint options, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_data_block_index_type(nint options, int val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_block_based_options_set_data_block_hash_ratio(nint options, double val);

    // ─────────────────────────────────────────────────────────────────────────
    // Cache
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_cache_create_lru(nuint capacity);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_cache_create_lru_with_strict_capacity_limit(nuint capacity);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_cache_create_hyper_clock(nuint capacity, nuint estimated_entry_charge);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_cache_destroy(nint cache);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_cache_set_capacity(nint cache, nuint capacity);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint rocksdb_cache_get_capacity(nint cache);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint rocksdb_cache_get_usage(nint cache);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint rocksdb_cache_get_pinned_usage(nint cache);

    // ─────────────────────────────────────────────────────────────────────────
    // Filter policy
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_filterpolicy_create_bloom(double bits_per_key);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_filterpolicy_create_bloom_full(double bits_per_key);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_filterpolicy_create_ribbon(double bloom_equivalent_bits_per_key);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_filterpolicy_create_ribbon_hybrid(
        double bloom_equivalent_bits_per_key,
        int bloom_before_level);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_filterpolicy_destroy(nint policy);

    // ─────────────────────────────────────────────────────────────────────────
    // Slice transform (prefix extractor)
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_slicetransform_create_fixed_prefix(nuint len);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_slicetransform_create_noop();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_slicetransform_destroy(nint st);

    // ─────────────────────────────────────────────────────────────────────────
    // Write batch
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_writebatch_create();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_writebatch_create_with_params(
        nuint reserved_bytes, nuint max_bytes,
        nuint protection_bytes_per_key, nuint default_cf_ts_sz);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_destroy(nint batch);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_clear(nint batch);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_writebatch_count(nint batch);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_put(
        nint batch,
        byte* key, nuint klen,
        byte* val, nuint vlen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_put_cf(
        nint batch, nint column_family,
        byte* key, nuint klen,
        byte* val, nuint vlen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_merge(
        nint batch,
        byte* key, nuint klen,
        byte* val, nuint vlen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_merge_cf(
        nint batch, nint column_family,
        byte* key, nuint klen,
        byte* val, nuint vlen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_delete(nint batch, byte* key, nuint klen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_delete_cf(
        nint batch, nint column_family,
        byte* key, nuint klen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_singledelete(nint batch, byte* key, nuint klen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_singledelete_cf(
        nint batch, nint column_family,
        byte* key, nuint klen);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_delete_range(
        nint batch,
        byte* start_key, nuint start_key_len,
        byte* end_key, nuint end_key_len);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_delete_range_cf(
        nint batch, nint column_family,
        byte* start_key, nuint start_key_len,
        byte* end_key, nuint end_key_len);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_put_log_data(nint batch, byte* blob, nuint len);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_set_save_point(nint batch);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_rollback_to_save_point(nint batch, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_writebatch_pop_save_point(nint batch, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte* rocksdb_writebatch_data(nint batch, out nuint size);

    // ─────────────────────────────────────────────────────────────────────────
    // Rate limiter
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_ratelimiter_create(
        long rate_bytes_per_sec,
        long refill_period_us,
        int fairness);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_ratelimiter_destroy(nint limiter);

    // ─────────────────────────────────────────────────────────────────────────
    // Env
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_create_default_env();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_create_mem_env();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_env_set_background_threads(nint env, int n);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_env_set_high_priority_background_threads(nint env, int n);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_env_destroy(nint env);

    // ─────────────────────────────────────────────────────────────────────────
    // Checkpoint
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_checkpoint_object_create(nint db, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_checkpoint_create(
        nint checkpoint,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string checkpoint_dir,
        ulong log_size_for_flush,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_checkpoint_object_destroy(nint checkpoint);

    // ─────────────────────────────────────────────────────────────────────────
    // Backup engine
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_backup_engine_open(
        nint options,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_backup_engine_create_new_backup(nint be, nint db, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_backup_engine_create_new_backup_flush(
        nint be, nint db, byte flush_before_backup, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_backup_engine_purge_old_backups(nint be, uint num_backups_to_keep, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_restore_options_create();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_restore_options_destroy(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_backup_engine_restore_db_from_latest_backup(
        nint be,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string db_dir,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string wal_dir,
        nint restore_options,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_backup_engine_get_backup_info(nint be);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rocksdb_backup_engine_info_count(nint info);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern long rocksdb_backup_engine_info_timestamp(nint info, int index);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint rocksdb_backup_engine_info_backup_id(nint info, int index);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong rocksdb_backup_engine_info_size(nint info, int index);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint rocksdb_backup_engine_info_number_files(nint info, int index);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_backup_engine_info_destroy(nint info);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_backup_engine_close(nint be);

    // ─────────────────────────────────────────────────────────────────────────
    // SST file writer / ingest
    // ─────────────────────────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_envoptions_create();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_envoptions_destroy(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_sstfilewriter_create(nint env, nint io_options);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_sstfilewriter_open(
        nint writer,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_sstfilewriter_put(
        nint writer,
        byte* key, nuint keylen,
        byte* val, nuint vallen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_sstfilewriter_merge(
        nint writer,
        byte* key, nuint keylen,
        byte* val, nuint vallen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_sstfilewriter_delete(
        nint writer,
        byte* key, nuint keylen,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_sstfilewriter_finish(nint writer, ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_sstfilewriter_file_size(nint writer, out ulong file_size);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_sstfilewriter_destroy(nint writer);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_ingestexternalfileoptions_create();

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_ingestexternalfileoptions_destroy(nint opt);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_ingestexternalfileoptions_set_move_files(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_ingestexternalfileoptions_set_snapshot_consistency(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_ingestexternalfileoptions_set_allow_global_seqno(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_ingestexternalfileoptions_set_allow_blocking_flush(nint opt, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_ingest_external_file(
        nint db,
        string[] file_list, nuint list_len,
        nint opt,
        ref nint errptr);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_ingest_external_file_cf(
        nint db, nint handle,
        string[] file_list, nuint list_len,
        nint opt,
        ref nint errptr);

    // ─────────────────────────────────────────────────────────────────────────
    // Compaction filter
    // ─────────────────────────────────────────────────────────────────────────

    // All three callback parameters are raw function pointers (nint / void*).
    // The managed delegates must be kept alive by the caller (as instance fields)
    // for as long as the native filter exists.
    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_compactionfilter_create(
        nint state,
        nint destructor,     // void (*)(void*)
        nint filter,         // unsigned char (*)(void*, int, char*, size_t, char*, size_t, char**, size_t*, unsigned char*)
        nint name);          // const char* (*)(void*)

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compactionfilter_set_ignore_snapshots(
        nint filter, byte val);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compactionfilter_destroy(nint filter);

    // ── Compaction filter context ─────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_compactionfiltercontext_is_full_compaction(nint context);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte rocksdb_compactionfiltercontext_is_manual_compaction(nint context);

    // ── Compaction filter factory ─────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint rocksdb_compactionfilterfactory_create(
        nint state,
        nint destructor,               // void (*)(void*)
        nint create_compaction_filter, // rocksdb_compactionfilter_t* (*)(void*, rocksdb_compactionfiltercontext_t*)
        nint name);                    // const char* (*)(void*)

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_compactionfilterfactory_destroy(nint factory);

    // ── Options setters ───────────────────────────────────────────────────────

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_compaction_filter(
        nint options, nint filter);

    [DllImport(NativeResolver.LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rocksdb_options_set_compaction_filter_factory(
        nint options, nint factory);

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Throws a <see cref="RocksDbException"/> if <paramref name="errPtr"/> is non-zero,
    /// freeing the native error string in the process.
    /// </summary>
    internal static void ThrowOnError(nint errPtr)
    {
        if (errPtr == nint.Zero)
            return;

        string? msg = Marshal.PtrToStringUTF8(errPtr);
        rocksdb_free(errPtr);
        throw new RocksDbException(msg ?? "Unknown RocksDB error");
    }

    /// <summary>
    /// Reads a native UTF-8 string pointer (not owned) into a managed string.
    /// </summary>
    internal static string? PtrToStringUTF8(byte* ptr, nuint len)
    {
        if (ptr == null)
            return null;
        return System.Text.Encoding.UTF8.GetString(ptr, (int)len);
    }
}
