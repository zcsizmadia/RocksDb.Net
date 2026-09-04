namespace RocksDbNet.Tests;

/// <summary>
/// <see cref="RocksDb.GetAggregatedPropertyInt"/>, which sums an integer
/// property over every column family.
/// </summary>
/// <remarks>
/// The point of the method is the gap it closes, so these are written to fail
/// if it ever collapses back into
/// <see cref="RocksDb.GetPropertyInt(string)"/>: every test with more than one
/// family asserts what the unaggregated call returns as well, and that the two
/// differ.
/// </remarks>
public class AggregatedPropertyTests
{
    private const string EstimatedKeys = "rocksdb.estimate-num-keys";

    /// <summary>Every family counts towards the total, not just the default.</summary>
    [Fact]
    public void SumsAcrossTheColumnFamilies()
    {
        using var options = new DbOptions
        {
            CreateIfMissing = true,
            CreateMissingColumnFamilies = true,
        };

        using RocksDb db = TestDb.OpenInMemory(
            options,
            [new("default"), new("orders"), new("invoices")]);

        Write(db, "default", 3);
        Write(db, "orders", 5);
        Write(db, "invoices", 7);

        // The estimate comes from the memtables until a flush, where it is the
        // entry count and so exact.
        Assert.Equal(15UL, db.GetAggregatedPropertyInt(EstimatedKeys));

        // The unaggregated call sees only the default family, which is the whole
        // reason the aggregated one exists.
        Assert.Equal(3UL, db.GetPropertyInt(EstimatedKeys));
    }

    /// <summary>The keys stay counted once they are in SST files.</summary>
    [Fact]
    public void SumsAcrossTheColumnFamiliesAfterAFlush()
    {
        using var options = new DbOptions
        {
            CreateIfMissing = true,
            CreateMissingColumnFamilies = true,
        };

        using RocksDb db = TestDb.OpenInMemory(options, [new("default"), new("orders")]);

        Write(db, "default", 4);
        Write(db, "orders", 6);

        db.Flush([db.GetColumnFamily("default"), db.GetColumnFamily("orders")]);

        Assert.Equal(10UL, db.GetAggregatedPropertyInt(EstimatedKeys));
        Assert.Equal(4UL, db.GetPropertyInt(EstimatedKeys));
    }

    /// <summary>
    /// A family created after the open is included, because the aggregate walks
    /// the families the database knows about now rather than the ones it was
    /// opened with.
    /// </summary>
    [Fact]
    public void IncludesAFamilyCreatedAfterTheOpen()
    {
        using var options = new DbOptions { CreateIfMissing = true };
        using RocksDb db = TestDb.OpenInMemory(options);

        Write(db, "default", 2);

        Assert.Equal(2UL, db.GetAggregatedPropertyInt(EstimatedKeys));

        using var extra = new DbOptions();

        // Owned by the database, which disposes it as a child.
        ColumnFamilyHandle added = db.CreateColumnFamily(extra, "added");
        WriteTo(db, added, 9);

        Assert.Equal(11UL, db.GetAggregatedPropertyInt(EstimatedKeys));
        Assert.Equal(2UL, db.GetPropertyInt(EstimatedKeys));
    }

    /// <summary>
    /// With one family the aggregate is the same number the unaggregated call
    /// gives, so reaching for it is never wrong.
    /// </summary>
    [Fact]
    public void WithOneFamilyItMatchesTheUnaggregatedCall()
    {
        using var options = new DbOptions { CreateIfMissing = true };
        using RocksDb db = TestDb.OpenInMemory(options);

        Write(db, "default", 6);

        Assert.Equal(6UL, db.GetAggregatedPropertyInt(EstimatedKeys));
        Assert.Equal(db.GetPropertyInt(EstimatedKeys), db.GetAggregatedPropertyInt(EstimatedKeys));
    }

    /// <summary>An empty database aggregates to zero rather than to null.</summary>
    [Fact]
    public void WithNoKeysItIsZero()
    {
        using var options = new DbOptions
        {
            CreateIfMissing = true,
            CreateMissingColumnFamilies = true,
        };

        using RocksDb db = TestDb.OpenInMemory(options, [new("default"), new("orders")]);

        Assert.Equal(0UL, db.GetAggregatedPropertyInt(EstimatedKeys));
    }

    /// <summary>
    /// A property with no integer value is null, the same answer the
    /// unaggregated call gives, rather than zero.
    /// </summary>
    /// <remarks>
    /// Zero would be indistinguishable from a real total, which for a counter is
    /// the most likely value there is.
    /// </remarks>
    [Theory]
    [InlineData("rocksdb.no-such-property")]
    [InlineData("rocksdb.stats")] // Real, but a string property rather than an integer one.
    public void APropertyWithNoIntegerValueIsNull(string propName)
    {
        using var options = new DbOptions
        {
            CreateIfMissing = true,
            CreateMissingColumnFamilies = true,
        };

        using RocksDb db = TestDb.OpenInMemory(options, [new("default"), new("orders")]);

        Write(db, "default", 1);
        Write(db, "orders", 1);

        Assert.Null(db.GetPropertyInt(propName));
        Assert.Null(db.GetAggregatedPropertyInt(propName));
    }

    /// <summary>Size properties aggregate too, not only key counts.</summary>
    [Fact]
    public void AggregatesASizeProperty()
    {
        const string MemtableBytes = "rocksdb.size-all-mem-tables";

        using var options = new DbOptions
        {
            CreateIfMissing = true,
            CreateMissingColumnFamilies = true,
        };

        using RocksDb db = TestDb.OpenInMemory(options, [new("default"), new("orders")]);

        Write(db, "default", 20);
        Write(db, "orders", 20);

        ulong? aggregated = db.GetAggregatedPropertyInt(MemtableBytes);
        ulong? defaultOnly = db.GetPropertyInt(MemtableBytes);

        Assert.NotNull(aggregated);
        Assert.NotNull(defaultOnly);

        // Both families hold the same data. Asserted as an inequality rather
        // than as double, because the figure includes per-family overhead.
        Assert.True(
            aggregated > defaultOnly,
            $"the aggregate {aggregated} was not above the default family's {defaultOnly}");
    }

    /// <summary>The argument guard matches the other property readers.</summary>
    [Fact]
    public void RejectsAMissingPropertyName()
    {
        using var db = new TempDb();

        Assert.Throws<ArgumentNullException>(() => db.Db.GetAggregatedPropertyInt(null!));
        Assert.Throws<ArgumentException>(() => db.Db.GetAggregatedPropertyInt(string.Empty));
    }

    private static void Write(RocksDb db, string family, int count)
        => WriteTo(db, db.GetColumnFamily(family), count);

    private static void WriteTo(RocksDb db, ColumnFamilyHandle cf, int count)
    {
        for (int i = 0; i < count; i++)
        {
            db.Put($"key{i:D3}", new string('v', 64), cf);
        }
    }
}
