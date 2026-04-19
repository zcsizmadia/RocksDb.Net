using RocksDbNet;

// ─── Column Families: logical partitions within a single database ─────────────
// Column families let you group related data with independent options
// (compression, compaction, etc.) while sharing the WAL for atomicity.

const string dbPath = "column_family_sample_db";

// --- Create a DB and add column families dynamically ---
var options = new DbOptions
{
    CreateIfMissing = true,
    CreateMissingColumnFamilies = true
};

using (var db = RocksDb.Open(options, dbPath))
{
    // Create column families
    using var metaCf = db.CreateColumnFamily(new DbOptions(), "metadata");
    using var dataCf = db.CreateColumnFamily(new DbOptions(), "data");

    // Write to different column families
    db.Put("app:version", "1.0.0", metaCf);
    db.Put("app:started", DateTime.UtcNow.ToString("O"), metaCf);

    db.Put("record:1", "Hello from data CF", dataCf);
    db.Put("record:2", "Another record", dataCf);

    // Default CF still works
    db.Put("global:status", "running");

    Console.WriteLine("=== After creating column families ===");
    Console.WriteLine($"  meta app:version = {db.GetString("app:version", metaCf)}");
    Console.WriteLine($"  data record:1    = {db.GetString("record:1", dataCf)}");
    Console.WriteLine($"  default status   = {db.GetString("global:status")}");
}

// --- List column families in existing DB ---
var cfNames = RocksDb.ListColumnFamilies(new DbOptions(), dbPath);
Console.WriteLine($"\n=== Column families on disk ({cfNames.Count}) ===");
foreach (var name in cfNames)
    Console.WriteLine($"  - {name}");

// --- Re-open with all column families ---
var descriptors = cfNames
    .Select(name => new ColumnFamilyDescriptor(name))
    .ToList();

using (var db = RocksDb.Open(options, dbPath, descriptors))
{
    var metaCf = db.GetColumnFamily("metadata");
    var dataCf = db.GetColumnFamily("data");

    Console.WriteLine("\n=== Re-opened with column families ===");
    Console.WriteLine($"  meta app:version = {db.GetString("app:version", metaCf)}");
    Console.WriteLine($"  data record:1    = {db.GetString("record:1", dataCf)}");
    Console.WriteLine($"  default status   = {db.GetString("global:status")}");

    // Iterate within a specific CF
    Console.WriteLine("\n=== Iterate 'data' column family ===");
    using var iter = db.NewIterator(dataCf);
    for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
        Console.WriteLine($"  {iter.KeyAsString()} = {iter.ValueAsString()}");

    // Column family info
    Console.WriteLine($"\n  metadata CF id:   {metaCf.Id}");
    Console.WriteLine($"  metadata CF name: {metaCf.Name}");

    // Drop a column family
    db.DropColumnFamily(dataCf);
    Console.WriteLine("\n  Dropped 'data' column family.");
}

// Verify it's gone
var remaining = RocksDb.ListColumnFamilies(new DbOptions(), dbPath);
Console.WriteLine($"\n=== Remaining column families ({remaining.Count}) ===");
foreach (var name in remaining)
    Console.WriteLine($"  - {name}");

Console.WriteLine("\nColumnFamily sample completed.");
