using NativeMethodsGenerator;

const string HeaderUrlTemplate =
    "https://raw.githubusercontent.com/facebook/rocksdb/v{0}/include/rocksdb/{1}";

// ── Parse arguments ──────────────────────────────────────────────────────────
// Usage: NativeMethodsGenerator [--version <version>] [--project <path>]
//
// Normal operation takes no arguments at all, run from the repository root:
//
//     dotnet run --project NativeMethodsGenerator
//
// The version comes from RocksDbVersion in Directory.Build.props, which is
// the property that decides which native library the package binds to.
// Bumping it is the first step of an upgrade, so reading it here means the
// generator cannot be run against a version the build is not pinned to by
// accident. --version overrides it, for trying a version out before
// committing to it.
//
// One directory rather than a path per file. Everything this generates is
// part of one library and has to be regenerated and committed together, so
// naming the files individually only created a way to update one and forget
// the other. The layout below is this repository's, and the generator owns
// it: callers say which project to write into, if not the usual one, and
// never where each file goes.

string? version = null;
string? projectDir = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--version" when i + 1 < args.Length:
            version = args[++i];
            break;
        case "--project" when i + 1 < args.Length:
            projectDir = args[++i];
            break;
    }
}

// The library this generator exists to write into. A caller outside this
// repository can override it, but nobody here should have to.
const string DefaultProjectDir = "RocksDb.Net";

static void PrintUsage()
{
    Console.Error.WriteLine(
        "Usage: NativeMethodsGenerator " +
        $"[--version <rocksdb-version>, default <{PinnedVersion.PropertyName}> from {PinnedVersion.PropsFileName}] " +
        $"[--project <path>, default {DefaultProjectDir}]");
}

projectDir ??= DefaultProjectDir;

// Checked rather than created. The default is relative, so running from the
// wrong directory would otherwise make a new RocksDb.Net folder somewhere
// unhelpful and report success while the real generated files went stale.
if (!Directory.Exists(projectDir))
{
    Console.Error.WriteLine(
        $"Error: the project directory '{projectDir}' does not exist. Run this from the " +
        "repository root, or pass --project <path>.");

    PrintUsage();
    return 1;
}

string resolvedVersion;
string versionSource;

if (string.IsNullOrEmpty(version))
{
    if (!PinnedVersion.TryRead(PinnedVersion.PropsFileName, out string? pinned, out string? readError))
    {
        Console.Error.WriteLine($"Error: {readError}");
        Console.Error.WriteLine(
            $"The version normally comes from <{PinnedVersion.PropertyName}> in " +
            $"{PinnedVersion.PropsFileName}. Run this from the repository root, or pass --version.");

        PrintUsage();
        return 1;
    }

    resolvedVersion = pinned!;
    versionSource = $"{PinnedVersion.PropsFileName} <{PinnedVersion.PropertyName}>";
}
else
{
    resolvedVersion = version;
    versionSource = "--version";
}

// Where each generated file belongs. The bindings sit under Native/ with the
// rest of the interop; the enums are public API and sit beside it.
var outputPath = Path.Combine(projectDir, "Native", "NativeMethods.g.cs");
var statisticsOutputPath = Path.Combine(projectDir, "StatisticsEnums.g.cs");

var url = string.Format(HeaderUrlTemplate, resolvedVersion, "c.h");
var statisticsUrl = string.Format(HeaderUrlTemplate, resolvedVersion, "statistics.h");

// ── Say what is about to happen ─────────────────────────────────────────────

Console.WriteLine($"RocksDb version : {resolvedVersion}  (from {versionSource})");
Console.WriteLine($"Project         : {Path.GetFullPath(projectDir)}");
Console.WriteLine();
Console.WriteLine("Generating:");
Console.WriteLine($"  P/Invoke bindings   {Path.GetFullPath(outputPath)}");
Console.WriteLine($"    from              {url}");
Console.WriteLine($"  Statistics enums    {Path.GetFullPath(statisticsOutputPath)}");
Console.WriteLine($"    from              {statisticsUrl}");
Console.WriteLine();

// ── Fetch headers ───────────────────────────────────────────────────────────

// Two headers, because the statistics counters are not declared in c.h.
// The C API takes them as plain integers, so nothing about them reaches a
// caller unless they are read from where they are actually defined.
string headerText;
string statisticsHeaderText;

Console.WriteLine("Fetching headers ...");

using (var http = new HttpClient())
{
    headerText = await http.GetStringAsync(url);
    Console.WriteLine($"  c.h            {headerText.Length,9:N0} characters");

    statisticsHeaderText = await http.GetStringAsync(statisticsUrl);
    Console.WriteLine($"  statistics.h   {statisticsHeaderText.Length,9:N0} characters");
}

// ── Parse and write ─────────────────────────────────────────────────────────

var functions = CHeaderParser.Parse(headerText);
var tickers = CppEnumParser.Parse(statisticsHeaderText, "Tickers");
var histograms = CppEnumParser.Parse(statisticsHeaderText, "Histograms");

var fullPath = Path.GetFullPath(outputPath);
Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
await File.WriteAllTextAsync(fullPath, PInvokeGenerator.Generate(functions, resolvedVersion, url));

var statisticsFullPath = Path.GetFullPath(statisticsOutputPath);
Directory.CreateDirectory(Path.GetDirectoryName(statisticsFullPath)!);
await File.WriteAllTextAsync(
    statisticsFullPath,
    StatisticsEnumGenerator.Generate(tickers, histograms, resolvedVersion, statisticsUrl));

Console.WriteLine();
Console.WriteLine("Wrote:");
Console.WriteLine($"  NativeMethods.g.cs     {functions.Count,5:N0} bindings");

// The sentinel each enum ends with is not a counter and is not emitted.
Console.WriteLine(
    $"  StatisticsEnums.g.cs   {tickers.Members.Count - 1,5:N0} tickers, " +
    $"{histograms.Members.Count - 1:N0} histograms");

Console.WriteLine();
Console.WriteLine("Done.");
return 0;
