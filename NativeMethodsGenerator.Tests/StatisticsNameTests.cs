using NativeMethodsGenerator;

namespace NativeMethodsGenerator.Tests;

/// <summary>
/// Reading RocksDb's statistic names out of <c>statistics.cc</c>, and refusing
/// to generate when one is missing. See issue #192.
/// </summary>
public class StatisticsNameTests
{
    private const string Source = """
        const std::vector<std::pair<Tickers, std::string>> TickersNameMap = {
            {BLOCK_CACHE_MISS, "rocksdb.block.cache.miss"},
            {BLOCK_CACHE_FILTER_BYTES_INSERT,
             "rocksdb.block.cache.filter.bytes.insert"},
        };

        const std::vector<std::pair<Histograms, std::string>> HistogramsNameMap = {
            {DB_GET, "rocksdb.db.get.micros"},
        };
        """;

    [Fact]
    public void ReadsPairs_IncludingOnesSplitAcrossLines()
    {
        IReadOnlyDictionary<string, string> tickers = StatisticsNameParser.Parse(Source, "TickersNameMap");

        Assert.Equal("rocksdb.block.cache.miss", tickers["BLOCK_CACHE_MISS"]);
        Assert.Equal("rocksdb.block.cache.filter.bytes.insert", tickers["BLOCK_CACHE_FILTER_BYTES_INSERT"]);
        Assert.Equal(2, tickers.Count);
    }

    [Fact]
    public void ReadsOnlyTheTableAskedFor()
    {
        IReadOnlyDictionary<string, string> histograms = StatisticsNameParser.Parse(Source, "HistogramsNameMap");

        Assert.Equal("rocksdb.db.get.micros", Assert.Single(histograms).Value);
    }

    [Fact]
    public void AMissingTable_Throws()
        => Assert.Throws<FormatException>(() => StatisticsNameParser.Parse(Source, "NoSuchMap"));

    [Fact]
    public void ARepeatedEnumerator_Throws()
    {
        const string repeated = """
            const std::vector<std::pair<Tickers, std::string>> TickersNameMap = {
                {BLOCK_CACHE_MISS, "a"},
                {BLOCK_CACHE_MISS, "b"},
            };
            """;

        Assert.Throws<FormatException>(() => StatisticsNameParser.Parse(repeated, "TickersNameMap"));
    }

    /// <summary>
    /// A counter RocksDb added without a name stops generation, rather than
    /// exporting under a name made up here.
    /// </summary>
    [Fact]
    public void ACounterWithoutAName_StopsGeneration()
    {
        var tickers = new CEnum("Tickers",
        [
            new CEnumMember("BLOCK_CACHE_MISS", 0, null),
            new CEnumMember("BRAND_NEW_TICKER", 1, null),
            new CEnumMember("TICKER_ENUM_MAX", 2, null),
        ]);
        var histograms = new CEnum("Histograms",
        [
            new CEnumMember("DB_GET", 0, null),
            new CEnumMember("HISTOGRAM_ENUM_MAX", 1, null),
        ]);

        var e = Assert.Throws<InvalidOperationException>(() => StatisticsEnumGenerator.Generate(
            tickers, histograms, "11.8.1", "https://example.invalid/statistics.h",
            StatisticsNameParser.Parse(Source, "TickersNameMap"),
            StatisticsNameParser.Parse(Source, "HistogramsNameMap")));

        Assert.Contains("BRAND_NEW_TICKER", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLookup_MapsEachMemberToItsName()
    {
        var tickers = new CEnum("Tickers",
        [
            new CEnumMember("BLOCK_CACHE_MISS", 0, null),
            new CEnumMember("TICKER_ENUM_MAX", 1, null),
        ]);
        var histograms = new CEnum("Histograms",
        [
            new CEnumMember("DB_GET", 0, null),
            new CEnumMember("HISTOGRAM_ENUM_MAX", 1, null),
        ]);

        string generated = StatisticsEnumGenerator.Generate(
            tickers, histograms, "11.8.1", "https://example.invalid/statistics.h",
            StatisticsNameParser.Parse(Source, "TickersNameMap"),
            StatisticsNameParser.Parse(Source, "HistogramsNameMap"));

        Assert.Contains("Ticker.BlockCacheMiss => \"rocksdb.block.cache.miss\",", generated, StringComparison.Ordinal);
        Assert.Contains("Histogram.DbGet => \"rocksdb.db.get.micros\",", generated, StringComparison.Ordinal);
    }
}
