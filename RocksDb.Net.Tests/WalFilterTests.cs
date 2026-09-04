using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Covers <see cref="WalFilter"/>. See issue #26.
/// </summary>
/// <remarks>
/// A WAL filter only runs while RocksDb replays the log, so every test here
/// writes with the WAL enabled, closes the database without flushing, then
/// reopens it with a filter installed. That reopen is the only thing that
/// exercises the callback.
/// </remarks>
public class WalFilterTests
{
    private sealed class RecordingFilter(WalProcessingOption decision) : WalFilter("recording-filter")
    {
        private readonly List<string> _logFileNames = [];
        private readonly List<ulong> _logNumbers = [];
        private readonly List<int> _batchCounts = [];

        public int RecordCount { get; private set; }

        public IReadOnlyDictionary<uint, ulong> LogNumbersByColumnFamilyId { get; private set; }
            = new Dictionary<uint, ulong>();

        public IReadOnlyDictionary<string, uint> ColumnFamilyIdsByName { get; private set; }
            = new Dictionary<string, uint>();

        public IReadOnlyList<string> LogFileNames => _logFileNames;

        /// <summary>The log number RocksDb passed for each record.</summary>
        public IReadOnlyList<ulong> LogNumbers => _logNumbers;

        /// <summary>How many operations each record's batch held.</summary>
        public IReadOnlyList<int> BatchCounts => _batchCounts;

        /// <summary>
        /// Checks what the callback saw, from the test thread.
        /// </summary>
        /// <remarks>
        /// These assertions used to live inside LogRecordFound. The callback
        /// catches every exception and returns its documented fallback, and an
        /// xunit assertion failure is only an exception, so a broken log number
        /// or an empty batch was swallowed and the test passed regardless.
        /// </remarks>
        public void AssertRecordsWereWellFormed()
        {
            Assert.NotEmpty(_logNumbers);
            Assert.All(_logNumbers, n => Assert.True(n > 0, $"log number was {n}"));
            Assert.All(_batchCounts, c => Assert.True(c > 0, $"batch held {c} operations"));
        }

        protected override void OnColumnFamilyLogNumberMap(
            IReadOnlyDictionary<uint, ulong> logNumbersByColumnFamilyId,
            IReadOnlyDictionary<string, uint> columnFamilyIdsByName)
        {
            LogNumbersByColumnFamilyId = logNumbersByColumnFamilyId;
            ColumnFamilyIdsByName = columnFamilyIdsByName;
        }

        protected override WalProcessingOption LogRecordFound(
            ulong logNumber,
            string logFileName,
            WriteBatch batch,
            WriteBatch replacementBatch,
            ref bool batchChanged)
        {
            RecordCount++;
            _logFileNames.Add(logFileName);

            // Recorded rather than asserted: see AssertRecordsWereWellFormed.
            _logNumbers.Add(logNumber);
            _batchCounts.Add(batch.Count);

            return decision;
        }
    }

    [Fact]
    public void ContinueProcessing_AppliesEveryRecord()
    {
        using var dir = new TempDir();
        TestDb.WriteRecordsLeftInTheWal(dir.Path, ("a", "1"), ("b", "2"));

        var filter = new RecordingFilter(WalProcessingOption.ContinueProcessing);
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetWalFilter(filter);

        using var db = RocksDb.Open(opts, dir.Path);

        Assert.Equal(2, filter.RecordCount);
        filter.AssertRecordsWereWellFormed();
        Assert.Equal("1", db.GetString("a"));
        Assert.Equal("2", db.GetString("b"));
    }

    [Fact]
    public void IgnoreCurrentRecord_DropsEveryRecord()
    {
        using var dir = new TempDir();
        TestDb.WriteRecordsLeftInTheWal(dir.Path, ("a", "1"), ("b", "2"));

        var filter = new RecordingFilter(WalProcessingOption.IgnoreCurrentRecord);
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetWalFilter(filter);

        using var db = RocksDb.Open(opts, dir.Path);

        // The filter saw both records and skipped both, so nothing was recovered.
        Assert.Equal(2, filter.RecordCount);
        filter.AssertRecordsWereWellFormed();
        Assert.Null(db.GetString("a"));
        Assert.Null(db.GetString("b"));
    }

