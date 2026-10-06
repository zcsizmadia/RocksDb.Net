using System.Text.RegularExpressions;

namespace RocksDbNet.Tests;

/// <summary>
/// Every wrapper whose native handle is passed into a call stays reachable
/// until that call returns. See issue #178.
/// </summary>
/// <remarks>
/// <para>
/// Reading <c>options.Handle</c> extracts a raw pointer, and nothing in the
/// pointer keeps <c>options</c> alive. Once the JIT sees no later use of the
/// variable it may report it dead, so a collection on another thread can run
/// its finalizer and destroy the native struct while RocksDb is still using
/// it. <c>db.Put(k, v, new WriteOptions { Sync = true })</c> is enough, since
/// nothing else refers to that object.
/// </para>
/// <para>
/// The rule this checks is the JIT's own: a variable is live across a call if
/// the method uses it again afterwards, on any path. So it is a source scan
/// rather than a runtime test, which could only ever catch the race by luck.
/// It reads the library's sources and, for each native call, requires every
/// local or parameter whose handle the call takes to be mentioned again after
/// the call, before the method ends. <c>GC.KeepAlive</c> is how a method that
/// had no reason to mention it again does so.
/// </para>
/// <para>
/// Exempt are handles of objects that cannot be finalized while their
/// database is in use, because the database's child list holds them: column
/// family handles, snapshots, iterators and transactions. Fields are exempt
/// too: they live as long as the object that holds them, which is the
/// receiver, and the receiver is the caller's to keep.
/// </para>
/// </remarks>
public class KeepAliveSourceTests
{
    // Database children, kept reachable by their parent while it is open.
    private static readonly HashSet<string> Exempt =
    [
        "cf", "cfh", "columnFamily", "snapshot", "iterator", "it", "transaction", "txn",
    ];

    private static readonly Regex HandleRead = new(
        @"\b(?<name>[a-z]\w*)!?\.Handle\b|\(\s*(?<name>[a-z]\w*)\s*\?\?[^()]*\)\.Handle\b",
        RegexOptions.Compiled);

    private static readonly Regex MemberDeclaration = new(
        @"^(?<indent>\s*)(public|private|internal|protected)\b",
        RegexOptions.Compiled);

    [Fact]
    public void EveryWrapperPassedToANativeCall_IsUsedAgainAfterIt()
    {
        var violations = new List<string>();

        foreach (string path in Directory.GetFiles(LibraryDirectory(), "*.cs"))
        {
            violations.AddRange(Check(path));
        }

        Assert.True(violations.Count == 0,
            "These native calls take a wrapper's handle and nothing keeps the wrapper alive " +
            "until the call returns. Add GC.KeepAlive after the call:\n" +
            string.Join("\n", violations));
    }

    private static IEnumerable<string> Check(string path)
    {
        string[] lines = File.ReadAllLines(path).Select(StripComment).ToArray();
        string file = Path.GetFileName(path);

        for (int i = 0; i < lines.Length; i++)
        {
            int start = lines[i].IndexOf("NativeMethods.rocksdb_", StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            int end = StatementEnd(lines, i, start, out string call);
            int methodEnd = MethodEnd(lines, i);
            string after = string.Join("\n", lines.Skip(end + 1).Take(Math.Max(0, methodEnd - end)));

            // The rest of the last line of the call, too: `...); GC.KeepAlive(x);`.
            string tail = lines[end][Math.Min(lines[end].Length, LastParen(lines[end]) + 1)..];

            foreach (string name in HandleRead.Matches(call)
                         .Select(m => m.Groups["name"].Value)
                         .Distinct()
                         .Where(n => !Exempt.Contains(n)))
            {
                var laterUse = new Regex($@"\b{Regex.Escape(name)}\b");

                if (!laterUse.IsMatch(after) && !laterUse.IsMatch(tail))
                {
                    yield return $"  {file}:{i + 1}  {name}";
                }
            }
        }
    }

    /// <summary>The line on which the call starting at (line, column) closes.</summary>
    private static int StatementEnd(string[] lines, int line, int column, out string call)
    {
        int depth = 0;
        bool opened = false;
        var text = new System.Text.StringBuilder();

        for (int i = line; i < lines.Length; i++)
        {
            string segment = i == line ? lines[i][column..] : lines[i];

            foreach (char c in segment)
            {
                text.Append(c);

                if (c == '(')
                {
                    depth++;
                    opened = true;
                }
                else if (c == ')')
                {
                    depth--;

                    if (opened && depth == 0)
                    {
                        call = text.ToString();
                        return i;
                    }
                }
            }

            text.Append('\n');
        }

        call = text.ToString();
        return lines.Length - 1;
    }

    /// <summary>
    /// The closing brace of the member containing <paramref name="line"/>, or
    /// the line itself for an expression-bodied member, which has nowhere to
    /// put a later use.
    /// </summary>
    private static int MethodEnd(string[] lines, int line)
    {
        string? indent = null;

        for (int i = line; i >= 0; i--)
        {
            Match m = MemberDeclaration.Match(lines[i]);
            if (m.Success)
            {
                indent = m.Groups["indent"].Value;
                break;
            }
        }

        if (indent is null)
        {
            return line;
        }

        string close = indent + "}";

        for (int i = line; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd() == close)
            {
                return i;
            }

            // The next member started without this one closing: it was
            // expression-bodied.
            if (i > line && MemberDeclaration.Match(lines[i]) is { Success: true } next
                && next.Groups["indent"].Value == indent)
            {
                return i - 1;
            }
        }

        return lines.Length - 1;
    }

    private static int LastParen(string line) => line.LastIndexOf(')');

    private static string StripComment(string line)
    {
        int comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    private static string LibraryDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RocksDb.Net.slnx")))
            {
                return Path.Combine(dir.FullName, "RocksDb.Net");
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root above " + AppContext.BaseDirectory);
    }
}
