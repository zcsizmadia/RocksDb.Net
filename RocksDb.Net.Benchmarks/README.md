# Benchmarks

Measures what this wrapper costs, so the README's performance claims are
something checked rather than asserted. Every other claim in this repository is
verified by something — coverage has a ratchet, the README's snippets are
compiled and run as tests, the generated bindings are diffed against the pinned
header — and until now performance was the exception.

## Running them

Not part of the normal build. `dotnet build` and `dotnet test` at the
repository root do not touch this project.

```bash
# Everything, which takes a while
dotnet run --project RocksDb.Net.Benchmarks -c Release

# One suite
dotnet run --project RocksDb.Net.Benchmarks -c Release -- --filter '*CallbackBenchmarks*'

# What is available
dotnet run --project RocksDb.Net.Benchmarks -c Release -- --list flat

# A rough answer in a fraction of the time, for iterating
dotnet run --project RocksDb.Net.Benchmarks -c Release -- --job short
```

Release is the default configuration for this project, because BenchmarkDotNet
refuses to run a Debug build and forgetting is easy.

## Not a CI gate, on purpose

Hosted runners are far too noisy for a throughput threshold. A performance gate
that fails at random gets disabled within a month and ignored thereafter, which
is worse than having none, because it also costs the credibility of the checks
that do mean something.

So: run on demand, and commit the results next to the machine and the pinned
RocksDb version they came from. A number without those two facts cannot be
compared with anything.

The place this belongs in a routine is the version upgrade. `/upgrade-rocksdb`
now has a step for it: a native bump that halved read throughput would
otherwise ship, and the first person to notice would be a user.

## What each suite is for

| Suite | Question |
| --- | --- |
| `CallbackBenchmarks` | What does a managed comparator cost against RocksDb's own? This is the payoff of converting the callbacks to function pointers, which was argued without a number. |
| `EventListenerBenchmarks` | Is a notification the listener does not want cheap? Decides whether the reflection that gates the ten event slots can simply be dropped. |
| `ReadBenchmarks` | Do the three read tiers actually differ, and does the gap widen with value size? The README's zero-copy claim rests on this. |
| `MultiGetBenchmarks` | Is batching worth restructuring calling code for, against a plain loop? |
| `IteratorBenchmarks` | Does a full scan allocate? The `ref struct` iterator claim lives in the allocation column, not the time column. |

## In memory, deliberately

The databases are built on RocksDb's in-memory environment. These benchmarks
measure what the *wrapper* costs — a copy that could have been avoided, a
native transition paid per call — and a real file system buries that under
page-cache behaviour and device variance unrelated to the code under test.

The trade is worth stating plainly: **no number here says anything about how
fast RocksDb is against a disk.** That is a different question, and not one any
of these suites is asking.

Values are written and flushed during setup, so reads come from SST files
through the block cache rather than from the memtable. That is the path where a
pinned read can avoid a copy natively, so measuring against the memtable would
flatter the copying calls.
