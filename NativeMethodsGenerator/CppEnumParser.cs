using System.Text;
using System.Text.RegularExpressions;

namespace NativeMethodsGenerator;

/// <summary>One member of a C++ enum, with the comment that describes it.</summary>
public record CEnumMember(string Name, long Value, string? Comment);

/// <summary>A parsed C++ enum.</summary>
public record CEnum(string Name, List<CEnumMember> Members);

/// <summary>
/// Parses a named C++ enum out of a rocksdb header.
/// </summary>
/// <remarks>
/// Deliberately narrow. It handles what the enums this generator reads actually
/// contain: a single explicit value on the first member, implicit sequential
/// values after it, line and block comments above members, and line comments
/// trailing them. Anything else — a second explicit value, an expression, a
/// preprocessor directive inside the body — makes it throw rather than guess,
/// because a mis-numbered enum would silently ask rocksdb for a different
/// counter than the caller named.
/// </remarks>
public static partial class CppEnumParser
{
    /// <summary>
    /// Extracts the enum called <paramref name="enumName"/> from the header.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The enum is missing, or contains something this parser will not guess at.
    /// </exception>
    public static CEnum Parse(string headerText, string enumName)
    {
        Match declaration = Regex.Match(
            headerText,
            $@"^enum\s+{Regex.Escape(enumName)}\s*(:\s*\w+\s*)?\{{",
            RegexOptions.Multiline);

        if (!declaration.Success)
        {
            throw new InvalidOperationException($"enum {enumName} was not found in the header.");
        }

        int bodyStart = declaration.Index + declaration.Length;
        int bodyEnd = headerText.IndexOf("\n};", bodyStart, StringComparison.Ordinal);

        if (bodyEnd < 0)
        {
            throw new InvalidOperationException($"enum {enumName} has no closing brace.");
        }

        var members = new List<CEnumMember>();

        // Text seen since the last member, which describes the next one.
        var pending = new StringBuilder();

        // Whether the last member carried a comment on its own line, in which
        // case a comment-only line straight after it is a continuation of that
        // one rather than a lead-in for the next. Without this the header's
        //
        //     COMPACTION_KEY_DROP_NEWER_ENTRY,  // key was written with a newer
        //                                       // value.
        //     COMPACTION_KEY_DROP_OBSOLETE,     // The key is obsolete.
        //
        // documents OBSOLETE with the tail of NEWER_ENTRY's sentence.
        bool lastMemberHadTrailingComment = false;

        long next = 0;
        bool inBlockComment = false;

        foreach (string rawLine in headerText[bodyStart..bodyEnd].Split('\n'))
        {
            string line = rawLine.Trim();

            if (inBlockComment)
            {
                int close = line.IndexOf("*/", StringComparison.Ordinal);

                Append(pending, StripBlockCommentDecoration(close < 0 ? line : line[..close]));

                if (close >= 0)
                {
                    inBlockComment = false;
                }

                continue;
            }

            if (line.Length == 0)
            {
                // A blank line separates one member's documentation from the
                // next, and ends any trailing-comment continuation.
                pending.Clear();
                lastMemberHadTrailingComment = false;
                continue;
            }

            if (line.StartsWith('#'))
            {
                throw new InvalidOperationException(
                    $"enum {enumName} contains the preprocessor directive '{line}', which this parser does not handle.");
            }

            if (line.StartsWith("/*", StringComparison.Ordinal))
            {
                int close = line.IndexOf("*/", StringComparison.Ordinal);

                Append(pending, StripBlockCommentDecoration(close < 0 ? line[2..] : line[2..close]));

                inBlockComment = close < 0;
                lastMemberHadTrailingComment = false;
                continue;
            }

            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                string text = line[2..].Trim();

                if (lastMemberHadTrailingComment && members.Count > 0)
                {
                    // Continuation of the previous member's trailing comment.
                    CEnumMember previous = members[^1];
                    var extended = new StringBuilder(previous.Comment);

                    Append(extended, text);

                    members[^1] = previous with { Comment = extended.ToString() };
                }
                else
                {
                    Append(pending, text);
                }

                continue;
            }

            // A member, possibly with a comment trailing it on the same line.
            string? trailing = null;
            int commentStart = line.IndexOf("//", StringComparison.Ordinal);

            if (commentStart >= 0)
            {
                trailing = line[(commentStart + 2)..].Trim();
                line = line[..commentStart].Trim();
            }

            Match member = MemberRegex().Match(line);

            if (!member.Success)
            {
                throw new InvalidOperationException(
                    $"enum {enumName} contains the unrecognised line '{line}'.");
            }

            string valueText = member.Groups["value"].Value;

            if (valueText.Length > 0)
            {
                if (!long.TryParse(valueText, out long explicitValue))
                {
                    throw new InvalidOperationException(
                        $"enum {enumName} member '{member.Groups["name"].Value}' has the value '{valueText}', which is not a plain integer.");
                }

                next = explicitValue;
            }

            Append(pending, trailing);

            members.Add(new CEnumMember(
                member.Groups["name"].Value,
                next,
                pending.Length > 0 ? pending.ToString() : null));

            next++;
            pending.Clear();
            lastMemberHadTrailingComment = trailing is { Length: > 0 };
        }

        if (inBlockComment)
        {
            throw new InvalidOperationException($"enum {enumName} has an unterminated block comment.");
        }

        if (members.Count == 0)
        {
            throw new InvalidOperationException($"enum {enumName} has no members.");
        }

        return new CEnum(enumName, members);
    }

    /// <summary>
    /// Converts a rocksdb enum member name to this library's casing:
    /// <c>BLOCK_CACHE_MISS</c> becomes <c>BlockCacheMiss</c>.
    /// </summary>
    public static string ToPascalCase(string screamingSnakeCase)
    {
        var sb = new StringBuilder(screamingSnakeCase.Length);

        foreach (string word in screamingSnakeCase.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(char.ToUpperInvariant(word[0]));

            if (word.Length > 1)
            {
                sb.Append(word[1..].ToLowerInvariant());
            }
        }

        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append(' ');
        }

        sb.Append(text.Trim());
    }

    // Block comment bodies are decorated with a leading asterisk per line.
    private static string StripBlockCommentDecoration(string line)
        => line.Trim().TrimStart('*').Trim();

    [GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z_0-9]*)\s*(=\s*(?<value>-?\d+)\s*)?,?$")]
    private static partial Regex MemberRegex();
}
