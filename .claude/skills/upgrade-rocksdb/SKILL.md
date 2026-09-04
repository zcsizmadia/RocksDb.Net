---
name: upgrade-rocksdb
description: Bump the pinned facebook/rocksdb version - regenerate the P/Invoke bindings and statistics enums, audit the header diff for added, removed and changed exports, re-check the ABI and enum hazards, and update the counts and changelog. Use when the user says "upgrade rocksdb", "bump to rocksdb X.Y.Z", "pin a new rocksdb version", or invokes /upgrade-rocksdb.
---

# Upgrading the pinned RocksDb version

One property drives everything: `RocksDbVersion` in `Directory.Build.props`. It
decides the package version (`$(RocksDbVersion).$(Revision)`), which native
library the package binds to, and which headers the generator downloads.

A `RocksDbVersion` change is also the **only** point at which breaking API
changes are allowed (see `CHANGELOG.md` under Versioning), so it is the moment
to spend on API cleanup — and the last chance until the next bump.

## Step 0 - the runtimes package comes first

`RocksDb.Net.Runtimes` must be published for the new version **before** the
wrapper releases, because the wrapper declares a bounded dependency computed
from `RocksDbVersionNextPatch`:

```bash
curl -s https://api.nuget.org/v3-flatcontainer/rocksdb.net.runtimes/index.json
```

Nothing here breaks if it is missing, but the release cannot ship.

## Step 1 - bump, then regenerate

```bash
# Edit Directory.Build.props: <RocksDbVersion>X.Y.Z</RocksDbVersion>
dotnet run --project NativeMethodsGenerator
```

The generator takes no arguments in normal use. It reads the version from
`Directory.Build.props`, downloads `include/rocksdb/c.h` and
`include/rocksdb/statistics.h` at that tag, and writes
`RocksDb.Net/Native/NativeMethods.g.cs` and
`RocksDb.Net/Native/StatisticsEnums.g.cs`, printing what it is doing and where.
`--version` and `--project` override the defaults if needed.

**Never hand-edit the generated files.** If a declaration is wrong, the defect
is in the generator: `CHeaderParser.cs` (parsing), `PInvokeGenerator.cs` (type
mapping and emission), `CppEnumParser.cs` and `StatisticsEnumGenerator.cs`
(the statistics enums). Note there is **no test project for the generator**, so
generator changes need manual verification against the header.

`PinnedVersionTests` compares the version stamped into the generated files with
`AssemblyMetadata("RocksDbVersion")`, so a bump without a regeneration fails
`dotnet test` rather than building green.

## Step 2 - audit the header diff

This is the substance of the upgrade. Get both headers and compare the exported
function sets:

```bash
old=11.1.2 ; new=11.8.1
for v in $old $new; do
  curl -s "https://raw.githubusercontent.com/facebook/rocksdb/v$v/include/rocksdb/c.h" \
    | grep -oE 'rocksdb_[a-z0-9_]+\(' | tr -d '(' | sort -u > "/tmp/exports-$v.txt"
done
comm -13 "/tmp/exports-$old.txt" "/tmp/exports-$new.txt" | wc -l   # added
comm -23 "/tmp/exports-$old.txt" "/tmp/exports-$new.txt"           # REMOVED - read every one
```

**Removed exports matter most.** If wrapper code calls one, the build breaks —
which is the good case. Check what the wrapper actually calls:

```bash
grep -rhoE '\brocksdb_[a-zA-Z0-9_]+\b' --include=*.cs RocksDb.Net/ \
  | grep -v -f <(echo) | sort -u > /tmp/used.txt
```

Also watch for a function that still *exists* but changed signature — those do
not break the build, they break at runtime. Diff the declarations for anything
the wrapper calls and the diff touched.

Beware: an apparent export named only in a header comment is not an export.
That has produced a false "missing function" report before.

## Step 3 - re-check the ABI hazards

The generator now throws on an unmapped type rather than silently emitting
`nint`, and that fail-loud behaviour is what makes a clean generation
meaningful. Still verify the classes that have bitten this project, because a
new header can introduce a new shape:

- A function returning a **struct by value** (`rocksdb_slice_t` is the only
  transparent struct today) declared as returning a pointer. On Windows x64 a
  >8-byte struct return uses a hidden pointer argument, so the call corrupts
  memory.
- C **`bool`** returns or parameters declared pointer-sized: reads register
  bits no ABI defines, so false can read as true. Should be `byte`.
- **`size_t`** mapped to a fixed 64-bit type, or a `size_t` **array** indexed as
  64-bit. Silently wrong on win-x86 only.
- **Pointer-to-array** parameters declared as untyped `nint`.
- By-value **`const uint32_t`** mapped to `nint`, so a value is passed where a
  pointer is expected.

A quick sanity check that the mapping is complete:

