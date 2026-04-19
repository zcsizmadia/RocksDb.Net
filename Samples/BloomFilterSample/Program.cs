using System.Diagnostics;
using RocksDbNet;

// ─── Bloom Filters, Block Cache & Prefix Extraction ──────────────────────────
// Bloom filters reduce unnecessary disk reads by quickly ruling out keys that
// don't exist. Combined with block cache and prefix extractors, they enable
// highly efficient point lookups and prefix scans.

const string dbPath = "bloom_filter_db";

// --- Configure block-based table with Bloom filter ---
using var cache = Cache.CreateLru(64 * 1024 * 1024); // 64 MB block cache
using var filterPolicy = FilterPolicy.CreateBloom(10); // 10 bits per key

var tableOptions = new BlockBasedTableOptions
{
    BlockSize = 16 * 1024, // 16 KB blocks
    CacheIndexAndFilterBlocks = true,
    PinL0FilterAndIndexBlocksInCache = true,
    WholeKeyFiltering = true
};
tableOptions.SetBlockCache(cache);
tableOptions.SetFilterPolicy(filterPolicy);

var options = new DbOptions
{
    CreateIfMissing = true,
    // Fixed prefix of 8 bytes for prefix scans
    PrefixExtractor = SliceTransform.CreateFixedPrefix(8),
    BlockBasedTableFactory = tableOptions
};
options.OptimizeForPointLookup(64); // 64 MB

using var db = RocksDb.Open(options, dbPath);

// --- Populate data ---
Console.WriteLine("=== Populating data ===");
var sw = Stopwatch.StartNew();
for (int i = 0; i < 100_000; i++)
    db.Put($"key:{i:D8}", $"value-{i}");
sw.Stop();
Console.WriteLine($"  Inserted 100,000 keys in {sw.ElapsedMilliseconds} ms");

// --- Point lookups with KeyMayExist (Bloom-optimized) ---
Console.WriteLine("\n=== KeyMayExist (Bloom filter check) ===");
bool mayExist1 = db.KeyMayExist(System.Text.Encoding.UTF8.GetBytes("key:00050000"));
bool mayExist2 = db.KeyMayExist(System.Text.Encoding.UTF8.GetBytes("nonexistent:key"));
Console.WriteLine($"  key:00050000  may exist: {mayExist1}"); // true
Console.WriteLine($"  nonexistent   may exist: {mayExist2}"); // likely false (Bloom)

// --- MultiGet for batch lookups ---
Console.WriteLine("\n=== MultiGet (batch lookup) ===");
var keys = new List<byte[]>
{
    System.Text.Encoding.UTF8.GetBytes("key:00000001"),
    System.Text.Encoding.UTF8.GetBytes("key:00000010"),
    System.Text.Encoding.UTF8.GetBytes("key:00099999"),
    System.Text.Encoding.UTF8.GetBytes("missing:key"),
};
var results = db.MultiGet(keys);
for (int i = 0; i < keys.Count; i++)
{
    string k = System.Text.Encoding.UTF8.GetString(keys[i]);
    string v = results[i] is not null
        ? System.Text.Encoding.UTF8.GetString(results[i]!)
        : "(null)";
    Console.WriteLine($"  {k} = {v}");
}

// --- Cache statistics ---
Console.WriteLine("\n=== Cache statistics ===");
Console.WriteLine($"  Capacity:     {cache.Capacity / 1024 / 1024} MB");
Console.WriteLine($"  Usage:        {cache.Usage / 1024} KB");
Console.WriteLine($"  Pinned usage: {cache.PinnedUsage / 1024} KB");

// --- Prefix scan using ReadOptions ---
Console.WriteLine("\n=== Prefix scan (first 5 matches for 'key:0001') ===");
using var readOpts = new ReadOptions { PrefixSameAsStart = true };
using var iter = db.NewIterator(readOpts);
iter.Seek("key:0001");
int shown = 0;
while (iter.IsValid() && shown < 5)
{
    Console.WriteLine($"  {iter.KeyAsString()} = {iter.ValueAsString()}");
    iter.Next();
    shown++;
}

// --- Different filter policies ---
Console.WriteLine("\n=== Available filter policies ===");
Console.WriteLine("  - CreateBloom(bitsPerKey)         : Partitioned Bloom filter");
Console.WriteLine("  - CreateBloomFull(bitsPerKey)      : Full (non-partitioned) Bloom");
Console.WriteLine("  - CreateRibbon(bitsPerKey)         : Space-efficient Ribbon filter");
Console.WriteLine("  - CreateRibbonHybrid(bits, level)  : Ribbon with Bloom at lower levels");

Console.WriteLine("\nBloomFilter sample completed.");
