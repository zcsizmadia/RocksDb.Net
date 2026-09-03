# Third-party notices

RocksDb.Net is a wrapper. It is not affiliated with, endorsed by, or sponsored by Meta Platforms, Inc.

## RocksDB

RocksDB is developed and maintained by Meta Platforms, Inc. (formerly Facebook, Inc.) and contributors.

- Project: <https://rocksdb.org/>
- Source: <https://github.com/facebook/rocksdb>
- Copyright (c) 2011-present, Facebook, Inc. All rights reserved.

RocksDB is dual-licensed under the GPLv2 (found in the `COPYING` file in its root directory) and the Apache 2.0 License (found in its `LICENSE.Apache` file). A recipient may choose either.

The Apache 2.0 text is included with this package, at [`licenses/LICENSE.rocksdb-Apache-2.0.txt`](licenses/LICENSE.rocksdb-Apache-2.0.txt), because Apache 2.0 section 4(a) asks for a copy of the licence rather than a pointer to one. The GPLv2 alternative is in [RocksDB's repository](https://github.com/facebook/rocksdb/blob/main/COPYING). Consult those for the authoritative terms.

RocksDB publishes no `NOTICE` file, so Apache 2.0 section 4(d) adds nothing to carry.

This project uses RocksDB in two ways:

**The generated bindings.** `RocksDb.Net/Native/NativeMethods.g.cs` is generated from RocksDB's public C header, [`include/rocksdb/c.h`](https://github.com/facebook/rocksdb/blob/main/include/rocksdb/c.h). The generated file carries RocksDB's copyright and licence notice in its header, since it is derived from that header.

**The native binaries.** The compiled RocksDB libraries are distributed separately through the [RocksDb.Net.Runtimes](https://github.com/zcsizmadia/RocksDb.Net.Runtimes) package, not by this repository. RocksDB's licence terms apply to those binaries.

RocksDB itself builds on other open-source work, including LevelDB by Google, Inc. Those notices are carried in the RocksDB source tree.

## RocksDb.Net

The wrapper code in this repository, everything other than the generated bindings noted above, is licensed under the MIT Licence. See [LICENSE](LICENSE).

Choosing this wrapper does not change your obligations under RocksDB's licence. If those obligations matter to your distribution, read RocksDB's own licence files rather than relying on this summary.
