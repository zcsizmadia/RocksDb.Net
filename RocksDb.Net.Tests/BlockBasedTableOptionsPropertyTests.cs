namespace RocksDbNet.Tests;

/// <summary>
/// Covers the <see cref="BlockBasedTableOptions"/> settings, including the ones
/// that only became round-trippable once RocksDb supplied their getters.
/// See issue #25.
/// </summary>
public class BlockBasedTableOptionsPropertyTests
{
    // ── Previously set-only, now get/set ─────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreExistingBoolProperties_NowRoundTrip(bool value)
    {
        using var opts = new BlockBasedTableOptions();

        opts.NoBlockCache = value;
        opts.WholeKeyFiltering = value;
        opts.CacheIndexAndFilterBlocks = value;
        opts.CacheIndexAndFilterBlocksWithHighPriority = value;
        opts.PinL0FilterAndIndexBlocksInCache = value;
        opts.PartitionFilters = value;
        opts.UseDeltaEncoding = value;
        opts.BlockAlign = value;
        opts.OptimizeFiltersForMemory = value;
        opts.PinTopLevelIndexAndFilter = value;
        opts.SeparateKeyValueInDataBlock = value;

        Assert.Equal(value, opts.NoBlockCache);
        Assert.Equal(value, opts.WholeKeyFiltering);
        Assert.Equal(value, opts.CacheIndexAndFilterBlocks);
        Assert.Equal(value, opts.CacheIndexAndFilterBlocksWithHighPriority);
        Assert.Equal(value, opts.PinL0FilterAndIndexBlocksInCache);
        Assert.Equal(value, opts.PartitionFilters);
        Assert.Equal(value, opts.UseDeltaEncoding);
        Assert.Equal(value, opts.BlockAlign);
        Assert.Equal(value, opts.OptimizeFiltersForMemory);
        Assert.Equal(value, opts.PinTopLevelIndexAndFilter);
        Assert.Equal(value, opts.SeparateKeyValueInDataBlock);
    }

    [Fact]
    public void PreExistingNumericProperties_NowRoundTrip()
    {
        using var opts = new BlockBasedTableOptions();

        opts.BlockSize = 8192;
        opts.BlockSizeDeviation = 15;
        opts.BlockRestartInterval = 32;
        opts.MetadataBlockSize = 8192;
        opts.IndexBlockRestartInterval = 4;

        Assert.Equal(8192UL, opts.BlockSize);
        Assert.Equal(15, opts.BlockSizeDeviation);
        Assert.Equal(32, opts.BlockRestartInterval);
        Assert.Equal(8192UL, opts.MetadataBlockSize);
        Assert.Equal(4, opts.IndexBlockRestartInterval);
    }

    [Fact]
    public void FormatVersion_RoundTrips()
    {
        using var opts = new BlockBasedTableOptions();

        // The native getter returns uint while the setter takes int, so this
        // covers the conversion as much as the option itself.
        opts.FormatVersion = 5;
        Assert.Equal(5, opts.FormatVersion);
    }

    [Fact]
    public void Checksum_RoundTrips()
    {
        using var opts = new BlockBasedTableOptions();

        // Setter takes sbyte, getter returns int.
        opts.Checksum = 4;
        Assert.Equal(4, opts.Checksum);
    }

    [Theory]
    [InlineData(BlockBasedTableIndexType.BinarySearch)]
    [InlineData(BlockBasedTableIndexType.HashSearch)]
    [InlineData(BlockBasedTableIndexType.TwoLevelIndexSearch)]
    public void IndexType_RoundTrips(BlockBasedTableIndexType indexType)
    {
        using var opts = new BlockBasedTableOptions();

        opts.IndexType = indexType;
        Assert.Equal(indexType, opts.IndexType);
    }

    [Theory]
    [InlineData(DataBlockIndexType.BinarySearch)]
    [InlineData(DataBlockIndexType.BinarySearchAndHash)]
    public void DataBlockIndexType_RoundTrips(DataBlockIndexType indexType)
    {
        using var opts = new BlockBasedTableOptions();

        opts.DataBlockIndexType = indexType;
        Assert.Equal(indexType, opts.DataBlockIndexType);
    }

    [Theory]
    [InlineData(IndexBlockSearchType.Binary)]
    [InlineData(IndexBlockSearchType.Interpolation)]
    [InlineData(IndexBlockSearchType.Auto)]
    public void IndexBlockSearchType_RoundTrips(IndexBlockSearchType searchType)
    {
        using var opts = new BlockBasedTableOptions();

        opts.IndexBlockSearchType = searchType;
        Assert.Equal(searchType, opts.IndexBlockSearchType);
    }

    // ── Readahead, filters and user-defined indexes ──────────────────────────

    [Theory]
    [InlineData(IndexShortening.NoShortening)]
    [InlineData(IndexShortening.ShortenSeparators)]
    [InlineData(IndexShortening.ShortenSeparatorsAndSuccessor)]
    public void IndexShortening_RoundTrips(IndexShortening shortening)
    {
        using var opts = new BlockBasedTableOptions();

        opts.IndexShortening = shortening;
        Assert.Equal(shortening, opts.IndexShortening);
    }

    [Theory]
    [InlineData(PrepopulateBlockCache.Disable)]
    [InlineData(PrepopulateBlockCache.FlushOnly)]
    [InlineData(PrepopulateBlockCache.FlushAndCompaction)]
    public void PrepopulateBlockCache_RoundTrips(PrepopulateBlockCache prepopulate)
    {
        using var opts = new BlockBasedTableOptions();

        opts.PrepopulateBlockCache = prepopulate;
        Assert.Equal(prepopulate, opts.PrepopulateBlockCache);
    }

