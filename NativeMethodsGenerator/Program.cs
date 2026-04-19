using NativeMethodsGenerator;

const string UrlTemplate =
    "https://raw.githubusercontent.com/facebook/rocksdb/v{0}/include/rocksdb/c.h";

// ── Parse arguments ──────────────────────────────────────────────────────────
// Usage: NativeMethodsGenerator --version <version> --output <path>

string? version = null;
string? outputPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--version" when i + 1 < args.Length:
            version = args[++i];
            break;
        case "--output" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
    }
}

if (string.IsNullOrEmpty(version))
{
    Console.Error.WriteLine("Error: --version <rocksdb-version> is required.");
    Console.Error.WriteLine("Usage: NativeMethodsGenerator --version 11.0.4 [--output path/NativeMethods.g.cs]");
    return 1;
}

outputPath ??= "NativeMethods.g.cs";
var url = string.Format(UrlTemplate, version);

// ── Fetch header ─────────────────────────────────────────────────────────────

Console.WriteLine($"Fetching c.h from {url} ...");

string headerText;
using (var http = new HttpClient())
{
    headerText = await http.GetStringAsync(url);
}

Console.WriteLine($"  Fetched {headerText.Length:N0} characters.");

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
Console.WriteLine("Done.");
return 0;
