using NativeMethodsGenerator;

const string HeaderUrlTemplate =
    "https://raw.githubusercontent.com/facebook/rocksdb/v{0}/include/rocksdb/{1}";

// ── Parse arguments ──────────────────────────────────────────────────────────
// Usage: NativeMethodsGenerator --version <version> [--project <path>]
//
// Normal operation is just the version, run from the repository root:
//
//     dotnet run --project NativeMethodsGenerator -- --version 11.8.1
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
        $"Usage: NativeMethodsGenerator --version <rocksdb-version> [--project <path>, default {DefaultProjectDir}]");
}

if (string.IsNullOrEmpty(version))
{
    Console.Error.WriteLine("Error: --version <rocksdb-version> is required.");
    PrintUsage();
    return 1;
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
    return 1;
}

// Where each generated file belongs. The bindings sit under Native/ with the
// rest of the interop; the enums are public API and sit beside it.
var outputPath = Path.Combine(projectDir, "Native", "NativeMethods.g.cs");
var statisticsOutputPath = Path.Combine(projectDir, "StatisticsEnums.g.cs");

var url = string.Format(HeaderUrlTemplate, version, "c.h");
var statisticsUrl = string.Format(HeaderUrlTemplate, version, "statistics.h");

// ── Fetch headers ───────────────────────────────────────────────────────────

// Two headers, because the statistics counters are not declared in c.h.
// The C API takes them as plain integers, so nothing about them reaches a
// caller unless they are read from where they are actually defined.
string headerText;
string statisticsHeaderText;

using (var http = new HttpClient())
{
    Console.WriteLine($"Fetching c.h from {url} ...");
    headerText = await http.GetStringAsync(url);
    Console.WriteLine($"  Fetched {headerText.Length:N0} characters.");

    Console.WriteLine($"Fetching statistics.h from {statisticsUrl} ...");
    statisticsHeaderText = await http.GetStringAsync(statisticsUrl);
    Console.WriteLine($"  Fetched {statisticsHeaderText.Length:N0} characters.");
}

// ── Parse ────────────────────────────────────────────────────────────────────

Console.WriteLine("Parsing exported functions ...");

var functions = CHeaderParser.Parse(headerText);
Console.WriteLine($"  Found {functions.Count} exported functions.");

// ── Generate ─────────────────────────────────────────────────────────────────

Console.WriteLine("Generating P/Invoke declarations ...");

var code = PInvokeGenerator.Generate(functions, version, url);

var fullPath = Path.GetFullPath(outputPath);
Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
await File.WriteAllTextAsync(fullPath, code);

Console.WriteLine($"  Wrote {fullPath}");

// ── Statistics enums ────────────────────────────────────────────────────────

Console.WriteLine("Parsing the statistics enums ...");

var tickers = CppEnumParser.Parse(statisticsHeaderText, "Tickers");
var histograms = CppEnumParser.Parse(statisticsHeaderText, "Histograms");

Console.WriteLine($"  Found {tickers.Members.Count} tickers and {histograms.Members.Count} histograms.");

var statisticsCode = StatisticsEnumGenerator.Generate(tickers, histograms, version, statisticsUrl);

var statisticsFullPath = Path.GetFullPath(statisticsOutputPath);
Directory.CreateDirectory(Path.GetDirectoryName(statisticsFullPath)!);
await File.WriteAllTextAsync(statisticsFullPath, statisticsCode);

Console.WriteLine($"  Wrote {statisticsFullPath}");
Console.WriteLine("Done.");
return 0;
