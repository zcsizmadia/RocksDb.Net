# RocksDb.Net

A modern C# wrapper for [RocksDb](https://rocksdb.org/), the high-performance embedded key-value store developed by Meta. Built on .NET's `LibraryImport` source generator with zero-copy spans and deterministic disposal.

## Install

```shell
dotnet add package RocksDb.Net
```

The native binaries come from the [RocksDb.Net.Runtimes](https://www.nuget.org/packages/RocksDb.Net.Runtimes) package, which the main package depends on.

Targets .NET 8.0, 9.0 and 10.0.

## Quick start

```csharp
using RocksDbNet;

using var options = new DbOptions { CreateIfMissing = true };
using var db = RocksDb.Open(options, "mydb");

db.Put("key", "value");
string? value = db.GetString("key");
```

`RocksDb.Open` takes ownership of the `DbOptions` it is given, so do not reuse that instance afterwards.

## Where to go next

- **[API reference](xref:RocksDbNet)** for the full surface, generated from the source.
- **[Guides](articles/ownership.md)** for the things worth reading before writing much code: what owns which native handle, how callbacks behave, and which options are only honoured at open time.
- **[Samples](https://github.com/zcsizmadia/RocksDb.Net/tree/main/Samples)** in the repository, one per feature area.

## Versioning

The package version is `<RocksDbVersion>.<Revision>`, so `11.8.1.1` wraps RocksDb 11.8.1. Breaking changes land only when the RocksDb version changes; a revision bump alone never breaks compatibility. See the [changelog](https://github.com/zcsizmadia/RocksDb.Net/blob/main/CHANGELOG.md).
