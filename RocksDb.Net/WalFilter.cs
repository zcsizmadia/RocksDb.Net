using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// What RocksDb should do with a write-ahead log record during recovery.
/// </summary>
/// <remarks>Values are defined by <c>c.h</c>.</remarks>
public enum WalProcessingOption
{
    /// <summary>Apply the record and carry on.</summary>
    ContinueProcessing = 0,

    /// <summary>Skip this record and carry on with the next one.</summary>
    IgnoreCurrentRecord = 1,

    /// <summary>Stop replaying. Everything after this record is discarded.</summary>
    StopReplay = 2,

    /// <summary>
    /// Treat the record as corrupt.
    /// </summary>
    /// <remarks>
    /// Whether this fails the open depends on
    /// <see cref="DbOptions.WalRecoveryMode"/>. Under the default,
    /// <see cref="WalRecoveryMode.TolerateCorruptedTailRecords"/>, RocksDb
    /// treats it as the end of the log and opens successfully without the
    /// record. Under <see cref="WalRecoveryMode.AbsoluteConsistency"/> the open
    /// fails.
    /// </remarks>
    CorruptedRecord = 3,
}

/// <summary>
/// Inspects, rewrites or skips write-ahead log records as RocksDb replays them
/// while opening a database.
/// Maps to <c>rocksdb_walfilter_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only used during recovery, so the filter runs on the thread calling
/// <see cref="RocksDb.Open(DbOptions, string)"/> and never concurrently.
/// </para>
/// <para>
/// <b>Lifetime:</b> RocksDb stores a raw pointer to the filter in the options
/// and never frees it, so unlike <see cref="EventListener"/> the wrapper keeps
/// ownership. <see cref="DbOptions.SetWalFilter"/> registers the filter with the
/// options so it is disposed alongside them.
/// </para>
/// </remarks>
public abstract class WalFilter : RocksDbHandle
{
    // ── Unmanaged delegate types ─────────────────────────────────────────────

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestructorCb(nint state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void ColumnFamilyLogNumberMapCb(
        nint state,
        uint* columnFamilyIds,
        ulong* logNumbers,
        nuint columnFamilyLogNumberCount,
        byte** columnFamilyNames,
        nuint* columnFamilyNameLengths,
        uint* columnFamilyNameIds,
        nuint columnFamilyNameCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int LogRecordFoundCb(
        nint state,
        ulong logNumber,
        byte* logFileName,
        nuint logFileNameLen,
        nint batch,
        nint newBatch,
        byte* batchChanged);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NameCb(nint state);

    // Held so the native side's function pointers stay valid.
    private readonly DestructorCb _destructorCb;
    private readonly ColumnFamilyLogNumberMapCb _columnFamilyLogNumberMapCb;
    private readonly LogRecordFoundCb _logRecordFoundCb;
    private readonly NameCb _nameCb;

    protected unsafe WalFilter(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        PinGarbageCollector(name);

        _destructorCb = CB_Destructor;
        _columnFamilyLogNumberMapCb = CB_ColumnFamilyLogNumberMap;
        _logRecordFoundCb = CB_LogRecordFound;
        _nameCb = GetNameFromPinnedIntPtrSafe;

        Handle = NativeMethods.rocksdb_walfilter_create(
            GetPinnedIntPtr(),
            Marshal.GetFunctionPointerForDelegate(_destructorCb),
            Marshal.GetFunctionPointerForDelegate(_columnFamilyLogNumberMapCb),
            Marshal.GetFunctionPointerForDelegate(_logRecordFoundCb),
            Marshal.GetFunctionPointerForDelegate(_nameCb));
    }

    // ── Static callbacks ─────────────────────────────────────────────────────

    private static void CB_Destructor(nint state)
    {
        try
        {
            GetSelfFromPinnedIntPtr<WalFilter>(state).UnpinGarbageCollector();
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report("WalFilter destructor", ex, state);
        }
    }

    private static unsafe void CB_ColumnFamilyLogNumberMap(
        nint state,
        uint* columnFamilyIds,
        ulong* logNumbers,
        nuint columnFamilyLogNumberCount,
        byte** columnFamilyNames,
        nuint* columnFamilyNameLengths,
        uint* columnFamilyNameIds,
        nuint columnFamilyNameCount)
    {
        try
        {
            WalFilter self = GetSelfFromPinnedIntPtr<WalFilter>(state);

            // RocksDb hands these over as parallel arrays; rebuild the two maps
            // it split apart.
            int logNumberCount = checked((int)columnFamilyLogNumberCount);
            var logNumbersByColumnFamilyId = new Dictionary<uint, ulong>(logNumberCount);
            for (int i = 0; i < logNumberCount; i++)
            {
                logNumbersByColumnFamilyId[columnFamilyIds[i]] = logNumbers[i];
            }

            int nameCount = checked((int)columnFamilyNameCount);
            var columnFamilyIdsByName = new Dictionary<string, uint>(nameCount);
            for (int i = 0; i < nameCount; i++)
            {
                string? name = NativeMethods.PtrToStringUTF8(columnFamilyNames[i], columnFamilyNameLengths[i]);
                if (name is not null)
                {
                    columnFamilyIdsByName[name] = columnFamilyNameIds[i];
                }
            }

            self.OnColumnFamilyLogNumberMap(logNumbersByColumnFamilyId, columnFamilyIdsByName);
        }
        catch (Exception ex)
        {
            // RocksDb ignores the outcome of this notification.
            RocksDbCallbacks.Report(nameof(OnColumnFamilyLogNumberMap), ex, state);
        }
    }

    private static unsafe int CB_LogRecordFound(
        nint state,
        ulong logNumber,
        byte* logFileName,
        nuint logFileNameLen,
        nint batch,
        nint newBatch,
        byte* batchChanged)
    {
        // Both batches belong to RocksDb and die when this returns: `batch`
        // is a cast of its own WriteBatch, and `newBatch` wraps a stack local.
        // So these views must not be disposed, and must not outlive the call.
        var currentBatch = new WriteBatch(batch, owned: false);
        var replacementBatch = new WriteBatch(newBatch, owned: false);

        try
        {
            WalFilter self = GetSelfFromPinnedIntPtr<WalFilter>(state);
            string fileName = NativeMethods.PtrToStringUTF8(logFileName, logFileNameLen) ?? string.Empty;

            bool changed = false;
            WalProcessingOption decision = self.LogRecordFound(
                logNumber, fileName, currentBatch, replacementBatch, ref changed);

            *batchChanged = changed ? (byte)1 : (byte)0;
            return (int)decision;
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report(nameof(LogRecordFound), ex, state);

            // Continuing leaves the record to be applied as written, which is
            // what would have happened with no filter at all. Reporting the
            // record as corrupt instead could fail the open outright.
            *batchChanged = 0;
            return (int)WalProcessingOption.ContinueProcessing;
        }
        finally
        {
            // Detach so nothing can use the dead pointers afterwards.
            currentBatch.Dispose();
            replacementBatch.Dispose();
        }
    }

    // ── Overridable behaviour ────────────────────────────────────────────────

    /// <summary>
    /// Called once before replay begins, with the log number RocksDb will
    /// recover each column family from, and the id of each column family by
    /// name.
    /// </summary>
    /// <param name="logNumbersByColumnFamilyId">
    /// The log number each column family is being recovered from, keyed by
    /// column family id.
    /// </param>
    /// <param name="columnFamilyIdsByName">Column family ids, keyed by name.</param>
    /// <remarks>
    /// Override this when <see cref="LogRecordFound"/> needs to know which
    /// column families exist. The default does nothing.
    /// </remarks>
    protected virtual void OnColumnFamilyLogNumberMap(
        IReadOnlyDictionary<uint, ulong> logNumbersByColumnFamilyId,
        IReadOnlyDictionary<string, uint> columnFamilyIdsByName)
    {
    }

    /// <summary>
    /// Called for each record RocksDb replays, and decides what happens to it.
    /// </summary>
    /// <param name="logNumber">Number of the log file the record came from.</param>
    /// <param name="logFileName">Name of that log file.</param>
    /// <param name="batch">
    /// The record as written. Valid only for the duration of this call, and must
    /// not be disposed or retained.
    /// </param>
    /// <param name="replacementBatch">
    /// Write a replacement here and set <paramref name="batchChanged"/> to
    /// <c>true</c> to have RocksDb apply this instead of
    /// <paramref name="batch"/>. Same lifetime rules.
    /// </param>
    /// <param name="batchChanged">
    /// Set to <c>true</c> when <paramref name="replacementBatch"/> should be
    /// applied in place of the original.
    /// </param>
    /// <remarks>
    /// Runs during <see cref="RocksDb.Open(DbOptions, string)"/> on the calling
    /// thread. An exception is reported through
    /// <see cref="RocksDbCallbacks.UnhandledException"/> and treated as
    /// <see cref="WalProcessingOption.ContinueProcessing"/>, which applies the
    /// record unchanged.
    /// </remarks>
    protected abstract WalProcessingOption LogRecordFound(
        ulong logNumber,
        string logFileName,
        WriteBatch batch,
        WriteBatch replacementBatch,
        ref bool batchChanged);

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_walfilter_destroy(Handle);
    }
}
