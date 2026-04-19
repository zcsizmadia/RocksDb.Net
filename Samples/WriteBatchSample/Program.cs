using System.Text;
using RocksDbNet;

// ─── WriteBatch: atomic multi-key operations ───────────────────────────────────
// WriteBatch groups multiple Put/Delete/Merge operations into a single atomic
// write. Either all operations succeed or none do, which is critical for
// maintaining consistency across related keys.

const string dbPath = "writebatch_sample_db";

var options = new DbOptions { CreateIfMissing = true };
using var db = RocksDb.Open(options, dbPath);

// --- Basic batch write ---
using (var batch = new WriteBatch())
{
    batch.Put("account:1:name", "Alice");
    batch.Put("account:1:balance", "1000");
    batch.Put("account:2:name", "Bob");
    batch.Put("account:2:balance", "500");
    db.Write(batch);
}

Console.WriteLine("After initial batch:");
Console.WriteLine($"  Alice balance: {db.GetString("account:1:balance")}");
Console.WriteLine($"  Bob balance:   {db.GetString("account:2:balance")}");

// --- Atomic transfer (debit + credit in one batch) ---
using (var batch = new WriteBatch())
{
    batch.Put("account:1:balance", "800");  // debit 200
    batch.Put("account:2:balance", "700");  // credit 200
    batch.Put("transfer:1", "from=1,to=2,amount=200");
    db.Write(batch);
}

Console.WriteLine("\nAfter transfer:");
Console.WriteLine($"  Alice balance: {db.GetString("account:1:balance")}");
Console.WriteLine($"  Bob balance:   {db.GetString("account:2:balance")}");

// --- Batch with save points (rollback support) ---
using (var batch = new WriteBatch())
{
    batch.Put("temp:1", "value1");
    batch.SetSavePoint();

    batch.Put("temp:2", "value2");
    batch.Put("temp:3", "value3");
    Console.WriteLine($"\nBatch count before rollback: {batch.Count}"); // 3

    batch.RollbackToSavePoint(); // undo temp:2 and temp:3
    Console.WriteLine($"Batch count after rollback:  {batch.Count}"); // 1

    db.Write(batch);
}

Console.WriteLine($"  temp:1 exists: {db.GetString("temp:1") is not null}");
Console.WriteLine($"  temp:2 exists: {db.GetString("temp:2") is not null}");

// --- Batch with Delete and DeleteRange ---
using (var batch = new WriteBatch())
{
    batch.Delete("temp:1");
    batch.Delete("transfer:1");
    db.Write(batch);
}

Console.WriteLine("\nAfter cleanup:");
Console.WriteLine($"  temp:1 exists:     {db.GetString("temp:1") is not null}");
Console.WriteLine($"  transfer:1 exists: {db.GetString("transfer:1") is not null}");

// --- Batch with mixed byte-level operations ---
using (var batch = new WriteBatch())
{
    batch.Put(Encoding.UTF8.GetBytes("binary:key"), new byte[] { 0x01, 0x02, 0x03 });
    batch.PutLogData(Encoding.UTF8.GetBytes("audit: added binary key"));
    db.Write(batch);
}

byte[]? binaryVal = db.Get("binary:key");
Console.WriteLine($"\nBinary value: [{string.Join(", ", binaryVal!.Select(b => $"0x{b:X2}"))}]");

Console.WriteLine("\nWriteBatch sample completed.");
