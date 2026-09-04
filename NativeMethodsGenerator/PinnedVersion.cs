using System.Xml.Linq;

namespace NativeMethodsGenerator;

/// <summary>
/// Reads the pinned RocksDb version out of <c>Directory.Build.props</c>.
/// </summary>
/// <remarks>
/// That property is the single source of truth for which native library the
/// package binds to, and bumping it is the first step of an upgrade. Reading it
/// here means the generator needs no argument in normal use, and cannot be run
/// for a version the build is not pinned to by accident.
/// <para>
/// The XML is read directly rather than asking msbuild for the evaluated
/// property. It is a literal in one file, and shelling out to msbuild from
/// inside a dotnet tool costs seconds and a working SDK on the path for no
/// benefit.
/// </para>
/// </remarks>
public static class PinnedVersion
{
    /// <summary>The property that pins the native library version.</summary>
    public const string PropertyName = "RocksDbVersion";

    /// <summary>The file it lives in, relative to the repository root.</summary>
    public const string PropsFileName = "Directory.Build.props";

    /// <summary>
    /// Returns the pinned version, or <see langword="null"/> with a reason when
    /// it cannot be read.
    /// </summary>
    public static bool TryRead(string propsPath, out string? version, out string? error)
    {
        version = null;
        error = null;

        if (!File.Exists(propsPath))
        {
            error = $"'{propsPath}' was not found.";
            return false;
        }

        XDocument document;

        try
        {
            document = XDocument.Load(propsPath);
        }
        catch (Exception ex)
        {
            error = $"'{propsPath}' could not be parsed: {ex.Message}";
            return false;
        }

        // No namespace on the project file, so a plain local-name match is
        // enough and stays right if one is ever added.
        string? value = document
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == PropertyName)
            ?.Value
            .Trim();

        if (string.IsNullOrEmpty(value))
        {
            error = $"'{propsPath}' has no <{PropertyName}> property.";
            return false;
        }

        // A property that referred to another property would arrive here
        // unevaluated, and fetching a header for a literal '$(Something)' would
        // fail with something far less obvious than this.
        if (value.Contains('$'))
        {
            error =
                $"<{PropertyName}> is '{value}', which is not a literal version. " +
                "Pass --version explicitly.";

            return false;
        }

        version = value;
        return true;
    }
}
