using System.Runtime.InteropServices;

using RocksDbNet.Extensions;

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
    FlushReason FlushReason)
{
    /// <summary>
    /// Properties of the SST file this flush produced, or <c>null</c> if RocksDb
    /// reported none.
    /// </summary>
    public TableProperties? TableProperties { get; init; }

    /// <summary>Blob files created by this flush. Empty when blob files are disabled.</summary>
    public IReadOnlyList<BlobFileAdditionInfo> BlobFileAdditions { get; init; } = [];

    /// <summary>Identifier of the flush job, unique within the database's lifetime.</summary>
    public int JobId { get; init; }

    /// <summary>Identifier of the RocksDb background thread that ran the flush.</summary>
    public ulong ThreadId { get; init; }

    /// <summary>Identifier of the column family that was flushed.</summary>
    public uint ColumnFamilyId { get; init; }

    /// <summary>File number of the SST file this flush produced.</summary>
    public ulong FileNumber { get; init; }

    /// <summary>
    /// File number of the oldest blob file the new SST references, or 0 when it
    /// references none.
    /// </summary>
    public ulong OldestBlobFileNumber { get; init; }

    /// <summary>Compression applied to any blob files this flush wrote.</summary>
    public Compression BlobCompressionType { get; init; }
}

/// <summary>Information about a completed compaction job.</summary>
public sealed record CompactionJobInfo(
    string? ColumnFamilyName,
    string[] InputFiles,
    string[] OutputFiles,
    ulong TotalInputBytes,
    ulong TotalOutputBytes,
    uint InputRecords,
    uint OutputRecords,
    TimeSpan Elapsed,
    ulong NumOfCorruptKeys,
    int BaseInputLevel,
    CompactionReason CompactionReason,
    string? Status)
{
    /// <summary>
    /// Detailed statistics for the compaction, or <c>null</c> if RocksDb reported
    /// none. Supersedes the individual totals on this record, which remain for
    /// compatibility.
    /// </summary>
    public CompactionJobStats? Stats { get; init; }

    /// <summary>Blob files created by this compaction. Empty when blob files are disabled.</summary>
    public IReadOnlyList<BlobFileAdditionInfo> BlobFileAdditions { get; init; } = [];

    /// <summary>Blob-file garbage discovered by this compaction.</summary>
    public IReadOnlyList<BlobFileGarbageInfo> BlobFileGarbage { get; init; } = [];

    /// <summary>Identifier of the compaction job, unique within the database's lifetime.</summary>
    public int JobId { get; init; }

    /// <summary>Identifier of the RocksDb background thread that ran the compaction.</summary>
    public ulong ThreadId { get; init; }

    /// <summary>Identifier of the column family that was compacted.</summary>
    public uint ColumnFamilyId { get; init; }

    /// <summary>
    /// <c>true</c> when the compaction was cancelled or otherwise did not finish.
    /// The other values describe however much work it did before stopping.
    /// </summary>
    public bool Aborted { get; init; }

    /// <summary>Compression applied to the SST files this compaction wrote.</summary>
    public Compression Compression { get; init; }

    /// <summary>Compression applied to any blob files this compaction wrote.</summary>
    public Compression BlobCompressionType { get; init; }

    /// <summary>
    /// Number of level-0 files in the column family around the time of the
    /// compaction. RocksDb documents this as the count "right before and after"
    /// the compaction, and in practice a compaction that drains level 0 reports
    /// 0 here.
    /// </summary>
    public int NumL0Files { get; init; }

    /// <summary>Level and file number for each input file.</summary>
    public IReadOnlyList<CompactionFileInfo> InputFileInfos { get; init; } = [];

    /// <summary>Level and file number for each output file.</summary>
    public IReadOnlyList<CompactionFileInfo> OutputFileInfos { get; init; } = [];

    /// <summary>
    /// Table properties for the files involved, keyed by file name. RocksDb
    /// includes both inputs and outputs.
    /// </summary>
    public IReadOnlyDictionary<string, TableProperties> TablePropertiesByFile { get; init; }
        = new Dictionary<string, TableProperties>();
}

