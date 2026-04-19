using RocksDbNet.Native;

namespace RocksDbNet;

/// <summary>
/// Options for configuring the RocksDB environment used by
/// <see cref="SstFileWriter"/>.
/// </summary>
public sealed class EnvOptions : RocksDbHandle
{
    public EnvOptions()
    {
        Handle = NativeMethods.rocksdb_envoptions_create();
    }

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_envoptions_destroy(Handle);
    }
}
