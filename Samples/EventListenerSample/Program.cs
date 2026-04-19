using RocksDbNet;

// ─── EventListener: monitoring database internals ────────────────────────────
// EventListener receives callbacks for flush, compaction, stall, and error
// events. Use it for monitoring, metrics, or adaptive tuning.

const string dbPath = "event_listener_db";

using var listener = new MetricsListener();

var options = new DbOptions
{
    CreateIfMissing = true,
    WriteBufferSize = 4 * 1024, // Tiny buffer to trigger flushes
    Level0FileNumCompactionTrigger = 2,
    EventListener = listener,
};

using var db = RocksDb.Open(options, dbPath);

Console.WriteLine("=== Writing data to trigger flush and compaction events ===\n");

// Write enough data to trigger multiple flushes and compactions
for (int batch = 0; batch < 5; batch++)
{
    for (int i = 0; i < 200; i++)
        db.Put($"batch{batch}:key:{i:D4}", $"value-{i}-{new string('d', 50)}");

    db.Flush();
}

// Trigger compaction
db.CompactRange();

Console.WriteLine($"\n=== Listener metrics ===");
Console.WriteLine($"  Flushes started:     {listener.FlushesStarted}");
Console.WriteLine($"  Flushes completed:   {listener.FlushesCompleted}");
Console.WriteLine($"  Compactions started: {listener.CompactionsStarted}");
Console.WriteLine($"  Compactions done:    {listener.CompactionsCompleted}");
Console.WriteLine($"  Stall changes:       {listener.StallChanges}");
Console.WriteLine($"  Memtables sealed:    {listener.MemTablesSealed}");

Console.WriteLine("\nEventListener sample completed.");

// ── Comprehensive EventListener ────────────────────────────────────────────

class MetricsListener : EventListener
{
    public int FlushesStarted;
    public int FlushesCompleted;
    public int CompactionsStarted;
    public int CompactionsCompleted;
    public int StallChanges;
    public int MemTablesSealed;

    public override void OnFlushBegin(FlushJobInfo info)
    {
        Interlocked.Increment(ref FlushesStarted);
        Console.WriteLine($"  [FLUSH BEGIN]     CF={info.ColumnFamilyName}");
    }

    public override void OnFlushCompleted(FlushJobInfo info)
    {
        Interlocked.Increment(ref FlushesCompleted);
        Console.WriteLine($"  [FLUSH DONE]      CF={info.ColumnFamilyName}, file={info.FilePath}, reason={info.FlushReason}");
    }

    public override void OnCompactionBegin(CompactionJobInfo info)
    {
        Interlocked.Increment(ref CompactionsStarted);
        Console.WriteLine($"  [COMPACT BEGIN]   CF={info.ColumnFamilyName}, inputs={info.InputFiles.Length}, outputs={info.OutputFiles.Length}");
    }

    public override void OnCompactionCompleted(CompactionJobInfo info)
    {
        Interlocked.Increment(ref CompactionsCompleted);
        Console.WriteLine($"  [COMPACT DONE]    CF={info.ColumnFamilyName}, reason={info.CompactionReason}, " +
            $"in={info.TotalInputBytes}B, out={info.TotalOutputBytes}B, elapsed={info.ElapsedMicros}us");
    }

    public override void OnSubCompactionBegin(SubCompactionJobInfo info)
    {
        Console.WriteLine($"  [SUBCOMPACT BEGIN] CF={info.ColumnFamilyName}");
    }

    public override void OnSubCompactionCompleted(SubCompactionJobInfo info)
    {
        Console.WriteLine($"  [SUBCOMPACT DONE]  CF={info.ColumnFamilyName}, status={info.Status}");
    }

    public override void OnStallConditionsChanged(WriteStallInfo info)
    {
        Interlocked.Increment(ref StallChanges);
        Console.WriteLine($"  [STALL CHANGE]    CF={info.ColumnFamilyName}, {info.PreviousCondition} -> {info.Condition}");
    }

    public override void OnBackgroundError(BackgroundErrorInfo info)
    {
        Console.WriteLine($"  [BG ERROR]        reason={info.Reason}, msg={info.Message}");
    }

    public override void OnMemTableSealed(MemTableInfo info)
    {
        Interlocked.Increment(ref MemTablesSealed);
        Console.WriteLine($"  [MEMTABLE SEALED] CF={info.ColumnFamilyName}, entries={info.NumEntries}, deletes={info.NumDeletes}");
    }
}
