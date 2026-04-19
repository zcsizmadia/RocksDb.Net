using RocksDbNet;

// ─── Tuning, Properties & Statistics ─────────────────────────────────────────
// Demonstrates DbOptions tuning presets, reading internal properties,
// and using the statistics system.

const string dbPath = "tuning_stats_db";

// --- Fully tuned options ---
using var rateLimiter = new RateLimiter(
    rateBytesPerSec: 50 * 1024 * 1024,  // 50 MB/s I/O rate limit
    refillPeriodMicros: 100_000,
    fairness: 10);

var options = new DbOptions
{
    CreateIfMissing = true,

    // Parallelism
    MaxBackgroundJobs = 4,
    MaxSubcompactions = 2,

    // Write buffers
    WriteBufferSize = 64 * 1024 * 1024,       // 64 MB
    MaxWriteBufferNumber = 3,
    MinWriteBufferNumberToMerge = 2,
    DbWriteBufferSize = 128 * 1024 * 1024,    // 128 MB total

    // Compaction
    NumLevels = 7,
    Level0FileNumCompactionTrigger = 4,
    Level0SlowdownWritesTrigger = 20,
    Level0StopWritesTrigger = 36,
    TargetFileSizeBase = 64 * 1024 * 1024,     // 64 MB
    MaxBytesForLevelBase = 256 * 1024 * 1024,  // 256 MB
    LevelCompactionDynamicLevelBytes = true,

    // I/O
    BytesPerSync = 1024 * 1024,                // 1 MB
    UseDirectReads = false,
    AllowConcurrentMemtableWrite = true,
    AtomicFlush = false,

    // Files
    MaxOpenFiles = 5000,
    KeepLogFileNum = 10,

    // Rate limiter
    RateLimiter = rateLimiter,

    // Compression
    Compression = Compression.Lz4,
    BottommostCompression = Compression.Zstd,

    // WAL
    WalRecoveryMode = WalRecoveryMode.PointInTime,
    ManualWalFlush = false,
};

// Enable statistics collection
options.EnableStatistics();

using var db = RocksDb.Open(options, dbPath);

// --- Populate some data ---
Console.WriteLine("=== Writing data ===");
for (int i = 0; i < 10_000; i++)
    db.Put($"key:{i:D8}", $"value-{i}-{new string('x', 100)}");
db.Flush();

// --- Read internal properties ---
Console.WriteLine("\n=== Database properties ===");

string[] properties = [
    "rocksdb.estimate-num-keys",
    "rocksdb.num-files-at-level0",
    "rocksdb.num-files-at-level1",
    "rocksdb.cur-size-all-mem-tables",
    "rocksdb.estimate-table-readers-mem",
    "rocksdb.num-running-compactions",
    "rocksdb.num-running-flushes",
    "rocksdb.actual-delayed-write-rate",
    "rocksdb.is-write-stopped",
];

foreach (var prop in properties)
{
    var intVal = db.GetPropertyInt(prop);
    if (intVal.HasValue)
        Console.WriteLine($"  {prop} = {intVal.Value:N0}");
    else
        Console.WriteLine($"  {prop} = (unavailable)");
}

// String properties
string? levelStats = db.GetProperty("rocksdb.levelstats");
if (levelStats is not null)
{
    Console.WriteLine($"\n=== Level stats ===");
    Console.WriteLine(levelStats);
}

// --- Database identity & sequence number ---
Console.WriteLine($"=== Database info ===");
Console.WriteLine($"  DB identity:    {db.GetDbIdentity()}");
Console.WriteLine($"  Latest seq#:    {db.LatestSequenceNumber}");

// --- Statistics summary ---
string? stats = options.GetStatisticsString();
if (stats is not null)
{
    Console.WriteLine($"\n=== Statistics (first 1000 chars) ===");
    Console.WriteLine(stats[..Math.Min(1000, stats.Length)]);
    Console.WriteLine("  ...(truncated)");
}

Console.WriteLine("\nTuning & Stats sample completed.");
