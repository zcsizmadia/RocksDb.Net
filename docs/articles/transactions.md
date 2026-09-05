# Transactions

A `WriteBatch` makes several writes land together. It does not help with the other half of the problem: deciding what to write based on what you read, and knowing that nothing changed underneath you in between. That is what a transaction is for.

RocksDb ships two implementations of it, and they are not versions of one another. Choosing between them is the first decision, so it comes first here.

Everything below is compiled and run as part of this repository's test suite, so the snippets are known to work rather than merely to look right.

## Which one

**`TransactionDb` is pessimistic.** Writing a key takes a lock on it and holds that lock until the transaction ends. A second writer that wants the same key waits, and then fails when the lock timeout expires. Conflicts are prevented rather than detected.

**`OptimisticTransactionDb` is optimistic.** Nothing is locked while the transaction runs. Both writers proceed, and the second one to commit is told its work is stale and must be thrown away.

The trade is about how often conflicts actually happen:

| | `TransactionDb` | `OptimisticTransactionDb` |
|---|---|---|
| Cost when there is no conflict | A lock per written key | Nothing |
| Cost when there is a conflict | The loser waits, then fails | The loser finishes its work, then discards it |
| Can deadlock | Yes, detected | No — there are no locks to wait on |
| Failure surfaces at | The write | The commit |

Low contention favours the optimistic one, because it pays nothing for locks that would never have been contended. High contention favours the pessimistic one, because failing early is cheaper than doing work twice. Neither is the safer choice; they fail in different places.

## A pessimistic transaction

```csharp
var options = new DbOptions { CreateIfMissing = true };
using var txnOptions = new TransactionDbOptions();
using TransactionDb db = TransactionDb.Open(options, txnOptions, "mydb");

using Transaction txn = db.BeginTransaction();

// GetForUpdate locks the key. A plain Get does not, and a decision based on
// one is not protected against anything.
string? balance = txn.GetStringForUpdate("account:1");
txn.Put("account:1", (int.Parse(balance ?? "0") + 100).ToString());

txn.Commit();
```

`GetForUpdate` is the important call. A plain `Get` inside a transaction takes no lock and is not tracked, so a key you read that way can change before you commit and nothing will tell you. If a write depends on what you read, read it for update.

Dispose the transaction whether or not it committed. `Commit` and `Rollback` decide what happens to the writes; neither releases the transaction, and one that is never released keeps its locks.

## An optimistic transaction, and the retry it needs

```csharp
var options = new DbOptions { CreateIfMissing = true };
using OptimisticTransactionDb db = OptimisticTransactionDb.Open(options, "mydb");

for (int attempt = 0; ; attempt++)
{
    using Transaction txn = db.BeginTransaction();

    string? balance = txn.GetStringForUpdate("account:1");
    txn.Put("account:1", (int.Parse(balance ?? "0") + 100).ToString());

    try
    {
        txn.Commit();
        break;
    }
    catch (RocksDbException) when (attempt < 5)
    {
        // Someone else committed first. Nothing was written, so start again
        // from what the database says now.
    }
}
```

The retry loop is not defensive programming; it is how the type is meant to be used. A failed commit here means "someone else got there first", not "the database is broken", and the database is left untouched so that starting again is sound. Code that calls `Commit` once and treats a failure as fatal has chosen the wrong database type.

Note that `GetForUpdate` still matters, for a different reason than before. It does not lock anything here — it marks the key as one to validate at commit. Without it, a read-modify-write commits happily on top of someone else's change.

## Reading a set of keys

Reading twenty keys one at a time is twenty round trips into native code. `MultiGet` is one:

```csharp
using Transaction txn = db.BeginTransaction();

byte[]?[] values = txn.MultiGet([
    "account:1"u8.ToArray(),
    "account:2"u8.ToArray(),
    "account:3"u8.ToArray(),
]);

// A missing key is null in the corresponding position.
```

`MultiGetForUpdate` does the same and marks every key it read, which is the batched form of the rule above — and the reason to prefer it when the keys are ones you intend to write. Its locks are always exclusive, because RocksDb's batched call takes no shared-lock flag.

For a value you only want to read, `GetPinned` avoids copying it into managed memory at all:

```csharp
using Transaction txn = db.BeginTransaction();
using PinnableSlice? slice = txn.GetPinned("account:1"u8.ToArray());

if (slice is not null)
{
    ReadOnlySpan<byte> value = slice.Value;   // no copy
}
```

Dispose it promptly. It pins the block the value came from, and that block cannot leave the block cache until you do.

## Surviving a crash

An ordinary transaction lives in memory. If the process dies before it commits, the work is gone and there is nothing to find afterwards. Two-phase commit changes that:

```csharp
using Transaction txn = db.BeginTransaction();
txn.Put("order:4711", "pending");

txn.Name = "order-4711";
txn.Prepare();          // durable, but not committed
```

After `Prepare` returns, the transaction is on disk. Committing it applies the writes and rolling it back discards them, exactly as before — but if the process dies in between, reopening the database finds it again:

```csharp
using TransactionDb db = TransactionDb.Open(options, txnOptions, "mydb");

foreach (Transaction recovered in db.GetPreparedTransactions())
{
    using (recovered)
    {
        // The name is how you decide. It is yours to choose, so make it mean
        // something to whoever has to resolve it.
        if (recovered.Name == "order-4711")
        {
            recovered.Commit();
        }
        else
        {
            recovered.Rollback();
        }
    }
}
```

A name is required before preparing, and it has to be unique among live transactions. Choose something the recovering process can act on — an order id, a message id, a coordinator's transaction id — rather than a counter that restarts with the program.

**Resolve every transaction you recover.** A prepared transaction is still holding the locks it held when it was prepared, and it kept them across the restart. Disposing it without committing or rolling it back leaves those keys locked against every other writer for the life of the database.

## What is not here

`Transaction` deliberately does not expose its internal snapshot or its write batch: RocksDb hands back pointers to objects the transaction still owns, and the obvious disposal would free them twice.

The non-transactional database behind a transaction database is withheld too, for a different reason — its handle must be released with a different call than a normal database, so exposing it would hand you an object whose disposal closes the real database.

See [Ownership and lifetime](ownership.md) for the full set of rules.