/// <summary>Information about a sub-compaction job.</summary>
public sealed record SubCompactionJobInfo(
    string? ColumnFamilyName,
    string? Status)
{
    /// <summary>Identifier of the parent compaction job.</summary>
    public int JobId { get; init; }

    /// <summary>Identifier of this sub-compaction within its parent job.</summary>
    public int SubCompactionJobId { get; init; }

    /// <summary>Identifier of the column family being compacted.</summary>
    public uint ColumnFamilyId { get; init; }

    /// <summary>Compression applied to the SST files this sub-compaction wrote.</summary>
    public Compression Compression { get; init; }

    /// <summary>Compression applied to any blob files this sub-compaction wrote.</summary>
    public Compression BlobCompressionType { get; init; }

    /// <summary>
    /// Statistics for this sub-compaction, or <c>null</c> if RocksDb reported
    /// none.
    /// </summary>
    public CompactionJobStats? Stats { get; init; }
}

/// <summary>Information about an external file ingestion event.</summary>
public sealed record ExternalFileIngestionInfo(
    string? ColumnFamilyName,
    string? InternalFilePath)
{
    /// <summary>Path the file was ingested from.</summary>
    public string? ExternalFilePath { get; init; }

    /// <summary>
    /// Sequence number assigned to every key in the ingested file, or 0 when
    /// RocksDb did not need to assign one.
    /// </summary>
    public ulong GlobalSeqno { get; init; }

    /// <summary>
    /// Properties of the ingested file, or <c>null</c> if RocksDb reported none.
    /// </summary>
    public TableProperties? TableProperties { get; init; }
}

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
    ulong NumDeletes)
{
    /// <summary>
    /// The newest user-defined timestamp in the sealed memtable, as raw bytes,
    /// or empty when the column family does not use them. The encoding is the
    /// application's own, so RocksDb passes it through untouched.
    /// </summary>
    public byte[] NewestUdt { get; init; } = [];
}

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

    // Whether the derived class overrides each event, decided once in the
    // constructor. See the comment there for why this cannot be expressed by
    // passing a null callback to RocksDb.
    private readonly bool _hasOnFlushBegin;
    private readonly bool _hasOnFlushCompleted;
    private readonly bool _hasOnCompactionBegin;
    private readonly bool _hasOnCompactionCompleted;
    private readonly bool _hasOnSubCompactionBegin;
    private readonly bool _hasOnSubCompactionCompleted;
    private readonly bool _hasOnExternalFileIngested;
    private readonly bool _hasOnBackgroundError;
    private readonly bool _hasOnStallConditionsChanged;
    private readonly bool _hasOnMemTableSealed;

    /// <summary>
    /// Invokes a listener method when the derived class overrides it, keeping any
    /// exception it throws from reaching native code. RocksDb ignores the outcome
    /// of these notifications, so reporting and swallowing the exception does not
    /// change any data.
    /// </summary>
    /// <remarks>
    /// Both delegate arguments are always <c>static</c> lambdas, so the compiler
    /// caches one instance per call site and this adds no per-event allocation.
    /// </remarks>
    private static void Notify(
        string callbackName,
        nint state,
        nint info,
        Func<EventListener, bool> isOverridden,
        Action<EventListener, nint> body)
    {
        try
        {
            EventListener self = SelfFromState(state);

            if (isOverridden(self))
            {
                body(self, info);
            }
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report(callbackName, ex);
        }
    }

    private static void DestructorCallback(nint state)
    {
        try
        {
            // RocksDB called this via shared_ptr deleter — the native handle is now freed.
            // Transfer ownership so that our Dispose() won't call rocksdb_eventlistener_destroy again,
            // then release the GC root created for native callbacks.
            var self = GetSelfFromPinnedIntPtr<EventListener>(state);
            self.TransferOwnership();
            self.UnpinGarbageCollector();
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report("EventListener destructor", ex);
        }
    }

    private static void OnFlushBeginCallback(nint state, nint db, nint info)
        => Notify(nameof(OnFlushBegin), state, info,
            static self => self._hasOnFlushBegin,
            static (self, i) => self.OnFlushBegin(CreateFlushJobInfo(i)));

    private static void OnFlushCompletedCallback(nint state, nint db, nint info)
        => Notify(nameof(OnFlushCompleted), state, info,
            static self => self._hasOnFlushCompleted,
            static (self, i) => self.OnFlushCompleted(CreateFlushJobInfo(i)));

    private static void OnCompactionBeginCallback(nint state, nint db, nint info)
        => Notify(nameof(OnCompactionBegin), state, info,
            static self => self._hasOnCompactionBegin,
            static (self, i) => self.OnCompactionBegin(CreateCompactionJobInfo(i)));

    private static void OnCompactionCompletedCallback(nint state, nint db, nint info)
        => Notify(nameof(OnCompactionCompleted), state, info,
            static self => self._hasOnCompactionCompleted,
            static (self, i) => self.OnCompactionCompleted(CreateCompactionJobInfo(i)));

    private static void OnSubCompactionBeginCallback(nint state, nint info)
        => Notify(nameof(OnSubCompactionBegin), state, info,
            static self => self._hasOnSubCompactionBegin,
            static (self, i) => self.OnSubCompactionBegin(CreateSubCompactionJobInfo(i)));

    private static void OnSubCompactionCompletedCallback(nint state, nint info)
        => Notify(nameof(OnSubCompactionCompleted), state, info,
            static self => self._hasOnSubCompactionCompleted,
            static (self, i) => self.OnSubCompactionCompleted(CreateSubCompactionJobInfo(i)));

    private static void OnExternalFileIngestedCallback(nint state, nint db, nint info)
        => Notify(nameof(OnExternalFileIngested), state, info,
            static self => self._hasOnExternalFileIngested,
            static (self, i) => self.OnExternalFileIngested(CreateExternalFileIngestionInfo(i)));

    private static void OnStallConditionsChangedCallback(nint state, nint info)
        => Notify(nameof(OnStallConditionsChanged), state, info,
            static self => self._hasOnStallConditionsChanged,
            static (self, i) => self.OnStallConditionsChanged(CreateWriteStallInfo(i)));

    private static void OnMemTableSealedCallback(nint state, nint info)
        => Notify(nameof(OnMemTableSealed), state, info,
            static self => self._hasOnMemTableSealed,
            static (self, i) => self.OnMemTableSealed(CreateMemTableInfo(i)));

    private static void OnBackgroundErrorCallback(nint state, uint reason, nint info)
    {
        try
        {
            EventListener self = SelfFromState(state);

            if (self._hasOnBackgroundError)
            {
                self.OnBackgroundError(CreateBackgroundErrorInfo(reason, info));
            }
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report(nameof(OnBackgroundError), ex);
        }
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

        // Skip work for events the derived class does not care about, so that no
        // info object is built and no virtual call is made for them.
        //
        // The check has to happen on this side of the boundary. Handing RocksDb a
        // null function pointer is not a way to opt out: rocksdb_eventlistener_t
        // in db/c.cc overrides all ten EventListener virtuals and invokes the
        // stored pointer with no null check, so a null crashes the process the
        // first time that event fires. See issue #35.
        //
        // Detecting an override uses reflection to see whether the declaring type
        // is still this base class. It runs once per instance, and listeners are
        // created infrequently, so the cost is irrelevant.
        _hasOnFlushBegin = this.CheckIfMethodOverridden<EventListener>(nameof(OnFlushBegin));
        _hasOnFlushCompleted = this.CheckIfMethodOverridden<EventListener>(nameof(OnFlushCompleted));
        _hasOnCompactionBegin = this.CheckIfMethodOverridden<EventListener>(nameof(OnCompactionBegin));
        _hasOnCompactionCompleted = this.CheckIfMethodOverridden<EventListener>(nameof(OnCompactionCompleted));
        _hasOnSubCompactionBegin = this.CheckIfMethodOverridden<EventListener>(nameof(OnSubCompactionBegin));
        _hasOnSubCompactionCompleted = this.CheckIfMethodOverridden<EventListener>(nameof(OnSubCompactionCompleted));
        _hasOnExternalFileIngested = this.CheckIfMethodOverridden<EventListener>(nameof(OnExternalFileIngested));
        _hasOnBackgroundError = this.CheckIfMethodOverridden<EventListener>(nameof(OnBackgroundError));
        _hasOnStallConditionsChanged = this.CheckIfMethodOverridden<EventListener>(nameof(OnStallConditionsChanged));
        _hasOnMemTableSealed = this.CheckIfMethodOverridden<EventListener>(nameof(OnMemTableSealed));

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
        byte* str = NativeMethods.rocksdb_flushjobinfo_cf_name(info, out length);
        var columnFamilyName = NativeMethods.PtrToStringUTF8(str, length);

        str = NativeMethods.rocksdb_flushjobinfo_file_path(info, out length);
        var filePath = NativeMethods.PtrToStringUTF8(str, length);

        return new FlushJobInfo(
            ColumnFamilyName: columnFamilyName,
            FilePath: filePath,
            TriggeredWritesSlowdown: NativeMethods.rocksdb_flushjobinfo_triggered_writes_slowdown(info) != 0,
            TriggeredWritesStop: NativeMethods.rocksdb_flushjobinfo_triggered_writes_stop(info) != 0,
            LargestSeqno: NativeMethods.rocksdb_flushjobinfo_largest_seqno(info),
            SmallestSeqno: NativeMethods.rocksdb_flushjobinfo_smallest_seqno(info),
            FlushReason: (FlushReason)NativeMethods.rocksdb_flushjobinfo_flush_reason(info))
        {
            // Both are borrowed views into `info` and are only valid for the
            // duration of this callback, so copy them now.
            TableProperties = TableProperties.Copy(NativeMethods.rocksdb_flushjobinfo_table_properties(info)),
            BlobFileAdditions = ReadBlobFileAdditions(
                NativeMethods.rocksdb_flushjobinfo_blob_file_addition_infos_count(info),
                pos => NativeMethods.rocksdb_flushjobinfo_blob_file_addition_info_at(info, pos)),
            JobId = NativeMethods.rocksdb_flushjobinfo_job_id(info),
            ThreadId = NativeMethods.rocksdb_flushjobinfo_thread_id(info),
            ColumnFamilyId = NativeMethods.rocksdb_flushjobinfo_cf_id(info),
            FileNumber = NativeMethods.rocksdb_flushjobinfo_file_number(info),
            OldestBlobFileNumber = NativeMethods.rocksdb_flushjobinfo_oldest_blob_file_number(info),
            BlobCompressionType = (Compression)NativeMethods.rocksdb_flushjobinfo_blob_compression_type(info),
        };
    }

    private static IReadOnlyList<BlobFileAdditionInfo> ReadBlobFileAdditions(nuint count, Func<nuint, nint> at)
    {
        if (count == 0)
        {
            return [];
        }

        var result = new List<BlobFileAdditionInfo>(checked((int)count));
        for (nuint i = 0; i < count; i++)
        {
            if (BlobFileAdditionInfo.Copy(at(i)) is { } addition)
            {
                result.Add(addition);
            }
        }

        return result;
    }

    private static IReadOnlyList<BlobFileGarbageInfo> ReadBlobFileGarbage(nuint count, Func<nuint, nint> at)
    {
        if (count == 0)
        {
            return [];
        }

        var result = new List<BlobFileGarbageInfo>(checked((int)count));
        for (nuint i = 0; i < count; i++)
        {
            if (BlobFileGarbageInfo.Copy(at(i)) is { } garbage)
            {
                result.Add(garbage);
            }
        }

        return result;
    }

    // ── CompactionJobInfo ──────────────────────────────────────────────────

    /// <summary>
    /// Reads a <c>rocksdb_compactionjobinfo_t</c> into a managed record. Shared
    /// with <see cref="RocksDb.CompactFiles(CompactFilesOptions, IReadOnlyList{string}, int, out CompactionJobInfo, int)"/>,
    /// which creates the info object itself rather than receiving it from a
    /// listener callback.
    /// </summary>
    internal static CompactionJobInfo ReadCompactionJobInfo(nint info)
        => CreateCompactionJobInfo(info);

    private static unsafe CompactionJobInfo CreateCompactionJobInfo(nint info)
    {
        nuint length;
        byte* str = NativeMethods.rocksdb_compactionjobinfo_cf_name(info, out length);
        var columnFamilyName = NativeMethods.PtrToStringUTF8(str, length);

        nuint inputCount = NativeMethods.rocksdb_compactionjobinfo_input_files_count(info);
        var inputFiles = new string[inputCount];
        for (nuint i = 0; i < inputCount; i++)
        {
            nuint len;
            byte* p = NativeMethods.rocksdb_compactionjobinfo_input_file_at(info, i, out len);
            inputFiles[i] = NativeMethods.PtrToStringUTF8(p, len) ?? string.Empty;
        }

        nuint outputCount = NativeMethods.rocksdb_compactionjobinfo_output_files_count(info);
        var outputFiles = new string[outputCount];
        for (nuint i = 0; i < outputCount; i++)
        {
            nuint len;
            byte* p = NativeMethods.rocksdb_compactionjobinfo_output_file_at(info, i, out len);
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
            Elapsed: TimeSpan.FromMicroseconds(NativeMethods.rocksdb_compactionjobinfo_elapsed_micros(info)),
            NumOfCorruptKeys: NativeMethods.rocksdb_compactionjobinfo_num_corrupt_keys(info),
            BaseInputLevel: NativeMethods.rocksdb_compactionjobinfo_base_input_level(info),
            CompactionReason: (CompactionReason)NativeMethods.rocksdb_compactionjobinfo_compaction_reason(info),
            Status: status)
        {
            // All three are borrowed views into `info`, valid only for the
            // duration of this callback, so copy them now.
            Stats = CompactionJobStats.Copy(NativeMethods.rocksdb_compactionjobinfo_stats(info)),
            BlobFileAdditions = ReadBlobFileAdditions(
                NativeMethods.rocksdb_compactionjobinfo_blob_file_addition_infos_count(info),
                pos => NativeMethods.rocksdb_compactionjobinfo_blob_file_addition_info_at(info, pos)),
            BlobFileGarbage = ReadBlobFileGarbage(
                NativeMethods.rocksdb_compactionjobinfo_blob_file_garbage_infos_count(info),
                pos => NativeMethods.rocksdb_compactionjobinfo_blob_file_garbage_info_at(info, pos)),
            JobId = NativeMethods.rocksdb_compactionjobinfo_job_id(info),
            ThreadId = NativeMethods.rocksdb_compactionjobinfo_thread_id(info),
            ColumnFamilyId = NativeMethods.rocksdb_compactionjobinfo_cf_id(info),
            Aborted = NativeMethods.rocksdb_compactionjobinfo_aborted(info) != 0,
            Compression = (Compression)NativeMethods.rocksdb_compactionjobinfo_compression(info),
            BlobCompressionType = (Compression)NativeMethods.rocksdb_compactionjobinfo_blob_compression_type(info),
            NumL0Files = NativeMethods.rocksdb_compactionjobinfo_num_l0_files(info),
            InputFileInfos = ReadCompactionFileInfos(
                NativeMethods.rocksdb_compactionjobinfo_input_file_infos_count(info),
                pos => NativeMethods.rocksdb_compactionjobinfo_input_file_info_at(info, pos)),
            OutputFileInfos = ReadCompactionFileInfos(
                NativeMethods.rocksdb_compactionjobinfo_output_file_infos_count(info),
                pos => NativeMethods.rocksdb_compactionjobinfo_output_file_info_at(info, pos)),
            TablePropertiesByFile = ReadTablePropertiesByFile(info),
        };
    }

    private static IReadOnlyList<CompactionFileInfo> ReadCompactionFileInfos(nuint count, Func<nuint, nint> at)
    {
        if (count == 0)
        {
            return [];
        }

        var result = new List<CompactionFileInfo>(checked((int)count));
        for (nuint i = 0; i < count; i++)
        {
            if (CompactionFileInfo.Copy(at(i)) is { } fileInfo)
            {
                result.Add(fileInfo);
            }
        }

        return result;
    }

    private static unsafe IReadOnlyDictionary<string, TableProperties> ReadTablePropertiesByFile(nint info)
    {
        nuint count = NativeMethods.rocksdb_compactionjobinfo_table_properties_count(info);
        if (count == 0)
        {
            return new Dictionary<string, TableProperties>();
        }

        var result = new Dictionary<string, TableProperties>(checked((int)count));

        // Both accessors index into a native map, so read each position once
        // rather than looking up by file name.
        for (nuint i = 0; i < count; i++)
        {
            byte* keyPtr = NativeMethods.rocksdb_compactionjobinfo_table_properties_key_at(info, i, out nuint keyLen);
            string? fileName = keyPtr is null ? null : NativeMethods.PtrToStringUTF8(keyPtr, keyLen);
            if (fileName is null)
            {
                continue;
            }

            if (TableProperties.Copy(NativeMethods.rocksdb_compactionjobinfo_table_properties_value_at(info, i)) is { } props)
            {
                result[fileName] = props;
            }
        }

        return result;
    }

    // ── SubCompactionJobInfo ───────────────────────────────────────────────

    private static unsafe SubCompactionJobInfo CreateSubCompactionJobInfo(nint info)
    {
        nuint length;
        byte* str = NativeMethods.rocksdb_subcompactionjobinfo_cf_name(info, out length);
        var columnFamilyName = NativeMethods.PtrToStringUTF8(str, length);

        nint errStr = nint.Zero;
        NativeMethods.rocksdb_subcompactionjobinfo_status(info, ref errStr);
        var status = errStr != nint.Zero ? Marshal.PtrToStringAnsi(errStr) : "OK";

        return new SubCompactionJobInfo(columnFamilyName, status)
        {
            JobId = NativeMethods.rocksdb_subcompactionjobinfo_job_id(info),
            SubCompactionJobId = NativeMethods.rocksdb_subcompactionjobinfo_subcompaction_job_id(info),
            ColumnFamilyId = NativeMethods.rocksdb_subcompactionjobinfo_cf_id(info),
            Compression = (Compression)NativeMethods.rocksdb_subcompactionjobinfo_compression(info),
            BlobCompressionType = (Compression)NativeMethods.rocksdb_subcompactionjobinfo_blob_compression_type(info),
            Stats = CompactionJobStats.Copy(NativeMethods.rocksdb_subcompactionjobinfo_stats(info)),
        };
    }

    // ── ExternalFileIngestionInfo ──────────────────────────────────────────

    private static unsafe ExternalFileIngestionInfo CreateExternalFileIngestionInfo(nint info)
    {
        nuint length;
        byte* str = NativeMethods.rocksdb_externalfileingestioninfo_cf_name(info, out length);
        var columnFamilyName = NativeMethods.PtrToStringUTF8(str, length);

        str = NativeMethods.rocksdb_externalfileingestioninfo_internal_file_path(info, out length);
        var internalPath = NativeMethods.PtrToStringUTF8(str, length);

        str = NativeMethods.rocksdb_externalfileingestioninfo_external_file_path(info, out length);
        var externalPath = str is null ? null : NativeMethods.PtrToStringUTF8(str, length);

        return new ExternalFileIngestionInfo(
            ColumnFamilyName: columnFamilyName,
            InternalFilePath: internalPath)
        {
            ExternalFilePath = externalPath,
            GlobalSeqno = NativeMethods.rocksdb_externalfileingestioninfo_global_seqno(info),
            TableProperties = TableProperties.Copy(NativeMethods.rocksdb_externalfileingestioninfo_table_properties(info)),
        };
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
        byte* str = NativeMethods.rocksdb_writestallinfo_cf_name(info, out length);
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
        byte* str = NativeMethods.rocksdb_memtableinfo_cf_name(info, out length);
        var columnFamilyName = NativeMethods.PtrToStringUTF8(str, length);

        return new MemTableInfo(
            ColumnFamilyName: columnFamilyName,
            FirstSeqno: NativeMethods.rocksdb_memtableinfo_first_seqno(info),
            EarliestSeqno: NativeMethods.rocksdb_memtableinfo_earliest_seqno(info),
            NumEntries: NativeMethods.rocksdb_memtableinfo_num_entries(info),
            NumDeletes: NativeMethods.rocksdb_memtableinfo_num_deletes(info))
        {
            NewestUdt = ReadNewestUdt(info),
        };
    }

    /// <summary>
    /// Copies the newest user-defined timestamp out. The pointer belongs to the
    /// info object, so this cannot be deferred past the callback.
    /// </summary>
    private static unsafe byte[] ReadNewestUdt(nint info)
    {
        byte* udt = NativeMethods.rocksdb_memtableinfo_newest_udt(info, out nuint length);

        return udt is null || length == 0
            ? []
            : new ReadOnlySpan<byte>(udt, checked((int)length)).ToArray();
    }

    // ── Disposal ───────────────────────────────────────────────────────────

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_eventlistener_destroy(Handle);
    }
}