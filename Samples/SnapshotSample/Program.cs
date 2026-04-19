using RocksDbNet;

// ─── Snapshots: point-in-time consistent reads ───────────────────────────────
// A snapshot freezes the database state. Reads through a snapshot always see
// the data as it was at snapshot creation, regardless of subsequent writes.

const string dbPath = "snapshot_sample_db";

var options = new DbOptions { CreateIfMissing = true };
using var db = RocksDb.Open(options, dbPath);

// Write initial data
db.Put("counter", "100");
db.Put("status", "active");

Console.WriteLine("=== Initial state ===");
Console.WriteLine($"  counter = {db.GetString("counter")}");
Console.WriteLine($"  status  = {db.GetString("status")}");

// Take a snapshot
using var snapshot = db.NewSnapshot();
Console.WriteLine($"\nSnapshot taken at sequence #{snapshot.SequenceNumber}");

// Modify data AFTER the snapshot
db.Put("counter", "200");
db.Put("status", "paused");
db.Put("new_key", "added after snapshot");

Console.WriteLine("\n=== Current state (after modifications) ===");
Console.WriteLine($"  counter = {db.GetString("counter")}");
Console.WriteLine($"  status  = {db.GetString("status")}");
Console.WriteLine($"  new_key = {db.GetString("new_key")}");

// Read through the snapshot — sees the OLD data
using var readOpts = new ReadOptions();
readOpts.SetSnapshot(snapshot);

Console.WriteLine("\n=== Snapshot state (frozen in time) ===");
Console.WriteLine($"  counter = {db.GetString("counter", readOpts)}");
Console.WriteLine($"  status  = {db.GetString("status", readOpts)}");
Console.WriteLine($"  new_key = {db.GetString("new_key", readOpts) ?? "(not found)"}");

// Iterate through snapshot
Console.WriteLine("\n=== Iterate snapshot ===");
using var iter = db.NewIterator(readOpts);
for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
    Console.WriteLine($"  {iter.KeyAsString()} = {iter.ValueAsString()}");

Console.WriteLine("\n=== Iterate current ===");
using var iter2 = db.NewIterator();
for (iter2.SeekToFirst(); iter2.IsValid(); iter2.Next())
    Console.WriteLine($"  {iter2.KeyAsString()} = {iter2.ValueAsString()}");

Console.WriteLine("\nSnapshot sample completed.");