    [Fact]
    public void StopReplay_DiscardsEverythingFromThatRecordOn()
    {
        using var dir = new TempDir();
        TestDb.WriteRecordsLeftInTheWal(dir.Path, ("a", "1"), ("b", "2"), ("c", "3"));

        var filter = new StopAfterFirstFilter();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetWalFilter(filter);

        using var db = RocksDb.Open(opts, dir.Path);

        // Replay stops at the second record, so only the first survives.
        Assert.Equal("1", db.GetString("a"));
        Assert.Null(db.GetString("b"));
        Assert.Null(db.GetString("c"));
    }

    private sealed class StopAfterFirstFilter() : WalFilter("stop-after-first")
    {
        private int _seen;

        protected override WalProcessingOption LogRecordFound(
            ulong logNumber,
            string logFileName,
            WriteBatch batch,
            WriteBatch replacementBatch,
            ref bool batchChanged)
            => _seen++ == 0
                ? WalProcessingOption.ContinueProcessing
                : WalProcessingOption.StopReplay;
    }

    [Fact]
    public void ReplacementBatch_IsAppliedInPlaceOfTheOriginal()
    {
        using var dir = new TempDir();
        TestDb.WriteRecordsLeftInTheWal(dir.Path, ("a", "original"));

        var filter = new RewritingFilter();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetWalFilter(filter);

        using var db = RocksDb.Open(opts, dir.Path);

        // The original write never lands; the replacement does.
        Assert.Null(db.GetString("a"));
        Assert.Equal("rewritten", db.GetString("replaced"));
    }

    private sealed class RewritingFilter() : WalFilter("rewriting-filter")
    {
        protected override WalProcessingOption LogRecordFound(
            ulong logNumber,
            string logFileName,
            WriteBatch batch,
            WriteBatch replacementBatch,
            ref bool batchChanged)
        {
            replacementBatch.Put("replaced"u8, "rewritten"u8);
            batchChanged = true;
            return WalProcessingOption.ContinueProcessing;
        }
    }

    /// <summary>
    /// Reporting a record as corrupt only fails the open when the recovery mode
    /// refuses to tolerate corruption. The default,
    /// <see cref="WalRecoveryMode.TolerateCorruptedTailRecords"/>, treats it as
    /// the end of the log instead, so the decision alone is not enough.
    /// </summary>
    [Fact]
    public void CorruptedRecord_FailsTheOpenUnderAbsoluteConsistency()
    {
        using var dir = new TempDir();
        TestDb.WriteRecordsLeftInTheWal(dir.Path, ("a", "1"));

        var filter = new RecordingFilter(WalProcessingOption.CorruptedRecord);
        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            WalRecoveryMode = WalRecoveryMode.AbsoluteConsistency,
        };
        opts.SetWalFilter(filter);

