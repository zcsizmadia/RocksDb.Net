using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// Mapped from rocksdb::FlushReason in listener.h
/// </summary>
public enum FlushReason : uint
{
    Others = 0x00,
    GetLiveFiles = 0x01,
    ShutDown = 0x02,
    ExternalFileIngestion = 0x03,
    ManualCompaction = 0x04,
    ManualFlush = 0x05,
    CheckPoint = 0x06,
    TableMetaWrite = 0x07,
    WalFull = 0x08,
    WriteBufferFull = 0x09,
    WriteBufferManager = 0x0a,
    BufferLimit = 0x0b,
    SleepInterval = 0x0c,
}

/// <summary>
/// Mapped from rocksdb::BackgroundErrorReason in status.h
/// </summary>
public enum BackgroundErrorReason : uint
{
    Flush = 0,
    Compaction = 1,
    WriteCallback = 2,
    MemTableSealing = 3,
    ManifestWrite = 4,
    FlushNoSpace = 5,
    CompactionNoSpace = 6,
}

/// <summary>Describes the write stall condition of a column family.</summary>
public enum WriteStallCondition : int
{
    Normal = 0,
    Delayed = 1,
    Stopped = 2
}

/// <summary>Information about a completed flush job.</summary>
public sealed record FlushJobInfo(
    string? ColumnFamilyName,
    string? FilePath,
    bool TriggeredWritesSlowdown,
    bool TriggeredWritesStop,
    ulong LargestSeqno,
    ulong SmallestSeqno,
    FlushReason FlushReason);

/// <summary>Information about a completed compaction job.</summary>
public sealed record CompactionJobInfo(
    string? ColumnFamilyName,
    string[] InputFiles,
    string[] OutputFiles,
    ulong TotalInputBytes,
    ulong TotalOutputBytes,
    uint InputRecords,
    uint OutputRecords,
    ulong ElapsedMicros,
    CompactionReason CompactionReason,
    string? Status);

/// <summary>Information about a sub-compaction job.</summary>
public sealed record SubCompactionJobInfo(
    string? ColumnFamilyName,
    string? Status);

/// <summary>Information about an external file ingestion event.</summary>
public sealed record ExternalFileIngestionInfo(
    string? ColumnFamilyName,
    string? InternalFilePath);

/// <summary>Information about a background error.</summary>
public sealed record BackgroundErrorInfo(
    BackgroundErrorReason Reason,
    string? Message);

/// <summary>Information about a write stall condition change.</summary>
public sealed record WriteStallInfo(
    string? ColumnFamilyName,
    WriteStallCondition Condition,
    WriteStallCondition PreviousCondition);

/// <summary>Information about a sealed memtable.</summary>
public sealed record MemTableInfo(
    string? ColumnFamilyName,
    ulong FirstSeqno,
    ulong EarliestSeqno,
    ulong NumEntries,
    ulong NumDeletes);

