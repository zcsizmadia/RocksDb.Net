using RocksDbNet;

const string dbPath = "rocksdb";

// Clean up previous runs
if (Directory.Exists(dbPath)) Directory.Delete(dbPath, true);

var options = new DbOptions()
{
    CreateIfMissing = true,
};

using var db = RocksDb.Open(options, dbPath);

db.Put("key1", "value1");
db.Put("key2", "value2");
db.Put("key3", "value3");

Console.WriteLine($"key1 = {db.GetString("key1")}");
Console.WriteLine($"key2 = {db.GetString("key2")}");
Console.WriteLine($"key3 = {db.GetString("key3")}");

db.Delete("key1");
db.Delete("key2");
db.Delete("key3");

Console.WriteLine("Basic sample completed.");
