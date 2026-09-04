using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// The events an <see cref="EventListener"/> wants to be told about.
/// </summary>
/// <remarks>
/// <para>
/// Override <see cref="EventListener.Subscribed"/> to narrow this. The default
/// is <see cref="All"/>, so a listener that says nothing receives everything
/// and narrowing is an optimisation rather than a requirement — a listener
/// cannot go silent by forgetting to declare an event it overrode.
/// </para>
/// <para>
/// What narrowing saves is the construction of the job-info object an event
/// carries, which is measurable: a listener overriding five of the ten events
/// allocates about twice what one overriding none does, while the boundary
/// crossing itself costs nothing detectable. See the benchmarks.
/// </para>
/// </remarks>
[Flags]
public enum EventKinds
{
    /// <summary>No events.</summary>
    None = 0,

    /// <summary><see cref="EventListener.OnFlushBegin"/>.</summary>
    FlushBegin = 1 << 0,

    /// <summary><see cref="EventListener.OnFlushCompleted"/>.</summary>
    FlushCompleted = 1 << 1,

    /// <summary><see cref="EventListener.OnCompactionBegin"/>.</summary>
    CompactionBegin = 1 << 2,

    /// <summary><see cref="EventListener.OnCompactionCompleted"/>.</summary>
    CompactionCompleted = 1 << 3,

    /// <summary><see cref="EventListener.OnSubCompactionBegin"/>.</summary>
    SubCompactionBegin = 1 << 4,

    /// <summary><see cref="EventListener.OnSubCompactionCompleted"/>.</summary>
    SubCompactionCompleted = 1 << 5,

    /// <summary><see cref="EventListener.OnExternalFileIngested"/>.</summary>
    ExternalFileIngested = 1 << 6,

    /// <summary><see cref="EventListener.OnBackgroundError"/>.</summary>
    BackgroundError = 1 << 7,

    /// <summary><see cref="EventListener.OnStallConditionsChanged"/>.</summary>
    StallConditionsChanged = 1 << 8,

    /// <summary><see cref="EventListener.OnMemTableSealed"/>.</summary>
    MemTableSealed = 1 << 9,

    /// <summary>Every event. The default.</summary>
    All = FlushBegin | FlushCompleted | CompactionBegin | CompactionCompleted
        | SubCompactionBegin | SubCompactionCompleted | ExternalFileIngested
        | BackgroundError | StallConditionsChanged | MemTableSealed,
}

/// <summary>
/// Why RocksDb flushed a memtable, mapped from <c>rocksdb::FlushReason</c> in
/// <c>listener.h</c>.
/// </summary>
/// <remarks>
/// The native values are explicit and must match exactly. A shifted value
/// silently mislabels every flush an application observes.
/// </remarks>
public enum FlushReason
{
    /// <summary>No specific reason recorded.</summary>
    Others = 0x00,

    /// <summary>A call that needed a consistent set of files on disk.</summary>
    GetLiveFiles = 0x01,

    /// <summary>The database is closing.</summary>
    ShutDown = 0x02,

    /// <summary>External SST file ingestion required a flush first.</summary>
    ExternalFileIngestion = 0x03,

    /// <summary>A manual compaction required a flush first.</summary>
    ManualCompaction = 0x04,

    /// <summary>The write buffer manager asked for memory back.</summary>
    WriteBufferManager = 0x05,

    /// <summary>The memtable reached <see cref="DbOptions.WriteBufferSize"/>.</summary>
    WriteBufferFull = 0x06,

    /// <summary>Internal to RocksDb's own tests.</summary>
    Test = 0x07,

    /// <summary>A file deletion required a flush first.</summary>
    DeleteFiles = 0x08,

    /// <summary>An automatic compaction required a flush first.</summary>
    AutoCompaction = 0x09,

    /// <summary>An explicit <see cref="RocksDb.Flush(FlushOptions)"/>.</summary>
    ManualFlush = 0x0a,

    /// <summary>Recovering from a background error.</summary>
    ErrorRecovery = 0x0b,

    /// <summary>A retried flush during error recovery.</summary>
    ErrorRecoveryRetryFlush = 0x0c,

    /// <summary>The write-ahead log reached its size limit.</summary>
    WalFull = 0x0d,

    /// <summary>Catching up after error recovery completed.</summary>
    CatchUpAfterErrorRecovery = 0x0e,

    /// <summary>The memtable accumulated too many range deletions.</summary>
    MemtableMaxRangeDeletions = 0x0f,
}

