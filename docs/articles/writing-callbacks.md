# Writing callbacks

Six kinds of extension point let you put your own code inside RocksDb: a comparator to change key ordering, a merge operator to combine values without reading them first, a compaction filter to transform data as it is rewritten, a logger to capture RocksDb's own diagnostics, an event listener to observe flushes and compactions, and a WAL filter to inspect or rewrite records as they are recovered.

This page is how to write each one. [Callbacks and exceptions](callbacks.md) is the companion: what happens when one throws, which thread it runs on, and how to tell which instance was responsible. Read that one too, because the answers differ per callback and one of them terminates the process.

Everything below is compiled and run as part of this repository's test suite.

## Comparator

A comparator decides key order, and therefore the order every scan sees. Subclass it and pass a name:

```csharp
using RocksDbNet;

public sealed class ReverseComparator : Comparator
{
    public ReverseComparator()
        : base("example.reverse")
    {
    }

    public override int Compare(ReadOnlySpan<byte> keyA, ReadOnlySpan<byte> keyB)
        => keyB.SequenceCompareTo(keyA);
}
```

```csharp
var comparator = new ReverseComparator();

var options = new DbOptions { CreateIfMissing = true };
options.Comparator = comparator;

using var db = RocksDb.Open(options, path);
```

**The name is not decoration.** RocksDb records it in the database and refuses to reopen with a comparator whose name differs, which is what stops data being read back in an order it was not written in. Change the name only when the ordering itself changes, and never reuse a name for different semantics.

**A comparator must be a total order and must be consistent.** RocksDb calls it constantly, including on internal keys during compaction, and an ordering that is not transitive corrupts the database rather than merely confusing a scan.

**It is the one callback that terminates the process if it throws.** `Compare` has no failure channel: it must return an ordering, and any value invented misrepresents key order for data RocksDb then writes and later reads back. Handle exceptions inside your comparator.

## Merge operator

A merge operator turns read-modify-write into a single append. `Merge` records an operand; RocksDb combines operands with the existing value when the key is next read or compacted.

```csharp
using System.Buffers.Binary;

public sealed class CounterMergeOperator : MergeOperator
{
    public CounterMergeOperator()
        : base("example.counter")
    {
    }

    public override bool FullMerge(
        ReadOnlySpan<byte> key,
        bool hasExistingValue,
        ReadOnlySpan<byte> existingValue,
        IReadOnlyList<byte[]> operands,
        out byte[]? newValue)
    {
        long total = hasExistingValue && existingValue.Length == sizeof(long)
            ? BinaryPrimitives.ReadInt64LittleEndian(existingValue)
            : 0;

        foreach (byte[] operand in operands)
        {
            if (operand.Length == sizeof(long))
            {
                total += BinaryPrimitives.ReadInt64LittleEndian(operand);
            }
        }

        newValue = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(newValue, total);
        return true;
    }

    // Optional. Combining operands with each other, without the existing
    // value, lets compaction collapse a long chain of them early.
    public override bool PartialMerge(
        ReadOnlySpan<byte> key, IReadOnlyList<byte[]> operands, out byte[]? newValue)
    {
        long sum = 0;

        foreach (byte[] operand in operands)
        {
            if (operand.Length != sizeof(long))
            {
                newValue = [];
                return false;   // decline, and let FullMerge handle it
            }

            sum += BinaryPrimitives.ReadInt64LittleEndian(operand);
        }

        newValue = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(newValue, sum);
        return true;
    }
}
```

Using it:

```csharp
static byte[] Delta(long by)
{
    var operand = new byte[sizeof(long)];
    BinaryPrimitives.WriteInt64LittleEndian(operand, by);
    return operand;
}

var options = new DbOptions { CreateIfMissing = true };
options.MergeOperator = new CounterMergeOperator();

using var db = RocksDb.Open(options, path);

db.Merge("visits"u8, Delta(1));
db.Merge("visits"u8, Delta(5));

long visits = BinaryPrimitives.ReadInt64LittleEndian(db.Get("visits"u8));   // 6
```

Three things to know.