    /// <summary>
    /// Each of these round-trips both ways on its own, without moving any of
    /// the others.
    /// </summary>
    [Fact]
    public void NewBoolProperties_RoundTrip()
    {
        using var opts = new BlockBasedTableOptions();

        BoolProperty.AssertRoundTripsIndependently(
            opts,
            (nameof(opts.EnableIndexCompression), (o, v) => o.EnableIndexCompression = v, o => o.EnableIndexCompression),
            (nameof(opts.VerifyCompression), (o, v) => o.VerifyCompression = v, o => o.VerifyCompression),
            (nameof(opts.DetectFilterConstructCorruption), (o, v) => o.DetectFilterConstructCorruption = v, o => o.DetectFilterConstructCorruption),
            (nameof(opts.DecouplePartitionedFilters), (o, v) => o.DecouplePartitionedFilters = v, o => o.DecouplePartitionedFilters),
            (nameof(opts.UseUdiAsPrimaryIndex), (o, v) => o.UseUdiAsPrimaryIndex = v, o => o.UseUdiAsPrimaryIndex),
            (nameof(opts.FailIfNoUdiOnOpen), (o, v) => o.FailIfNoUdiOnOpen = v, o => o.FailIfNoUdiOnOpen));
    }

    [Fact]
    public void ReadaheadProperties_RoundTrip()
    {
        using var opts = new BlockBasedTableOptions();

        opts.InitialAutoReadaheadSize = 16384;
        opts.MaxAutoReadaheadSize = 524288;
        opts.NumFileReadsForAutoReadahead = 3;

        Assert.Equal(16384UL, opts.InitialAutoReadaheadSize);
        Assert.Equal(524288UL, opts.MaxAutoReadaheadSize);
        Assert.Equal(3UL, opts.NumFileReadsForAutoReadahead);
    }

    [Fact]
    public void ReadAmpBytesPerBit_RoundTrips()
    {
        using var opts = new BlockBasedTableOptions();

        opts.ReadAmpBytesPerBit = 32;
        Assert.Equal(32U, opts.ReadAmpBytesPerBit);
    }

    [Fact]
    public void DataBlockHashTableUtilRatio_RoundTrips()
    {
        using var opts = new BlockBasedTableOptions();

        opts.DataBlockHashTableUtilRatio = 0.5;
        Assert.Equal(0.5, opts.DataBlockHashTableUtilRatio);
    }

    [Fact]
    public void SuperBlockAlignmentProperties_RoundTrip()
    {
        using var opts = new BlockBasedTableOptions();

        opts.SuperBlockAlignmentSize = 4096;
        opts.SuperBlockAlignmentSpaceOverheadRatio = 10;

        Assert.Equal(4096UL, opts.SuperBlockAlignmentSize);
        Assert.Equal(10UL, opts.SuperBlockAlignmentSpaceOverheadRatio);
    }

    [Fact]
    public void UniformCvThreshold_RoundTrips()
    {
        using var opts = new BlockBasedTableOptions();

        opts.UniformCvThreshold = 0.25;
        Assert.Equal(0.25, opts.UniformCvThreshold);
    }

    // ── User-defined index factory ───────────────────────────────────────────

    [Fact]
    public void UserDefinedIndexFactoryName_IsEmptyByDefault()
    {
        using var opts = new BlockBasedTableOptions();

        Assert.True(string.IsNullOrEmpty(opts.UserDefinedIndexFactoryName));
    }

    [Fact]
    public void SetUserDefinedIndexFactoryFromString_UnknownName_Throws()
    {
        using var opts = new BlockBasedTableOptions();

        Assert.Throws<RocksDbException>(() => opts.SetUserDefinedIndexFactoryFromString("not-a-real-factory"));
    }

    [Fact]
    public void SetUserDefinedIndexFactoryFromString_Null_Throws()
    {
        using var opts = new BlockBasedTableOptions();

        Assert.Throws<ArgumentNullException>(() => opts.SetUserDefinedIndexFactoryFromString(null!));
    }

    [Fact]
    public void ClearUserDefinedIndexFactory_OnFreshOptions_DoesNotThrow()
    {
        using var opts = new BlockBasedTableOptions();

        opts.ClearUserDefinedIndexFactory();

        Assert.True(string.IsNullOrEmpty(opts.UserDefinedIndexFactoryName));
    }

    // ── End to end ───────────────────────────────────────────────────────────

    /// <summary>
    /// The options are only read when the table factory is installed, so this
    /// confirms a configured instance actually opens a working database.
    /// </summary>
    [Fact]
    public void ConfiguredOptions_OpenAndReadBack()
    {
        using var dir = new TempDir();
        using var dbOpts = new DbOptions { CreateIfMissing = true };

        var tableOpts = new BlockBasedTableOptions
        {
            BlockSize = 8192,
            IndexShortening = IndexShortening.ShortenSeparatorsAndSuccessor,
            PrepopulateBlockCache = PrepopulateBlockCache.FlushOnly,
            EnableIndexCompression = true,
            DetectFilterConstructCorruption = true,
            DataBlockIndexType = DataBlockIndexType.BinarySearchAndHash,
            DataBlockHashTableUtilRatio = 0.75,
        };

        dbOpts.BlockBasedTableFactory = tableOpts;

        using var db = RocksDb.Open(dbOpts, dir.Path);
        db.Put("a", "1");
        db.Flush();

        Assert.Equal("1", db.GetString("a"));
    }
}