/// <summary>
/// What RocksDb was doing when a background error occurred, mapped from
/// <c>rocksdb::BackgroundErrorReason</c> in <c>listener.h</c>.
/// </summary>
/// <remarks>
/// The values are positional in the native header and must match it exactly.
/// </remarks>
public enum BackgroundErrorReason
{
    /// <summary>Flushing a memtable.</summary>
    Flush = 0,

    /// <summary>Running a compaction.</summary>
    Compaction = 1,

    /// <summary>Invoking a write callback.</summary>
    WriteCallback = 2,

    /// <summary>Writing to the memtable.</summary>
    MemTable = 3,

    /// <summary>Writing the manifest.</summary>
    ManifestWrite = 4,

    /// <summary>Flushing with the write-ahead log disabled.</summary>
    FlushNoWal = 5,

    /// <summary>Writing the manifest with the write-ahead log disabled.</summary>
    ManifestWriteNoWal = 6,

    /// <summary>Opening a file asynchronously.</summary>
    AsyncFileOpen = 7,
}

/// <summary>
/// The write stall state of a column family, mapped from
/// <c>rocksdb::WriteStallCondition</c> in <c>types.h</c>.
/// </summary>
/// <remarks>
/// Note the native ordering: <c>kNormal</c> is last, not first, because RocksDb
/// adds new stall conditions before it. Assuming the conventional
/// normal-first order inverts the signal, reporting the onset of a stall as
/// normal operation and recovery as a stop.
/// </remarks>
public enum WriteStallCondition
{
    /// <summary>Writes are being slowed down.</summary>
    Delayed = 0,

    /// <summary>Writes are stopped.</summary>
    Stopped = 1,

