using RocksDbNet.Native;

namespace RocksDbNet;

public sealed class EnvOptions : RocksDbHandle
{
    public EnvOptions()
    {
        Handle = NativeMethods.rocksdb_envoptions_create();
    }

    public override void DisposeUnmanagedResources()
    {
        NativeMethods.rocksdb_envoptions_destroy(Handle);
    }
}