**The operands are managed copies and may be kept.** RocksDb builds those arrays as call-scoped locals natively, and the wrapper materialises them before calling you, so storing the list beyond the callback is safe.

**Returning false from `FullMerge` is a real failure.** The read that triggered it reports a corruption error. Use it when the operands genuinely cannot be combined, not as a way to skip work.

**One instance per options object.** RocksDb wraps a merge operator in a fresh shared pointer of its own, so handing the same instance to two `DbOptions` would give it two owners that each delete it. The second assignment throws rather than letting that corrupt the heap later.

## Compaction filter

Covered in its own guide, [Compaction filters](compaction-filters.md), because the timing of when it runs shapes how you use it.

## Logger

RocksDb writes its own diagnostics to a LOG file next to the data. A logger redirects them into your application's logging instead:

```csharp
public sealed class ConsoleLogger : Logger
{
    public ConsoleLogger()
        : base(InfoLogLevel.Info)
    {
    }

    public override void Log(InfoLogLevel logLevel, string message)
        => Console.WriteLine($"[rocksdb {logLevel}] {message}");
}
```

```csharp
var options = new DbOptions { CreateIfMissing = true };
options.InfoLog = new ConsoleLogger();
```

The level passed to the constructor is a threshold: RocksDb does not call you for anything below it.

This is the callback with the sharpest lifetime rule, and the wrapper handles it for you. The C API offers no destructor callback for a logger, so nothing tells the wrapper when RocksDb has finished with it, while RocksDb's own copy of the pointer outlives the options it was given. Attaching it therefore registers a hold, and the pin is released only when the last holder lets go. A `using` on your logger is safe, because disposing it while the database still points at it is deferred rather than obeyed.

## Event listener

An event listener observes background work. Override only the events you want:

```csharp
public sealed class FlushWatcher : EventListener
{
    private long _flushes;

    public long Flushes => Interlocked.Read(ref _flushes);

    public override void OnFlushCompleted(FlushJobInfo info)
        => Interlocked.Increment(ref _flushes);

    public override void OnBackgroundError(BackgroundErrorInfo info)
        => Console.Error.WriteLine($"rocksdb background error: {info.Reason}");
}
```

```csharp
var options = new DbOptions { CreateIfMissing = true };
options.AddEventListener(new FlushWatcher());
```

Note the `Interlocked` calls. Most listener callbacks run on RocksDb's background threads, concurrently when several flushes or compactions are in flight, so a listener that accumulates anything must be thread-safe. `OnMemTableSealed` is the exception and runs on the writer's thread, which is not a reason to skip the synchronisation: the same listener still hears the rest from elsewhere.

**Adding accumulates.** Call `AddEventListener` twice and both listeners receive every event; the second does not displace the first, and there is no way to remove one afterwards. That is RocksDb's behaviour, not a wrapper choice, and it is why this is a method rather than the property it used to be: a property reads like an assignment that replaces.

The info objects are copied out before the callback returns, so they are safe to keep and to pass between threads. That is not true of every callback argument: `ReadOptions.SetTableFilter` hands you a live view that dies when the callback returns, and `WalFilter` receives batches that belong to RocksDb.

## The rules that apply to all of them

**Never let an exception reach native code.** Every callback the library installs catches at the boundary and reports through `RocksDbCallbacks.UnhandledException`. Subscribe to it, because a throwing callback is otherwise invisible, and each one degrades to a different fallback. See [Callbacks and exceptions](callbacks.md).

**Attach before opening.** Nearly every `DbOptions` value, callbacks included, is read once when the database opens. Setting one on a live `DbOptions` does nothing.

**Lifetime is not uniform across them.** A comparator and compaction filter are raw pointers RocksDb never frees, so the wrapper owns them. A logger is a shared pointer RocksDb copies. A merge operator, event listener and compaction filter factory are transfers RocksDb takes over. The wrapper reconciles those so the ordinary `using` shape is safe in every case, but [Ownership and lifetime](ownership.md) is worth reading before you share one instance between two databases, which is supported for some and rejected for others.
