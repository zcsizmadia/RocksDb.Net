# Your first database

RocksDb is an embedded key-value store. There is no server and no connection string: a database is a directory on disk that one process opens at a time.

Everything below is compiled and run as part of this repository's test suite, so the snippets are known to work rather than merely to look right.

## Opening and closing

```csharp
using RocksDbNet;

var options = new DbOptions { CreateIfMissing = true };
using var db = RocksDb.Open(options, "mydb");

db.Put("hello", "world");
string? value = db.GetString("hello");   // "world"
```

Two things about that fourth line are worth knowing immediately.

**`Open` consumes the options.** The database takes ownership of the `DbOptions` you hand it and disposes them when it closes. Do not reuse that instance for a second database, or for `Destroy`, `Repair` or `ListColumnFamilies`, because after the database closes you would be reading freed memory. Note the snippet does *not* write `using var options`; it would be harmless, since disposing them early is deferred, but it implies an ownership you do not have.

**`CreateIfMissing` is false by default.** Opening a directory that does not contain a database fails rather than creating one, which is deliberate: it stops a typo in a path from silently producing an empty database.

Disposing the database flushes nothing by default. It closes cleanly, but data still in the memtable is recovered from the write-ahead log on the next open rather than being written to an SST first. That is normal and safe.

## Keys and values are bytes

RocksDb stores opaque bytes. The `string` overloads exist for convenience and encode as UTF-8:

```csharp
db.Put("key", "value");                       // UTF-8 encoded for you
db.Put("key"u8, "value"u8);                   // no encoding, no allocation
db.Put(Encoding.UTF8.GetBytes("key"), bytes); // your own encoding
```

Prefer the span overloads on a hot path. `"key"u8` is a UTF-8 literal, so it allocates nothing at all.

Reading gives you back either form:

```csharp
byte[]? raw = db.Get("key"u8);
string? text = db.GetString("key");
```

Both return `null` for a key that is not present, which is distinct from a key whose value is an empty array. RocksDb stores empty values happily, so `Get` returning a zero-length array means the key exists with no value.

## Deleting

```csharp
db.Delete("key");
Assert.Null(db.GetString("key"));
```

A delete is a write, not an erasure. RocksDb appends a tombstone that hides earlier versions of the key, and the space is reclaimed later by compaction. This is why deleting everything does not immediately shrink a database, and why a range full of tombstones makes reads across it slower until compaction runs.

## Iterating

```csharp
using Iterator iter = db.NewIterator();

for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
{
    Console.WriteLine($"{iter.KeyAsString()} = {iter.ValueAsString()}");
}
```

Keys come back in sorted byte order, which is the property most RocksDb designs are built on: choose a key layout whose lexicographic order matches the order you want to scan in, and range queries become sequential reads.

`foreach` works too, and hands out spans rather than copies:

```csharp
foreach (Iterator.Entry entry in iter)
{
    Process(entry.Key, entry.Value);   // both ReadOnlySpan<byte>
}
```

Those spans point into the iterator's own buffers and are invalidated as soon as it moves, so copy anything you need to keep.

An iterator reads a consistent point-in-time view from when it was created. Writes made afterwards are not visible to it, and it must be disposed before the database.

## Writing several keys atomically

Individual writes are atomic on their own. To make several succeed or fail together, batch them:

```csharp
using var batch = new WriteBatch();
batch.Put("a", "1");
batch.Put("b", "2");
batch.Delete("c");

db.Write(batch);
```

The batch is applied as one unit. A reader sees none of it or all of it, never half. Applying a batch does not consume it, so it can be applied again or to a second database.

## Durability

By default a write returns once it is in the memtable and the write-ahead log, and the log write is not fsynced. A process crash loses nothing, because the log is on disk; a machine losing power can lose recent writes.

```csharp
using var sync = new WriteOptions { Sync = true };
db.Put("important", "value", sync);
```

That costs an fsync per write. The usual middle ground is to leave writes unsynced and flush the log periodically instead:

```csharp
db.FlushWal(sync: true);
```

`db.Flush()` is a different operation. It writes the memtable out as an SST file, which is about organising data on disk rather than about durability, and it covers the default column family only.

## Column families

A column family is an independent keyspace inside one database, with its own options, memtable and SST files. Keys in different families never collide, and a write batch can span them atomically.

```csharp
var options = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

using var db = RocksDb.Open(options, "mydb", [new("default"), new("users")]);
ColumnFamilyHandle users = db.GetColumnFamily("users");

db.Put("alice", "…", users);
string? alice = db.GetString("alice", users);
```

Every family that exists in a database must be named when opening it, including ones you do not intend to use. The `"default"` family always exists.

Be careful with `null` here. `db.Put("k", "v", null)` binds to the `WriteOptions` overload, not the column family one, so it writes to the default family rather than throwing. Cast explicitly if you mean a family.

## Storing large values

Every compaction rewrites the values it moves along with the keys. For large values that is most of the work and none of the benefit, since the value has not changed. Integrated BlobDB writes values above a threshold into separate blob files, so compaction moves only the keys and a reference.

```csharp
var options = new DbOptions
{
    CreateIfMissing = true,
    EnableBlobFiles = true,
    MinBlobSize = 1024,
    EnableBlobGarbageCollection = true,
};

using var db = RocksDb.Open(options, path);

db.Put("large", new string('v', 4096));
```

The trade is an extra file access per blob read, so this pays off for large values and costs for small ones. `MinBlobSize` is where you decide which is which.

Two settings are worth knowing about beyond turning it on.

**Blobs are not in the block cache.** They are not in the SST files, so the block cache never sees them and every read of a blob reaches the file system. `BlobCache` gives them their own cache, which also stops them evicting the index and filter blocks that the block cache exists to hold. `PrepopulateBlobCache` then puts each blob into that cache as it is written, so a value read soon after being written does not go to disk at all. It does nothing without a `BlobCache`, because there is nowhere to put anything.

**Overwritten blobs are not reclaimed until collected.** Overwriting a key leaves the old value in its blob file, and the file is only rewritten when garbage collection decides enough of it is dead. `EnableBlobGarbageCollection` turns that on; `BlobGarbageCollectionAgeCutoff` chooses how much of the oldest data is eligible and `BlobGarbageCollectionForceThreshold` how much garbage is enough. Avoid the degenerate pair of a 1.0 cutoff with a 0.0 threshold: it means "rewrite any file with any garbage", and each rewrite produces another file that immediately qualifies.

`CompactRangeOptions` carries per-compaction overrides for the same two settings, if one compaction should collect more aggressively than the database default.

## Where to go next

- **[Ownership and lifetime](ownership.md)** for which object frees which native handle. Worth reading before you attach a comparator or logger, because the rules are not uniform.
- **[Compaction filters](compaction-filters.md)** to transform or expire data as it is rewritten.
- **[Ownership and lifetime](ownership.md)** again if you attach a `BlobCache`, since it is shared with RocksDb rather than handed over.
- **[Writing callbacks](writing-callbacks.md)** for comparators, merge operators, loggers and event listeners.
- **[Callbacks and exceptions](callbacks.md)** for what happens when one of those throws, which is not the same for all of them.
- **[Samples](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples)** in the repository, one per feature area.