/// <summary>
/// Base class for receiving database event notifications such as flushes,
/// compactions, and background errors. Override the virtual methods for
/// events you want to observe.
/// </summary>
public abstract class EventListener : RocksDbHandle
{
    // ── Unmanaged delegate types ─────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestructorDelegate(nint state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnFlushBeginDelegate(
        nint state, nint db, nint info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnFlushCompletedDelegate(
        nint state, nint db, nint info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnCompactionBeginDelegate(
        nint state, nint db, nint info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnCompactionCompletedDelegate(
        nint state, nint db, nint info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnSubCompactionBeginDelegate(
        nint state, nint info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnSubCompactionCompletedDelegate(
        nint state, nint info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnExternalFileIngestedDelegate(
        nint state, nint db, nint info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnBackgroundErrorDelegate(
        nint state, uint reason, nint info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnStallConditionsChangedDelegate(
        nint state, nint info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnMemTableSealedDelegate(
        nint state, nint info);

    // Delegate instances kept as fields to prevent GC from collecting the
    // objects while the native side still holds function pointers into them.
    private readonly DestructorDelegate _destructorDelegate;
    private readonly OnFlushBeginDelegate _onFlushBeginDelegate;
    private readonly OnFlushCompletedDelegate _onFlushCompletedDelegate;
    private readonly OnCompactionBeginDelegate _onCompactionBeginDelegate;
    private readonly OnCompactionCompletedDelegate _onCompactionCompletedDelegate;
    private readonly OnSubCompactionBeginDelegate _onSubCompactionBeginDelegate;
    private readonly OnSubCompactionCompletedDelegate _onSubCompactionCompletedDelegate;
    private readonly OnExternalFileIngestedDelegate _onExternalFileIngestedDelegate;
    private readonly OnBackgroundErrorDelegate _onBackgroundErrorDelegate;
    private readonly OnStallConditionsChangedDelegate _onStallConditionsChangedDelegate;
    private readonly OnMemTableSealedDelegate _onMemTableSealedDelegate;

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    private static void DestructorCallback(nint state)
    {
        // RocksDB called this via shared_ptr deleter — the native handle is now freed.
        // Transfer ownership so that our Dispose() won't call rocksdb_eventlistener_destroy again,
        // then release the GC root created for native callbacks.
        var self = GetSelfFromPinnedIntPtr<EventListener>(state);
        self.TransferOwnership();
        self.UnpinGarbageCollector();
    }

    private static void OnFlushBeginCallback(nint state, nint db, nint info)
    {
        var self = SelfFromState(state);

        self.OnFlushBegin(CreateFlushJobInfo(info));
    }

    private static void OnFlushCompletedCallback(nint state, nint db, nint info)
    {
        var self = SelfFromState(state);

        self.OnFlushCompleted(CreateFlushJobInfo(info));
    }

    private static void OnCompactionBeginCallback(nint state, nint db, nint info)
    {
        var self = SelfFromState(state);

        self.OnCompactionBegin(CreateCompactionJobInfo(info));
    }

    private static void OnCompactionCompletedCallback(nint state, nint db, nint info)
    {
        var self = SelfFromState(state);

        self.OnCompactionCompleted(CreateCompactionJobInfo(info));
    }

    private static void OnSubCompactionBeginCallback(nint state, nint info)
    {
        var self = SelfFromState(state);

        self.OnSubCompactionBegin(CreateSubCompactionJobInfo(info));
    }

    private static void OnSubCompactionCompletedCallback(nint state, nint info)
    {
        var self = SelfFromState(state);

        self.OnSubCompactionCompleted(CreateSubCompactionJobInfo(info));
    }

    private static void OnExternalFileIngestedCallback(nint state, nint db, nint info)
    {
        var self = SelfFromState(state);

        self.OnExternalFileIngested(CreateExternalFileIngestionInfo(info));
    }

    private static void OnBackgroundErrorCallback(nint state, uint reason, nint info)
    {
        var self = SelfFromState(state);

        self.OnBackgroundError(CreateBackgroundErrorInfo(reason, info));
    }

    private static void OnStallConditionsChangedCallback(nint state, nint info)
    {
        var self = SelfFromState(state);

        self.OnStallConditionsChanged(CreateWriteStallInfo(info));
    }

    private static void OnMemTableSealedCallback(nint state, nint info)
    {
        var self = SelfFromState(state);

        self.OnMemTableSealed(CreateMemTableInfo(info));
    }

    private static EventListener SelfFromState(nint state) => GetSelfFromPinnedIntPtr<EventListener>(state);

    // ── Construction ─────────────────────────────────────────────────────────

    protected EventListener()
    {
        // Pin this instance so that the C++ callbacks can access it via the state pointer
        PinGarbageCollector();

        _destructorDelegate = DestructorCallback;
        _onFlushBeginDelegate = OnFlushBeginCallback;
        _onFlushCompletedDelegate = OnFlushCompletedCallback;
        _onCompactionBeginDelegate = OnCompactionBeginCallback;
        _onCompactionCompletedDelegate = OnCompactionCompletedCallback;
        _onSubCompactionBeginDelegate = OnSubCompactionBeginCallback;
        _onSubCompactionCompletedDelegate = OnSubCompactionCompletedCallback;
        _onExternalFileIngestedDelegate = OnExternalFileIngestedCallback;
        _onBackgroundErrorDelegate = OnBackgroundErrorCallback;
        _onStallConditionsChangedDelegate = OnStallConditionsChangedCallback;
        _onMemTableSealedDelegate = OnMemTableSealedCallback;

        Handle = NativeMethods.rocksdb_eventlistener_create(
            GetPinnedIntPtr(),
            Marshal.GetFunctionPointerForDelegate(_destructorDelegate),
            Marshal.GetFunctionPointerForDelegate(_onFlushBeginDelegate),
            Marshal.GetFunctionPointerForDelegate(_onFlushCompletedDelegate),
            Marshal.GetFunctionPointerForDelegate(_onCompactionBeginDelegate),
            Marshal.GetFunctionPointerForDelegate(_onCompactionCompletedDelegate),
            Marshal.GetFunctionPointerForDelegate(_onSubCompactionBeginDelegate),
            Marshal.GetFunctionPointerForDelegate(_onSubCompactionCompletedDelegate),
            Marshal.GetFunctionPointerForDelegate(_onExternalFileIngestedDelegate),
            Marshal.GetFunctionPointerForDelegate(_onBackgroundErrorDelegate),
            Marshal.GetFunctionPointerForDelegate(_onStallConditionsChangedDelegate),
            Marshal.GetFunctionPointerForDelegate(_onMemTableSealedDelegate));
    }

    // ── Virtual methods ───────────────────────────────────────────────

    /// <summary>Called when a flush job begins.</summary>
    public virtual void OnFlushBegin(FlushJobInfo info)
    {
    }

    /// <summary>Called when a flush job completes.</summary>
    public virtual void OnFlushCompleted(FlushJobInfo info)
    {
    }

    /// <summary>Called when a compaction job begins.</summary>
    public virtual void OnCompactionBegin(CompactionJobInfo info)
    {
    }

    /// <summary>Called when a compaction job completes.</summary>
    public virtual void OnCompactionCompleted(CompactionJobInfo info)
    {
    }

    /// <summary>Called when a sub-compaction job begins.</summary>
    public virtual void OnSubCompactionBegin(SubCompactionJobInfo info)
    {
    }

    /// <summary>Called when a sub-compaction job completes.</summary>
    public virtual void OnSubCompactionCompleted(SubCompactionJobInfo info)
    {
    }

    /// <summary>Called when an external file has been ingested.</summary>
    public virtual void OnExternalFileIngested(ExternalFileIngestionInfo info)
    {
    }

    /// <summary>Called when a background error occurs.</summary>
    public virtual void OnBackgroundError(BackgroundErrorInfo info)
    {
    }

    /// <summary>Called when write stall conditions change for a column family.</summary>
    public virtual void OnStallConditionsChanged(WriteStallInfo info)
    {
    }

    /// <summary>Called when a memtable is sealed.</summary>
    public virtual void OnMemTableSealed(MemTableInfo info)
    {
    }

    // ── FlushJobInfo ───────────────────────────────────────────────────────

    private static unsafe FlushJobInfo CreateFlushJobInfo(nint info)
    {
        nuint length;
        byte* str = NativeMethods.rocksdb_flushjobinfo_cf_name(info, &length);
        var columnFamilyName = NativeMethods.PtrToStringUTF8(str, length);

        str = NativeMethods.rocksdb_flushjobinfo_file_path(info, &length);
        var filePath = NativeMethods.PtrToStringUTF8(str, length);

        return new FlushJobInfo(
            ColumnFamilyName: columnFamilyName,
            FilePath: filePath,
            TriggeredWritesSlowdown: NativeMethods.rocksdb_flushjobinfo_triggered_writes_slowdown(info) != 0,
            TriggeredWritesStop: NativeMethods.rocksdb_flushjobinfo_triggered_writes_stop(info) != 0,
            LargestSeqno: NativeMethods.rocksdb_flushjobinfo_largest_seqno(info),
            SmallestSeqno: NativeMethods.rocksdb_flushjobinfo_smallest_seqno(info),
            FlushReason: (FlushReason)NativeMethods.rocksdb_flushjobinfo_flush_reason(info));
    }

    // ── CompactionJobInfo ──────────────────────────────────────────────────

    private static unsafe CompactionJobInfo CreateCompactionJobInfo(nint info)
    {
        nuint length;
        byte* str = NativeMethods.rocksdb_compactionjobinfo_cf_name(info, &length);
        var columnFamilyName = NativeMethods.PtrToStringUTF8(str, length);

        nuint inputCount = NativeMethods.rocksdb_compactionjobinfo_input_files_count(info);
        var inputFiles = new string[inputCount];
        for (nuint i = 0; i < inputCount; i++)
        {
            nuint len;
            byte* p = NativeMethods.rocksdb_compactionjobinfo_input_file_at(info, i, &len);
            inputFiles[i] = NativeMethods.PtrToStringUTF8(p, len) ?? string.Empty;
        }

        nuint outputCount = NativeMethods.rocksdb_compactionjobinfo_output_files_count(info);
        var outputFiles = new string[outputCount];
        for (nuint i = 0; i < outputCount; i++)
        {
            nuint len;
            byte* p = NativeMethods.rocksdb_compactionjobinfo_output_file_at(info, i, &len);
            outputFiles[i] = NativeMethods.PtrToStringUTF8(p, len) ?? string.Empty;
        }

        nint errptr = default;
        NativeMethods.rocksdb_compactionjobinfo_status(info, ref errptr);
        var status = errptr != nint.Zero ? Marshal.PtrToStringAnsi(errptr) : "OK";

        return new CompactionJobInfo(
            ColumnFamilyName: columnFamilyName,
            InputFiles: inputFiles,
            OutputFiles: outputFiles,
            TotalInputBytes: NativeMethods.rocksdb_compactionjobinfo_total_input_bytes(info),
            TotalOutputBytes: NativeMethods.rocksdb_compactionjobinfo_total_output_bytes(info),
            InputRecords: (uint)NativeMethods.rocksdb_compactionjobinfo_input_records(info),
            OutputRecords: (uint)NativeMethods.rocksdb_compactionjobinfo_output_records(info),
            ElapsedMicros: NativeMethods.rocksdb_compactionjobinfo_elapsed_micros(info),
            CompactionReason: (CompactionReason)NativeMethods.rocksdb_compactionjobinfo_compaction_reason(info),
            Status: status);
    }

    // ── SubCompactionJobInfo ───────────────────────────────────────────────

    private static unsafe SubCompactionJobInfo CreateSubCompactionJobInfo(nint info)
    {
        nuint length;
        byte* str = NativeMethods.rocksdb_subcompactionjobinfo_cf_name(info, &length);
        var columnFamilyName = NativeMethods.PtrToStringUTF8(str, length);

        byte* errStr = null;
        NativeMethods.rocksdb_subcompactionjobinfo_status(info, &errStr);
        var status = errStr != null ? Marshal.PtrToStringAnsi((nint)errStr) : "OK";

        return new SubCompactionJobInfo(columnFamilyName, status);
    }

    // ── ExternalFileIngestionInfo ──────────────────────────────────────────

    private static unsafe ExternalFileIngestionInfo CreateExternalFileIngestionInfo(nint info)
    {
        nuint length;
        byte* str = NativeMethods.rocksdb_externalfileingestioninfo_cf_name(info, &length);
        var columnFamilyName = NativeMethods.PtrToStringUTF8(str, length);

        str = NativeMethods.rocksdb_externalfileingestioninfo_internal_file_path(info, &length);
        var internalPath = NativeMethods.PtrToStringUTF8(str, length);

        return new ExternalFileIngestionInfo(
            ColumnFamilyName: columnFamilyName,
            InternalFilePath: internalPath);
    }

    // ── BackgroundErrorInfo ─────────────────────────────────────────────────────

    private static BackgroundErrorInfo CreateBackgroundErrorInfo(uint reason, nint statusPtr)
    {
        // The C API for status_ptr returns the error message via a char** errptr
        nint errptr = default;
        NativeMethods.rocksdb_status_ptr_get_error(statusPtr, ref errptr);

        // Standard RocksDb C error strings are allocated via strdup and must be freed,
        // but in this specific callback context, check if your NativeMethods.PtrToStringUTF8 
        // handles the lifecycle or if you need Marshal.PtrToStringAnsi.
        var message = errptr != nint.Zero ? Marshal.PtrToStringAnsi(errptr) : null;

        // After capturing the string, we MUST free the memory allocated by SaveError in c.cc
        if (errptr != nint.Zero)
        {
            NativeMethods.rocksdb_free(errptr);
        }

        return new BackgroundErrorInfo(
            Reason: (BackgroundErrorReason)reason,
            Message: message);
    }

    // ── WriteStallInfo ─────────────────────────────────────────────────────

    private static unsafe WriteStallInfo CreateWriteStallInfo(nint info)
    {
        nuint length;
        byte* str = NativeMethods.rocksdb_writestallinfo_cf_name(info, &length);
        var columnFamilyName = NativeMethods.PtrToStringUTF8(str, length);

        // Fetch pointers to the conditions
        nint curPtr = NativeMethods.rocksdb_writestallinfo_cur(info);
        nint prevPtr = NativeMethods.rocksdb_writestallinfo_prev(info);

        // Dereference the pointers to get the actual enum values
        // Note: The C API typically returns a pointer to the internal enum field.
        return new WriteStallInfo(
            ColumnFamilyName: columnFamilyName,
            Condition: (WriteStallCondition)Marshal.ReadInt32(curPtr),
            PreviousCondition: (WriteStallCondition)Marshal.ReadInt32(prevPtr));
    }

    // ── MemTableInfo ───────────────────────────────────────────────────────

    private static unsafe MemTableInfo CreateMemTableInfo(nint info)
    {
        nuint length;
        byte* str = NativeMethods.rocksdb_memtableinfo_cf_name(info, &length);
        var columnFamilyName = NativeMethods.PtrToStringUTF8(str, length);

        return new MemTableInfo(
            ColumnFamilyName: columnFamilyName,
            FirstSeqno: NativeMethods.rocksdb_memtableinfo_first_seqno(info),
            EarliestSeqno: NativeMethods.rocksdb_memtableinfo_earliest_seqno(info),
            NumEntries: NativeMethods.rocksdb_memtableinfo_num_entries(info),
            NumDeletes: NativeMethods.rocksdb_memtableinfo_num_deletes(info));
    }

    // --

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_eventlistener_destroy(Handle);
    }
}