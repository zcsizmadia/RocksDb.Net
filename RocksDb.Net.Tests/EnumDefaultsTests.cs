namespace RocksDbNet.Tests;

/// <summary>
/// Options whose default value has to be a named member of its enum.
/// </summary>
/// <remarks>
/// Two of these enums stopped short of the value RocksDb uses as the default,
/// so reading the property on fresh options returned a number no member
/// matched: <c>Enum.IsDefined</c> said false, <c>ToString</c> gave the digits,
/// a <c>switch</c> fell through every case, and a caller who had set the option
/// could not put it back. Found by the pre-release review.
/// </remarks>
public class EnumDefaultsTests
{
    /// <summary>
    /// A fresh <see cref="ReadOptions"/> reports no I/O activity, and that is a
    /// member rather than a bare number.
    /// </summary>
    [Fact]
    public void IoActivity_DefaultsToADefinedMember()
    {
        using var options = new ReadOptions();

        Assert.Equal(IoActivity.Unknown, options.IoActivity);
        Assert.True(Enum.IsDefined(options.IoActivity), $"{options.IoActivity} is not a member");

        // And it can be set back, which was the part that could not be
        // expressed at all before.
        options.IoActivity = IoActivity.Get;
        Assert.Equal(IoActivity.Get, options.IoActivity);

        options.IoActivity = IoActivity.Unknown;
        Assert.Equal(IoActivity.Unknown, options.IoActivity);
    }

    /// <summary>
    /// A fresh <see cref="DbOptions"/> inherits its bottommost compression from
    /// <see cref="DbOptions.Compression"/> rather than naming an algorithm.
    /// </summary>
    /// <remarks>
    /// <see cref="Compression.Inherit"/> is not <see cref="Compression.None"/>:
    /// one selects nothing at all, the other selects no compression. Reading
    /// the default used to give neither.
    /// </remarks>
    [Fact]
    public void BottommostCompression_DefaultsToADefinedMember()
    {
        using var options = new DbOptions();

        Assert.Equal(Compression.Inherit, options.BottommostCompression);
        Assert.True(
            Enum.IsDefined(options.BottommostCompression),
            $"{options.BottommostCompression} is not a member");

        options.BottommostCompression = Compression.Zstd;
        Assert.Equal(Compression.Zstd, options.BottommostCompression);

        // Restoring the default is only possible because the member exists.
        options.BottommostCompression = Compression.Inherit;
        Assert.Equal(Compression.Inherit, options.BottommostCompression);
        Assert.NotEqual(Compression.None, options.BottommostCompression);
    }

    /// <summary>
    /// A read is not charged to the rate limiter unless it asks to be, and the
    /// value that says so is the default.
    /// </summary>
    /// <remarks>
    /// <c>RateLimiterPriority.Total</c> was documented as a count that it was a
    /// programming error to pass. It is the opposite: RocksDb declares
    /// <c>rate_limiter_priority = Env::IO_TOTAL</c> and documents that the
    /// special value disables charging the rate limiter. Removing it, which is
    /// what the review recommended, would have deleted the only way to say
    /// "do not rate-limit this".
    /// </remarks>
    [Fact]
    public void RateLimiterPriority_DefaultsToNotBeingCharged()
    {
        using var options = new ReadOptions();

        Assert.Equal(RateLimiterPriority.Total, options.RateLimiterPriority);
        Assert.True(
            Enum.IsDefined(options.RateLimiterPriority),
            $"{options.RateLimiterPriority} is not a member");

        options.RateLimiterPriority = RateLimiterPriority.High;
        Assert.Equal(RateLimiterPriority.High, options.RateLimiterPriority);

        options.RateLimiterPriority = RateLimiterPriority.Total;
        Assert.Equal(RateLimiterPriority.Total, options.RateLimiterPriority);
    }

    /// <summary>
    /// The tiers read as their own names rather than repeating the type's.
    /// </summary>
    /// <remarks>
    /// A rename, taken while the enum was still new in this release and so had
    /// no callers to protect. <c>ReadTier.ReadAllTier</c> and
    /// <c>ReadTier.BlockCacheTier</c> stuttered where every sibling enum —
    /// <c>CacheTier</c>, <c>PinningTier</c> — does not. The numeric values are
    /// unchanged, which is what matters to RocksDb.
    /// </remarks>
    [Fact]
    public void ReadTier_MembersDoNotRepeatTheTypeName()
    {
        Assert.Equal(0, (int)ReadTier.All);
        Assert.Equal(1, (int)ReadTier.BlockCache);
        Assert.Equal(2, (int)ReadTier.Persisted);
        Assert.Equal(3, (int)ReadTier.Memtable);

        using var options = new ReadOptions();

        Assert.Equal(ReadTier.All, options.ReadTier);

        options.ReadTier = ReadTier.BlockCache;
        Assert.Equal(ReadTier.BlockCache, options.ReadTier);
    }

    /// <summary>
    /// The compact-on-deletion collector takes its sizes as <c>ulong</c>, so no
    /// cast is needed and the meaning does not change with the process width.
    /// </summary>
    [Fact]
    public void CompactOnDeletionCollector_TakesUnsignedLongSizes()
    {
        using var options = new DbOptions { CreateIfMissing = true };

        // No (nuint) casts. This was the last nuint in the public API.
        options.AddCompactOnDeletionCollector(
            windowSize: 1000UL, deletionTrigger: 500UL, deletionRatio: 0.5, minFileSize: 64UL * 1024);

        options.Env = Env.CreateInMemory();

        using RocksDb db = RocksDb.Open(options, TestDb.InMemoryPath);
        db.Put("k", "v");
        Assert.Equal("v", db.GetString("k"));
    }
}
