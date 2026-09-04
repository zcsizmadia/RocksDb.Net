# Compaction filters

A compaction filter is a hook RocksDb calls for every entry it rewrites, letting you drop or change values as they pass through. It is the idiomatic way to expire data, strip fields or migrate a value format without scanning the database yourself.

Everything below is compiled and run as part of this repository's test suite.

## When it runs, and when it does not

RocksDb writes new data to a memtable, flushes that to an SST file, and later merges SST files together in the background. Your filter is called during those rewrites, on each entry, once per rewrite.

That timing is the whole shape of the feature, and it has two consequences people are usually surprised by.

**Filtering is not deletion on a schedule.** An entry your filter would remove stays readable until a compaction happens to touch the file holding it. Data with a five-minute expiry can persist for hours if nothing provokes a rewrite of that file. If you need a bound, force it with `CompactRange`, or set `DbOptions.PeriodicCompactionSeconds` so files get rewritten whether or not their size demands it.

**Reads never call the filter.** A `Get` returns whatever is stored. The filter only shapes what survives the next rewrite, so it cannot be used to hide data from a reader.

## A filter that expires old entries

Values here begin with an eight-byte little-endian Unix timestamp, and anything older than the retention window is dropped.

```csharp
using System.Buffers.Binary;
using RocksDbNet;

public sealed class ExpiryFilter : CompactionFilter
{
    private readonly TimeSpan _retention;

    public ExpiryFilter(TimeSpan retention)
        : base("ExpiryFilter")
    {
        _retention = retention;
    }

    protected override FilterDecision Filter(
        int level,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> existingValue,
        out byte[]? newValue)
    {
        newValue = null;

        // Anything that does not carry a timestamp is left alone rather than
        // discarded. A filter that cannot interpret a value should keep it.
        if (existingValue.Length < sizeof(long))
        {
            return FilterDecision.Keep;
        }

        long written = BinaryPrimitives.ReadInt64LittleEndian(existingValue);
        DateTimeOffset writtenAt = DateTimeOffset.FromUnixTimeSeconds(written);

        return DateTimeOffset.UtcNow - writtenAt > _retention
            ? FilterDecision.Remove
            : FilterDecision.Keep;
    }
}
```

Attach it before opening, and keep it alive for as long as the database:

```csharp
var filter = new ExpiryFilter(TimeSpan.FromDays(30));

var options = new DbOptions { CreateIfMissing = true };
options.CompactionFilter = filter;

using var db = RocksDb.Open(options, path);
```

RocksDb holds the filter as a raw pointer it never frees, so the wrapper owns it. Disposing it while the database still points at it is deferred rather than obeyed, which makes the obvious `using` shape safe; see [Ownership and lifetime](ownership.md) for why.

## The three decisions

| Decision | Effect |
| --- | --- |
| `Keep` | The entry survives unchanged. |
| `Remove` | For a key-value, a tombstone is written, hiding older versions of that key. For a merge operand, the operand is simply dropped. |
| `ChangeValue` | The entry survives with the value you wrote to `newValue`. |

`Remove` writing a tombstone rather than erasing is worth dwelling on: the entry is gone as far as reads are concerned, but it costs a little space until a later compaction drops the tombstone too.

`ChangeValue` accepts an empty array, which blanks the value while keeping the key. Returning `ChangeValue` with `newValue` left `null` is a contradiction and leaves the entry untouched.

## Rewriting rather than dropping

The same hook migrates a value format in place, so the cost is paid by compaction rather than by a migration script:

```csharp
public sealed class UppercaseFilter : CompactionFilter
{
    public UppercaseFilter()
        : base("UppercaseFilter")
    {
    }

    protected override FilterDecision Filter(
        int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> existingValue, out byte[]? newValue)
    {
        newValue = new byte[existingValue.Length];

        for (int i = 0; i < existingValue.Length; i++)
        {
            newValue[i] = (byte)char.ToUpperInvariant((char)existingValue[i]);
        }

        return FilterDecision.ChangeValue;
    }
}
```

## Threading, and the factory

Your filter runs on RocksDb's background threads, and concurrently when several compactions are in flight. A single instance must therefore be thread-safe.

If per-job state would be easier than locking, use a factory instead and RocksDb will ask for one filter per compaction job:

```csharp
public sealed class ExpiryFilterFactory : CompactionFilterFactory
{
    private readonly TimeSpan _retention;

    public ExpiryFilterFactory(TimeSpan retention)
        : base("ExpiryFilterFactory")
    {
        _retention = retention;
    }

    protected override CompactionFilter CreateFilter(CompactionFilterContext context)
        => new ExpiryFilter(_retention);
}
```

`CompactionFilterContext` reports whether the job is a full compaction and whether it was triggered manually, which is useful when a full rewrite should be more aggressive than a routine one.

Set `DbOptions.CompactionFilterFactory` instead of `CompactionFilter`. Set one or the other, not both.

## What happens when your filter throws

The entry is kept unchanged, and the exception is reported through `RocksDbCallbacks.UnhandledException`. Keeping the entry is the only outcome that cannot lose or alter data, but it means **a filter that throws for every entry silently becomes a no-op**. Subscribe to that event, or the failure is invisible.

See [Callbacks and exceptions](callbacks.md) for the full table of what each callback does when it throws, and how to tell which instance was responsible.

## Two things not to reach for

**There is no `IgnoreSnapshots` setting.** RocksDb always ignores snapshots for compaction filters now, and a filter reporting false makes RocksDb fail table file creation, which stops compaction. Since `rocksdb_compact_range` has no error channel, that failure was silent: the compaction simply did not happen. The wrapper offered the property for a while and threw on false; it is gone, because the only value it accepted was the one RocksDb already uses.

**Table properties collectors are unreachable.** The C API exposes no way to create a collector factory, so `TableProperties.ReadableProperties` is always empty and user-defined collectors cannot be installed. Only the built-in compact-on-deletion collector is usable.
