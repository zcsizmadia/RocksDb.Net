using System.Text;
using RocksDbNet;

// ─── CompactionFilter: automatic data transformation during compaction ────────
// CompactionFilter lets you intercept every key-value during compaction to
// remove expired data, transform values, or implement TTL-like logic
// without explicit deletes.

const string dbPath = "compaction_filter_db";

// --- Custom filter that removes "expired" keys and uppercases values ---
var filter = new ExpiryFilter();

var options = new DbOptions
{
    CreateIfMissing = true,
    CompactionFilter = filter,
    // Small buffer to trigger compaction more easily
    WriteBufferSize = 1024
};

using var db = RocksDb.Open(options, dbPath);

// Write data with a convention: "expired:" prefix means it should be removed
db.Put("expired:session:1", "old-session-data");
db.Put("expired:session:2", "stale-token");
db.Put("active:user:1", "alice");
db.Put("active:user:2", "bob");
db.Put("transform:greeting", "hello world");

Console.WriteLine("=== Before compaction ===");
PrintAll(db);

// Force compaction to trigger the filter
db.CompactRange();

Console.WriteLine("\n=== After compaction ===");
Console.WriteLine("  (expired keys removed, values uppercased)");
PrintAll(db);

Console.WriteLine("\nCompactionFilter sample completed.");

static void PrintAll(RocksDb db)
{
    using var iter = db.NewIterator();
    for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
        Console.WriteLine($"  {iter.KeyAsString()} = {iter.ValueAsString()}");
}

// ── Custom CompactionFilter ────────────────────────────────────────────────

class ExpiryFilter : CompactionFilter
{
    public ExpiryFilter() : base("ExpiryFilter") { }

    protected override FilterDecision Filter(
        int level,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> existingValue,
        out byte[]? newValue)
    {
        string keyStr = Encoding.UTF8.GetString(key);

        // Remove keys starting with "expired:"
        if (keyStr.StartsWith("expired:"))
        {
            newValue = null;
            return FilterDecision.Remove;
        }

        // Transform values starting with "transform:" to uppercase
        if (keyStr.StartsWith("transform:"))
        {
            string val = Encoding.UTF8.GetString(existingValue);
            newValue = Encoding.UTF8.GetBytes(val.ToUpperInvariant());
            return FilterDecision.ChangeValue;
        }

        // Keep everything else as-is
        newValue = null;
        return FilterDecision.Keep;
    }
}
