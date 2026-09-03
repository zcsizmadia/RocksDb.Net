namespace RocksDbNet.Tests;

/// <summary>
/// The C API declares these parameters as a plain <c>int</c>, so the values are
/// hand-mirrored from the RocksDb C++ headers. If they drift, options are set to
/// the wrong thing silently, hence these tests.
/// </summary>
public class TemperatureEnumTests
{
    /// <summary>
    /// From <c>include/rocksdb/types.h</c>. Deliberately non-contiguous: RocksDb
    /// reserves the gaps for tiers inserted later.
    /// </summary>
    [Fact]
    public void Temperature_HasTheNativeValues()
    {
        Assert.Equal(0, (int)Temperature.Unknown);
        Assert.Equal(0x04, (int)Temperature.Hot);
        Assert.Equal(0x08, (int)Temperature.Warm);
        Assert.Equal(0x0A, (int)Temperature.Cool);
        Assert.Equal(0x0C, (int)Temperature.Cold);
        Assert.Equal(0x10, (int)Temperature.Ice);
    }

    /// <summary>From <c>include/rocksdb/advanced_options.h</c>.</summary>
    [Fact]
    public void CacheTier_HasTheNativeValues()
    {
        Assert.Equal(0, (int)CacheTier.Volatile);
        Assert.Equal(1, (int)CacheTier.VolatileCompressed);
        Assert.Equal(2, (int)CacheTier.NonVolatileBlock);
    }

    /// <summary>From <c>include/rocksdb/types.h</c>, in declaration order.</summary>
    [Fact]
    public void FileType_HasTheNativeValues()
    {
        Assert.Equal(0, (int)FileType.WalFile);
        Assert.Equal(1, (int)FileType.DbLockFile);
        Assert.Equal(2, (int)FileType.TableFile);
        Assert.Equal(3, (int)FileType.DescriptorFile);
        Assert.Equal(4, (int)FileType.CurrentFile);
        Assert.Equal(5, (int)FileType.TempFile);
        Assert.Equal(6, (int)FileType.InfoLogFile);
        Assert.Equal(7, (int)FileType.MetaDatabase);
        Assert.Equal(8, (int)FileType.IdentityFile);
        Assert.Equal(9, (int)FileType.OptionsFile);
        Assert.Equal(10, (int)FileType.BlobFile);
        Assert.Equal(11, (int)FileType.CompactionProgressFile);
    }
}
