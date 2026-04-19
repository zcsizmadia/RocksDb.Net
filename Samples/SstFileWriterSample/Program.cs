using System.Text;
using RocksDbNet;

// ─── SstFileWriter & IngestExternalFile: bulk loading ─────────────────────────
// SstFileWriter creates pre-sorted SST files outside RocksDb. These files can
// then be ingested directly into the database, bypassing the memtable and WAL
// for maximum bulk-load performance.

const string dbPath = "sst_file_writer_db";
const string sstFile = "bulk_data.sst";

// Clean up previous runs
if (Directory.Exists(dbPath)) Directory.Delete(dbPath, true);
if (File.Exists(sstFile)) File.Delete(sstFile);

// --- Create an SST file with sorted data ---
Console.WriteLine("=== Creating SST file ===");
var opts = new DbOptions();
using (var writer = SstFileWriter.Create(opts))
{
    writer.Open(sstFile);

    // Keys MUST be added in strictly ascending order
    for (int i = 0; i < 1000; i++)
    {
        string key = $"bulk:{i:D6}";
        string val = $"value-{i}";
        writer.Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(val));
    }

    writer.Finish();
    Console.WriteLine($"  Written {sstFile}, size: {writer.FileSize} bytes");
}

// --- Ingest the SST file into a database ---
Console.WriteLine("\n=== Ingesting SST file ===");
var dbOpts = new DbOptions { CreateIfMissing = true };
using var db = RocksDb.Open(dbOpts, dbPath);

// Add some existing data
db.Put("existing:key", "was here before ingest");

using var ingestOpts = new IngestExternalFileOptions
{
    MoveFiles = true // move instead of copy for efficiency
};
db.IngestExternalFile([sstFile], ingestOpts);
Console.WriteLine("  Ingestion complete.");

// --- Verify the data ---
Console.WriteLine("\n=== Verifying data ===");
Console.WriteLine($"  existing:key  = {db.GetString("existing:key")}");
Console.WriteLine($"  bulk:000000   = {db.GetString("bulk:000000")}");
Console.WriteLine($"  bulk:000500   = {db.GetString("bulk:000500")}");
Console.WriteLine($"  bulk:000999   = {db.GetString("bulk:000999")}");

// Count all bulk keys with an iterator
int count = 0;
using (var iter = db.NewIterator())
{
    iter.Seek("bulk:");
    while (iter.IsValid() && iter.KeyAsString().StartsWith("bulk:"))
    {
        count++;
        iter.Next();
    }
}
Console.WriteLine($"  Total bulk keys: {count}");

Console.WriteLine("\nSstFileWriter sample completed.");
