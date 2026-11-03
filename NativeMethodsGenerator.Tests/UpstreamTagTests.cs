using NativeMethodsGenerator;

namespace NativeMethodsGenerator.Tests;

/// <summary>
/// Mapping the pinned three-part version to the tag upstream published it
/// under, which for 11.12.0 was <c>v11.12</c>.
/// </summary>
public class UpstreamTagTests
{
    [Fact]
    public void FullVersion_IsTheOnlyCandidate()
        => Assert.Equal(["v11.8.1"], UpstreamTag.Candidates("11.8.1"));

    [Fact]
    public void DotZeroPatch_FallsBackToTheTwoPartTag()
        => Assert.Equal(["v11.12.0", "v11.12"], UpstreamTag.Candidates("11.12.0"));

    [Fact]
    public void TwoPartVersion_IsNotShortenedFurther()
        => Assert.Equal(["v11.12"], UpstreamTag.Candidates("11.12"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BlankVersion_IsRejected(string version)
        => Assert.ThrowsAny<ArgumentException>(() => UpstreamTag.Candidates(version));
}
