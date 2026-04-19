using RocksDbNet;

// ─── ReadOnly & Secondary instances ──────────────────────────────────────────
// ReadOnly mode opens a database that cannot be written to.
// Secondary mode opens a follower that can catch up to the primary's writes.

const string dbPath = "readonly_secondary_db";
const string secondaryPath = "readonly_secondary_follower";

// Clean up previous runs
foreach (var dir in new[] { dbPath, secondaryPath })
    if (Directory.Exists(dir)) Directory.Delete(dir, true);

var options = new DbOptions { CreateIfMissing = true };

// --- Create and populate primary DB ---
Console.WriteLine("=== Primary database ===");
using var primary = RocksDb.Open(options, dbPath);
primary.Put("key1", "initial-value-1");
primary.Put("key2", "initial-value-2");
primary.Flush();

Console.WriteLine($"  key1 = {primary.GetString("key1")}");
Console.WriteLine($"  key2 = {primary.GetString("key2")}");

// --- ReadOnly mode ---
Console.WriteLine("\n=== ReadOnly instance ===");
using (var readOnly = RocksDb.OpenReadOnly(options, dbPath))
{
    Console.WriteLine($"  key1 = {readOnly.GetString("key1")}");
    Console.WriteLine($"  key2 = {readOnly.GetString("key2")}");

    // Writing to read-only would throw:
    try
    {
        readOnly.Put("key3", "should-fail");
    }
    catch (RocksDbException ex)
    {
        Console.WriteLine($"  Write rejected: {ex.Message}");
    }
}

// --- Secondary instance (follower) ---
Console.WriteLine("\n=== Secondary instance (follower) ===");
using var secondary = RocksDb.OpenAsSecondary(options, dbPath, secondaryPath);

Console.WriteLine("  Before catch-up:");
Console.WriteLine($"    key1 = {secondary.GetString("key1")}");
Console.WriteLine($"    key3 = {secondary.GetString("key3") ?? "(not found)"}");

// Write new data to primary
primary.Put("key3", "added-after-secondary-opened");
primary.Put("key1", "updated-value-1");
primary.Flush();

Console.WriteLine("\n  Primary updated key1 and added key3.");

// Secondary still sees old data
Console.WriteLine("\n  Secondary before catch-up:");
Console.WriteLine($"    key1 = {secondary.GetString("key1")}");
Console.WriteLine($"    key3 = {secondary.GetString("key3") ?? "(not found)"}");

// Catch up to primary
secondary.TryCatchUpWithPrimary();

Console.WriteLine("\n  Secondary after catch-up:");
Console.WriteLine($"    key1 = {secondary.GetString("key1")}");
Console.WriteLine($"    key3 = {secondary.GetString("key3")}");

// --- TTL mode ---
Console.WriteLine("\n=== TTL mode ===");
const string ttlPath = "readonly_secondary_ttl_db";
using (var ttlDb = RocksDb.OpenWithTtl(
    new DbOptions { CreateIfMissing = true }, ttlPath, 3600))
{
    ttlDb.Put("session:abc", "user-data");
    Console.WriteLine($"  session:abc = {ttlDb.GetString("session:abc")}");
    Console.WriteLine("  (key will expire after 3600 seconds via TTL compaction)");
}

Console.WriteLine("\nReadOnly & Secondary sample completed.");
