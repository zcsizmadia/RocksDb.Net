using RocksDbNet;

// ─── Iterator: scanning keys in sorted order ──────────────────────────────────
// Iterators provide forward/backward traversal over all key-value pairs.
// Keys are stored in sorted (lexicographic) order.

const string dbPath = "iterator_sample_db";

var options = new DbOptions { CreateIfMissing = true };
using var db = RocksDb.Open(options, dbPath);

// Populate some data
for (int i = 1; i <= 20; i++)
    db.Put($"user:{i:D4}", $"User #{i}");

db.Put("config:max_users", "100");
db.Put("config:timeout", "30");
db.Put("log:2025-01-01", "started");
db.Put("log:2025-01-02", "running");

// --- Forward scan (all keys) ---
Console.WriteLine("=== Forward scan (all keys) ===");
using (var iter = db.NewIterator())
{
    iter.SeekToFirst();
    while (iter.IsValid())
    {
        Console.WriteLine($"  {iter.KeyAsString()} = {iter.ValueAsString()}");
        iter.Next();
    }
}

// --- Prefix scan (only "user:" keys) ---
Console.WriteLine("\n=== Prefix scan (user:0005 .. user:0010) ===");
using (var iter = db.NewIterator())
{
    iter.Seek("user:0005");
    while (iter.IsValid())
    {
        string key = iter.KeyAsString();
        if (string.Compare(key, "user:0010", StringComparison.Ordinal) > 0)
            break;
        Console.WriteLine($"  {key} = {iter.ValueAsString()}");
        iter.Next();
    }
}

// --- Reverse scan (last 5 keys) ---
Console.WriteLine("\n=== Reverse scan (last 5 keys) ===");
using (var iter = db.NewIterator())
{
    iter.SeekToLast();
    for (int i = 0; i < 5 && iter.IsValid(); i++)
    {
        Console.WriteLine($"  {iter.KeyAsString()} = {iter.ValueAsString()}");
        iter.Prev();
    }
}

// --- SeekForPrev (find the entry at or before a target) ---
Console.WriteLine("\n=== SeekForPrev(\"user:0015\") ===");
using (var iter = db.NewIterator())
{
    iter.SeekForPrev("user:0015");
    if (iter.IsValid())
        Console.WriteLine($"  Found: {iter.KeyAsString()} = {iter.ValueAsString()}");
}

// --- Zero-copy iteration with ForEach ---
Console.WriteLine("\n=== ForEach (config: prefix, zero-copy) ===");
using (var iter = db.NewIterator())
{
    iter.Seek("config:");
    iter.ForEach((key, value) =>
    {
        string k = System.Text.Encoding.UTF8.GetString(key);
        if (!k.StartsWith("config:")) return;
        Console.WriteLine($"  {k} = {System.Text.Encoding.UTF8.GetString(value)}");
    });
}

// --- AsEnumerable (LINQ-friendly) ---
Console.WriteLine("\n=== AsEnumerable + LINQ (log: keys) ===");
using (var iter = db.NewIterator())
{
    iter.Seek("log:");
    var logs = iter.AsEnumerable()
        .TakeWhile(kv => System.Text.Encoding.UTF8.GetString(kv.Key).StartsWith("log:"))
        .ToList();
    foreach (var (key, value) in logs)
        Console.WriteLine($"  {System.Text.Encoding.UTF8.GetString(key)} = {System.Text.Encoding.UTF8.GetString(value)}");
}

Console.WriteLine("\nIterator sample completed.");
