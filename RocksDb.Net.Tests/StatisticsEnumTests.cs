namespace RocksDbNet.Tests;

/// <summary>
/// The generated <see cref="Ticker"/> and <see cref="Histogram"/> enums.
/// </summary>
/// <remarks>
/// <para>
/// These come from <c>statistics.h</c> at the pinned version rather than from
/// <c>c.h</c>, because the C API takes a counter as a plain integer and does not
/// declare what the integers mean. Before them, <c>GetTickerCount</c> and
/// <c>GetHistogramData</c> took a bare number that a caller had to look up in
/// RocksDb's header.
/// </para>
/// <para>
/// CI regenerates the file and fails on any difference, so these tests are not
/// checking the generator's arithmetic. They check the properties a wrong
/// generation would break and a diff would not explain: no gaps, no duplicates,
/// no sentinel, and a value that RocksDb agrees with.
/// </para>
/// </remarks>
public class StatisticsEnumTests
{
    [Fact]
    public void Ticker_IsDenseAndStartsAtZero()
    {
        int[] values = [.. Enum.GetValues<Ticker>().Select(v => (int)v).Order()];

        Assert.Equal(263, values.Length);
        Assert.Equal(0, values[0]);

        // Positional values, so a gap means the generator dropped a member and
        // everything after it now names the wrong counter.
        Assert.Equal(Enumerable.Range(0, values.Length), values);
    }

    [Fact]
    public void Histogram_IsDenseAndStartsAtZero()
    {
        int[] values = [.. Enum.GetValues<Histogram>().Select(v => (int)v).Order()];

        Assert.Equal(80, values.Length);
        Assert.Equal(Enumerable.Range(0, values.Length), values);
    }

    /// <summary>
    /// The sentinels RocksDb uses to size its own arrays are not counters, and
    /// asking for one would read past the end.
    /// </summary>
    [Fact]
    public void Sentinels_AreNotMembers()
    {
        Assert.DoesNotContain(
            Enum.GetNames<Ticker>(),
            name => name.Contains("EnumMax", StringComparison.Ordinal));

        Assert.DoesNotContain(
            Enum.GetNames<Histogram>(),
            name => name.Contains("EnumMax", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two members cannot share a name after the conversion out of RocksDb's
    /// casing, and none may collide with a different counter's number.
    /// </summary>
    [Fact]
    public void Members_AreUnique()
    {
        Assert.Equal(Enum.GetNames<Ticker>().Length, Enum.GetNames<Ticker>().Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enum.GetNames<Histogram>().Length, Enum.GetNames<Histogram>().Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The numbering agrees with the running library, not just with the header
    /// it was generated from.
    /// </summary>
    /// <remarks>
    /// A counter RocksDb increments exactly once per written key is the one that
    /// can be checked without knowing anything else: write five keys, and if
    /// <see cref="Ticker.NumberKeysWritten"/> really is that counter it reads
    /// five. A generation off by one would land on a neighbouring counter and
    /// report something else.
    /// </remarks>
    [Fact]
    public void Ticker_NumberingAgreesWithTheRunningLibrary()
    {
        using var dir = new TempDir();

        var options = new DbOptions { CreateIfMissing = true };
        options.EnableStatistics();

        using RocksDb db = RocksDb.Open(options, dir.Path);

        for (int i = 0; i < 5; i++)
        {
            db.Put($"key{i}", "value");
        }

        Assert.Equal(5UL, options.GetTickerCount(Ticker.NumberKeysWritten));
        Assert.Equal(0UL, options.GetTickerCount(Ticker.NumberKeysRead));

        _ = db.GetString("key0");
        _ = db.GetString("key1");

        Assert.Equal(2UL, options.GetTickerCount(Ticker.NumberKeysRead));

        // Still five, so the read did not increment the write counter, which a
        // pair of adjacent numbers off by one would show.
        Assert.Equal(5UL, options.GetTickerCount(Ticker.NumberKeysWritten));
    }

    /// <summary>
    /// Every counter can be read without the native side rejecting the id, so
    /// nothing in the enum is out of range.
    /// </summary>
    [Fact]
    public void EveryCounterCanBeRead()
    {
        using var dir = new TempDir();

        var options = new DbOptions { CreateIfMissing = true };
        options.EnableStatistics();

        using RocksDb db = RocksDb.Open(options, dir.Path);

        db.Put("a", "1");
        db.Flush();

        foreach (Ticker ticker in Enum.GetValues<Ticker>())
        {
            _ = options.GetTickerCount(ticker);
        }

        foreach (Histogram histogram in Enum.GetValues<Histogram>())
        {
            Assert.NotNull(options.GetHistogramData(histogram));
        }
    }
}
