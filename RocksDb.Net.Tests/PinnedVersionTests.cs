using System.Reflection;
using RocksDbNet.Native;

namespace RocksDbNet.Tests;

/// <summary>
/// The generated files were regenerated after the pinned version last changed.
/// </summary>
/// <remarks>
/// <para>
/// Bumping <c>RocksDbVersion</c> in <c>Directory.Build.props</c> changes which
/// native library the package binds to. If the generated files are not
/// regenerated with it, the P/Invoke signatures describe one library while
/// another is loaded, and the <see cref="Ticker"/> and <see cref="Histogram"/>
/// values name counters by position in a list that has moved. Neither shows up
/// as a build failure, and both are the kind of thing that goes wrong quietly
/// and much later.
/// </para>
/// <para>
/// So it fails here instead. The generator stamps the version it read into each
/// file it writes, the build passes the pinned property into this assembly, and
/// these two compare them.
/// </para>
/// </remarks>
public class PinnedVersionTests
{
    private const string HowToFix =
        "Regenerate the bindings and commit the result:" +
        "\n\n    dotnet run --project NativeMethodsGenerator\n\n" +
        "Run it from the repository root. It reads RocksDbVersion from " +
        "Directory.Build.props and writes both generated files.";

    /// <summary>
    /// The version the build is pinned to, as the build itself evaluated it.
    /// </summary>
    /// <remarks>
    /// Passed in as assembly metadata by the test project rather than read out
    /// of <c>Directory.Build.props</c> here, so this compares against the value
    /// the build actually used and needs no idea where the repository root is.
    /// </remarks>
    private static string PinnedVersion
    {
        get
        {
            string? pinned = typeof(PinnedVersionTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "RocksDbVersion")
                ?.Value;

            Assert.False(
                string.IsNullOrWhiteSpace(pinned),
                "The test assembly carries no RocksDbVersion metadata, so the generated files " +
                "cannot be checked against the pinned version. The test project sets this from " +
                "the RocksDbVersion property; see its AssemblyMetadata item.");

            return pinned!;
        }
    }

    [Fact]
    public void Bindings_WereGeneratedForThePinnedVersion()
    {
        Assert.True(
            NativeMethods.RocksDbVersion == PinnedVersion,
            $"NativeMethods.g.cs was generated for RocksDb {NativeMethods.RocksDbVersion}, " +
            $"but RocksDbVersion is {PinnedVersion}.\n\n" +
            "Every P/Invoke signature in it describes the older library, while the newer one " +
            $"is what gets loaded.\n\n{HowToFix}");
    }

    [Fact]
    public void StatisticsEnums_WereGeneratedForThePinnedVersion()
    {
        Assert.True(
            StatisticsEnumsVersion.RocksDbVersion == PinnedVersion,
            $"StatisticsEnums.g.cs was generated for RocksDb {StatisticsEnumsVersion.RocksDbVersion}, " +
            $"but RocksDbVersion is {PinnedVersion}.\n\n" +
            "Ticker and Histogram values are positional, so a counter added or retired between " +
            "those two versions makes every member after it name the wrong thing. Nothing throws; " +
            $"the numbers are just quietly about something else.\n\n{HowToFix}");
    }

    /// <summary>
    /// Both generated files come from one run of the generator, so they cannot
    /// disagree with each other either.
    /// </summary>
    /// <remarks>
    /// Redundant while both tests above pass, and worth having anyway: it is the
    /// one that still fails if the pinned version is somehow unavailable, which
    /// would make the other two vacuous.
    /// </remarks>
    [Fact]
    public void BothGeneratedFiles_AgreeWithEachOther()
    {
        Assert.True(
            NativeMethods.RocksDbVersion == StatisticsEnumsVersion.RocksDbVersion,
            $"NativeMethods.g.cs was generated for RocksDb {NativeMethods.RocksDbVersion} but " +
            $"StatisticsEnums.g.cs for {StatisticsEnumsVersion.RocksDbVersion}. They are written " +
            $"by one command and cannot legitimately differ.\n\n{HowToFix}");
    }
}
