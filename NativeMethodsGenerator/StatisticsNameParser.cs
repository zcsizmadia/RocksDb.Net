using System.Text.RegularExpressions;

namespace NativeMethodsGenerator;

/// <summary>
/// Reads RocksDb's own names for its statistics, such as
/// <c>rocksdb.block.cache.miss</c>, out of <c>monitoring/statistics.cc</c>.
/// </summary>
/// <remarks>
/// They are the names RocksDb prints in its statistics dump and that its
/// documentation and existing dashboards use, so the metrics export reports
/// under them rather than under names made up here. They live in a source file
/// rather than a header, as two tables of <c>{ENUMERATOR, "name"}</c> pairs,
/// some of which a formatter has split across lines.
/// </remarks>
public static partial class StatisticsNameParser
{
    /// <summary>
    /// The pairs in the table called <paramref name="tableName"/>, keyed by the
    /// C++ enumerator.
    /// </summary>
    /// <exception cref="FormatException">The table is missing or empty.</exception>
    public static IReadOnlyDictionary<string, string> Parse(string sourceText, string tableName)
    {
        int start = sourceText.IndexOf($"{tableName} = {{", StringComparison.Ordinal);
        if (start < 0)
        {
            throw new FormatException($"'{tableName}' was not found in statistics.cc.");
        }

        int end = sourceText.IndexOf("\n};", start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new FormatException($"The end of '{tableName}' was not found in statistics.cc.");
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match pair in Pair().Matches(sourceText[start..end]))
        {
            string enumerator = pair.Groups["enumerator"].Value;
            string name = pair.Groups["name"].Value;

            if (!names.TryAdd(enumerator, name))
            {
                throw new FormatException($"'{enumerator}' appears twice in '{tableName}'.");
            }
        }

        if (names.Count == 0)
        {
            throw new FormatException($"'{tableName}' has no entries the parser recognised.");
        }

        return names;
    }

    // `{ENUMERATOR, "name"}`, with any whitespace, including line breaks,
    // between the parts.
    [GeneratedRegex("""\{\s*(?<enumerator>[A-Z][A-Z0-9_]*)\s*,\s*"(?<name>[^"]+)"\s*\}""")]
    private static partial Regex Pair();
}