    /// <summary>No stall; writes proceed at full speed.</summary>
    Normal = 2,
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
    ulong InputRecords,
    ulong OutputRecords,
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
        // ── Native entry points ──────────────────────────────────────────────────
    //
    // [UnmanagedCallersOnly] rather than delegates, so RocksDb receives the
    // address of each method instead of a runtime-generated marshalling thunk.
    // See Comparator for the full reasoning. The eleven fields that used to
    // hold the delegates alive are gone; what keeps this listener reachable is
    // the GCHandle from PinGarbageCollector.
    //
    // All eleven slots are still installed unconditionally. RocksDb invokes
    // each without a null check, so a slot left null for an event the subclass
    // did not override terminated the process. The gate is on the managed side
    // instead; see the constructor.

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    /// <summary>
    /// Which events this listener wants. Every one, unless a subclass narrows
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaced a reflection check that asked, once per instance, whether
    /// each of the ten virtuals had been overridden. The check worked, but
    /// <c>GetMethod</c> by name on a runtime-known type is the shape trimming
    /// and NativeAOT analysers object to, and it was the only reflection left in
    /// the library.
    /// </para>
    /// <para>
    /// What it bought is worth keeping, so this is the same saving stated
    /// explicitly rather than inferred: an event nobody wants costs no job-info
    /// object. That is the measurable part — a listener overriding five events
    /// allocates about twice what one overriding none does, while the boundary
    /// crossing itself costs nothing detectable.
    /// </para>
    /// <para>
    /// Read when an event arrives rather than cached at construction, so an
    /// override may depend on the subclass's own fields. A virtual called from
    /// this base constructor would run before those fields were assigned.
    /// </para>
    /// </remarks>
    protected virtual EventKinds Subscribed => EventKinds.All;

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
        EventKinds kind,
        nint state,
        nint info,
        Action<EventListener, nint> body)
    {
        try
        {
            EventListener self = SelfFromState(state);

            if ((self.Subscribed & kind) != 0)
            {
                body(self, info);
            }
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report(callbackName, ex, state);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
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
            RocksDbCallbacks.Report("EventListener destructor", ex, state);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFlushBeginCallback(nint state, nint db, nint info)
        => Notify(nameof(OnFlushBegin), EventKinds.FlushBegin, state, info,
            static (self, i) => self.OnFlushBegin(CreateFlushJobInfo(i)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFlushCompletedCallback(nint state, nint db, nint info)
        => Notify(nameof(OnFlushCompleted), EventKinds.FlushCompleted, state, info,
            static (self, i) => self.OnFlushCompleted(CreateFlushJobInfo(i)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCompactionBeginCallback(nint state, nint db, nint info)
        => Notify(nameof(OnCompactionBegin), EventKinds.CompactionBegin, state, info,
            static (self, i) => self.OnCompactionBegin(CreateCompactionJobInfo(i)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCompactionCompletedCallback(nint state, nint db, nint info)
        => Notify(nameof(OnCompactionCompleted), EventKinds.CompactionCompleted, state, info,
            static (self, i) => self.OnCompactionCompleted(CreateCompactionJobInfo(i)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSubCompactionBeginCallback(nint state, nint info)
        => Notify(nameof(OnSubCompactionBegin), EventKinds.SubCompactionBegin, state, info,
            static (self, i) => self.OnSubCompactionBegin(CreateSubCompactionJobInfo(i)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSubCompactionCompletedCallback(nint state, nint info)
        => Notify(nameof(OnSubCompactionCompleted), EventKinds.SubCompactionCompleted, state, info,
            static (self, i) => self.OnSubCompactionCompleted(CreateSubCompactionJobInfo(i)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnExternalFileIngestedCallback(nint state, nint db, nint info)
        => Notify(nameof(OnExternalFileIngested), EventKinds.ExternalFileIngested, state, info,
            static (self, i) => self.OnExternalFileIngested(CreateExternalFileIngestionInfo(i)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnStallConditionsChangedCallback(nint state, nint info)
        => Notify(nameof(OnStallConditionsChanged), EventKinds.StallConditionsChanged, state, info,
            static (self, i) => self.OnStallConditionsChanged(CreateWriteStallInfo(i)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnMemTableSealedCallback(nint state, nint info)
        => Notify(nameof(OnMemTableSealed), EventKinds.MemTableSealed, state, info,
            static (self, i) => self.OnMemTableSealed(CreateMemTableInfo(i)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnBackgroundErrorCallback(nint state, uint reason, nint info)
    {
        try
        {
            EventListener self = SelfFromState(state);

            if ((self.Subscribed & EventKinds.BackgroundError) != 0)
            {
                self.OnBackgroundError(CreateBackgroundErrorInfo(reason, info));
            }
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report(nameof(OnBackgroundError), ex, state);
        }
    }

    private static EventListener SelfFromState(nint state) => GetSelfFromPinnedIntPtr<EventListener>(state);

    // ── Construction ─────────────────────────────────────────────────────────

    protected unsafe EventListener()
    {
        // Pin this instance so that the C++ callbacks can access it via the state pointer
        PinGarbageCollector();


        // Every one of the ten slots is installed, whatever the subclass wants.
        // Handing RocksDb a null function pointer is not a way to opt out:
        // rocksdb_eventlistener_t in db/c.cc overrides all ten EventListener
        // virtuals and invokes the stored pointer with no null check, so a null
        // crashes the process the first time that event fires. See issue #35.
        // Which events are actually delivered is decided on this side, by
        // Subscribed.

        Handle = NativeMethods.rocksdb_eventlistener_create(
            GetPinnedIntPtr(),
            (nint)(delegate* unmanaged[Cdecl]<nint, void>)&DestructorCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnFlushBeginCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnFlushCompletedCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnCompactionBeginCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnCompactionCompletedCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnSubCompactionBeginCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnSubCompactionCompletedCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnExternalFileIngestedCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint, void>)&OnBackgroundErrorCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnStallConditionsChangedCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnMemTableSealedCallback);
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

        // The status arrives through SaveError, which strdups the message, so it
        // has to be freed once copied. Leaked one string per failing compaction.
        nint errptr = default;
        NativeMethods.rocksdb_compactionjobinfo_status(info, ref errptr);
        string? status = "OK";
        if (errptr != nint.Zero)
        {
            status = Marshal.PtrToStringAnsi(errptr);
            NativeMethods.rocksdb_free(errptr);
        }

        return new CompactionJobInfo(
            ColumnFamilyName: columnFamilyName,
            InputFiles: inputFiles,
            OutputFiles: outputFiles,
            TotalInputBytes: NativeMethods.rocksdb_compactionjobinfo_total_input_bytes(info),
            TotalOutputBytes: NativeMethods.rocksdb_compactionjobinfo_total_output_bytes(info),
            InputRecords: NativeMethods.rocksdb_compactionjobinfo_input_records(info),
            OutputRecords: NativeMethods.rocksdb_compactionjobinfo_output_records(info),
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

        // Freed for the same reason as in CompactionJobInfo above.
        nint errStr = nint.Zero;
        NativeMethods.rocksdb_subcompactionjobinfo_status(info, ref errStr);
        string? status = "OK";
        if (errStr != nint.Zero)
        {
            status = Marshal.PtrToStringAnsi(errStr);
            NativeMethods.rocksdb_free(errStr);
        }

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

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_eventlistener_destroy(Handle);
    }
}