        Assert.Throws<RocksDbException>(() => RocksDb.Open(opts, dir.Path));
    }

    [Fact]
    public void CorruptedRecord_UnderTheDefaultMode_StopsReplayInstead()
    {
        using var dir = new TempDir();
        TestDb.WriteRecordsLeftInTheWal(dir.Path, ("a", "1"));

        var filter = new RecordingFilter(WalProcessingOption.CorruptedRecord);
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetWalFilter(filter);

        using var db = RocksDb.Open(opts, dir.Path);

        // The open succeeds, and the record is simply not recovered.
        Assert.Equal(1, filter.RecordCount);
        filter.AssertRecordsWereWellFormed();
        Assert.Null(db.GetString("a"));
    }

    [Fact]
    public void ColumnFamilyLogNumberMap_ReportsTheDefaultColumnFamily()
    {
        using var dir = new TempDir();
        TestDb.WriteRecordsLeftInTheWal(dir.Path, ("a", "1"));

        var filter = new RecordingFilter(WalProcessingOption.ContinueProcessing);
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetWalFilter(filter);

        using var db = RocksDb.Open(opts, dir.Path);

        // RocksDb splits one logical mapping across parallel arrays; this checks
        // the two dictionaries were rebuilt from them correctly.
        Assert.Contains("default", filter.ColumnFamilyIdsByName.Keys);
        Assert.Equal(0U, filter.ColumnFamilyIdsByName["default"]);
        Assert.NotEmpty(filter.LogNumbersByColumnFamilyId);
    }

    [Fact]
    public void LogFileName_IsReportedForEachRecord()
    {
        using var dir = new TempDir();
        TestDb.WriteRecordsLeftInTheWal(dir.Path, ("a", "1"), ("b", "2"));

        var filter = new RecordingFilter(WalProcessingOption.ContinueProcessing);
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetWalFilter(filter);

        using var db = RocksDb.Open(opts, dir.Path);

        Assert.All(filter.LogFileNames, n => Assert.EndsWith(".log", n, StringComparison.Ordinal));
    }

    [Fact]
    public void BatchContents_AreReadableInsideTheCallback()
    {
        using var dir = new TempDir();
        TestDb.WriteRecordsLeftInTheWal(dir.Path, ("a", "1"));

        var filter = new BatchInspectingFilter();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetWalFilter(filter);

        using var db = RocksDb.Open(opts, dir.Path);

        // The batch view really does point at the record RocksDb is replaying.
        Assert.Equal(1, filter.ObservedCount);
        Assert.NotEmpty(filter.ObservedData);
    }

    private sealed class BatchInspectingFilter() : WalFilter("batch-inspecting")
    {
        public int ObservedCount { get; private set; }

        public byte[] ObservedData { get; private set; } = [];

        protected override WalProcessingOption LogRecordFound(
            ulong logNumber,
            string logFileName,
            WriteBatch batch,
            WriteBatch replacementBatch,
            ref bool batchChanged)
        {
            ObservedCount = batch.Count;
            ObservedData = batch.GetData();
            return WalProcessingOption.ContinueProcessing;
        }
    }

    [Fact]
    public void ThrowingFilter_AppliesTheRecordAndReports()
    {
        using var dir = new TempDir();
        TestDb.WriteRecordsLeftInTheWal(dir.Path, ("a", "1"));

        using var reported = new CallbackExceptionRecorder();

        var filter = new ThrowingFilter();
        using var opts = new DbOptions { CreateIfMissing = true };
        opts.SetWalFilter(filter);

        using var db = RocksDb.Open(opts, dir.Path);

        // Continuing is the safe fallback: the record is applied as written,
        // exactly as it would have been with no filter at all.
        Assert.Equal("1", db.GetString("a"));

        Assert.True(reported.Contains("LogRecordFound"));
    }

    private sealed class ThrowingFilter() : WalFilter("throwing-filter")
    {
        protected override WalProcessingOption LogRecordFound(
            ulong logNumber,
            string logFileName,
            WriteBatch batch,
            WriteBatch replacementBatch,
            ref bool batchChanged)
            => throw new InvalidOperationException("wal filter boom");
    }

    [Fact]
    public void SetWalFilter_WithNull_Throws()
    {
        using var opts = new DbOptions();

        Assert.Throws<ArgumentNullException>(() => opts.SetWalFilter(null!));
    }

    [Fact]
    public void ClearWalFilter_OnFreshOptions_DoesNotThrow()
    {
        using var opts = new DbOptions();

        opts.ClearWalFilter();
    }

    [Fact]
    public void WalFilter_WithEmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => new RecordingFilterWithName(string.Empty));

    private sealed class RecordingFilterWithName(string name) : WalFilter(name)
    {
        protected override WalProcessingOption LogRecordFound(
            ulong logNumber,
            string logFileName,
            WriteBatch batch,
            WriteBatch replacementBatch,
            ref bool batchChanged)
            => WalProcessingOption.ContinueProcessing;
    }

    /// <summary>
    /// The options own the filter, since RocksDb never frees it. Disposing the
    /// options must therefore dispose the filter too, without a double free.
    /// </summary>
    [Fact]
    public void DisposingOptions_DisposesTheFilter()
    {
        var filter = new RecordingFilter(WalProcessingOption.ContinueProcessing);

        var opts = new DbOptions();
        opts.SetWalFilter(filter);
        opts.Dispose();

        Assert.True(filter.IsDisposed);
    }
}