```bash
grep -c 'LibraryImport' RocksDb.Net/Native/NativeMethods.g.cs   # should equal the export count
grep -c 'CallConvCdecl' RocksDb.Net/Native/NativeMethods.g.cs   # every one, no exceptions
grep -c '\bbool\b' RocksDb.Net/Native/NativeMethods.g.cs        # expect 0
```

Hand-written delegates are **not** covered by the generator (see the `_cb` rule
comment in `PInvokeGenerator.cs`), so any callback whose native signature
changed must be updated by hand in the wrapper.

## Step 4 - the statistics enums renumber

`Ticker` and `Histogram` are generated from `statistics.h` and their values are
**positional**: they renumber whenever a counter is added or retired upstream.
Regeneration handles that, but check the diff and check nothing pins a literal
value.

More broadly, roughly twenty enums are mirrored by hand from C++ headers that
the C API declares as plain `int` — `IoActivity`, `RateLimiterPriority`,
`ReadTier`, `Temperature`, `FileType`, `CacheTier`, `PinningTier`,
`BottommostLevelCompaction`, `PerfLevel`, `CompactionPri`, `StatsLevel`,
`PrepopulateBlobCache`, `Compression`, `CompactionStyle`, `ChecksumType`,
`WalRecoveryMode`, `CpuPriority`, `RestoreMode`, `BlobGarbageCollectionPolicy`
and the `BlockBasedTable*` family. Only the four event-listener enums are pinned
against the headers by `EnumTests.AssertExactly<T>`; the rest are asserted only
against the wrapper's own numbers, so **a value that changed upstream will not
fail a test**. On a version bump, re-read those headers. Two rules:

- Never mirror a trailing `kLast*` / count sentinel as a member.
- Do mirror the value RocksDb uses as a **default**, or the property cannot
  express its own default (`Compression.Inherit` = `kDisableCompressionOption`,
  `IoActivity.Unknown` = `kUnknown`).

## Step 5 - native ownership can change

Whether a setter takes a raw pointer, a fresh `shared_ptr` or a copy of an
existing one decides whether the wrapper must free it, must not, or must
register a hold. Read `db/c.cc` at the new tag for any setter the diff touched,
and check it against `docs/articles/ownership.md`:

- fresh `shared_ptr`/`unique_ptr` -> ownership transfers, use
  `AttachExclusively`, the wrapper must not free it;
- copy of an existing `shared_ptr` -> shared, register **no** hold (as for a
  `Cache` or a `RateLimiter`), and the object stays reusable;
- raw pointer RocksDb never frees -> the wrapper owns it, register a hold.

Getting this wrong is either a leak, a double free, or an object the caller
cannot reuse.

## Step 6 - counts, docs, changelog

`.github/workflows/lint.yml` enforces three numbers, so update them together:

- the version badge in `README.md` (`RocksDb-X.Y.Z-blue`),
- the binding count in `README.md` (comma-grouped, e.g. `(1,745 functions)`),
- the binding count in `docs/api/index.md` (`There are 1745 of them`).

It also fails on any stale `RocksDb-x.y.z-blue` or `--version x.y.z` left in
markdown.

For `CHANGELOG.md`, one rule that has been got wrong before:

> **The breaking-changes table lists only members that existed in the previously
> published version.** A type or shape decision taken while the new version was
> in development is not a breaking change for anyone upgrading — it is new API,
> and belongs under Added.

Verify each row against the previous tag rather than from memory:

```bash
git grep -n "MemberName" v11.1.2.1 -- 'RocksDb.Net/*.cs'
git cat-file -e v11.1.2.1:RocksDb.Net/SomeType.cs && echo existed || echo new
```

Nine rows once described migrations for members that never shipped, including an
instruction to remove `(nuint)` casts when the previous release had no public
`nuint` members at all. Also look for the opposite mistake: a member whose
signature changed with **no** row at all.

## Step 7 - verify

```bash
dotnet build -c Release --no-incremental -warnaserror
dotnet test -c Release --no-build
dotnet docfx docs/docfx.json --warningsAsErrors
```

CI runs the suite on ubuntu, macos and windows, because the native library is
platform-specific. There is **no 32-bit leg**, so nothing in the suite exercises
the `size_t` hazards above — those have to be reasoned about, not tested.

Then follow the `release` skill.

## Worth doing while the window is open

A `RocksDbVersion` bump is the only time breaking changes are allowed, so pair
the upgrade with the API cleanup that has accumulated: members that cannot work,
raw integers where an enum belongs, inconsistencies between sibling types, and
anything whose accessibility or nullability is wrong. After the release those
are frozen until the next bump.

New upstream surface is also worth a look: check whether anything previously
unreachable became possible (a factory the C API did not expose, a getter that
was missing), and whether a newly added cluster of exports is worth wrapping.
