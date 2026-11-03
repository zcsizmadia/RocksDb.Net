namespace NativeMethodsGenerator;

/// <summary>
/// Maps the pinned RocksDb version to the git tags it may be published under.
/// </summary>
/// <remarks>
/// The pinned version is always three parts, because it is the first three
/// parts of the package version, and RocksDb.Net.Runtimes pins the same
/// three-part number. Upstream does not always tag that way: 11.12.0 was tagged
/// <c>v11.12</c>, so fetching <c>v11.12.0</c> gets a 404. The runtimes build
/// pads a two-part tag to <c>.0</c>, and this is the inverse of that.
/// <para>
/// The exact tag is tried first, so every version tagged in full keeps
/// resolving to the tag it always did. The short form is only a fallback, and
/// only for a <c>.0</c> patch, which is the only case where the two can name
/// the same release.
/// </para>
/// </remarks>
public static class UpstreamTag
{
    /// <summary>
    /// The tags to try for <paramref name="version"/>, most specific first.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var tags = new List<string> { "v" + version };

        string[] parts = version.Split('.');
        if (parts.Length == 3 && parts[2] == "0")
        {
            tags.Add($"v{parts[0]}.{parts[1]}");
        }

        return tags;
    }
}
