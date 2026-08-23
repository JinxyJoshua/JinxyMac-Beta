using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Deciding whether a release is newer than the running build.
/// </summary>
/// <remarks>
/// The only part of the updater that can be tested from here — the rest
/// downloads a file and replaces a running .app — and the part most likely to
/// be quietly wrong, because comparing versions looks like comparing strings
/// right up until the tenth release.
/// </remarks>
public class UpdaterTests
{
    [Theory]
    [InlineData("1.0.3", "1.0.2")]
    [InlineData("1.1.0", "1.0.9")]
    [InlineData("2.0.0", "1.9.9")]
    public void NoticesANewerBuild(string candidate, string current)
    {
        Assert.True(Updater.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("1.0.2", "1.0.2")]
    [InlineData("1.0.1", "1.0.2")]
    [InlineData("0.9.9", "1.0.0")]
    public void LeavesTheSameOrOlderAlone(string candidate, string current)
    {
        Assert.False(Updater.IsNewer(candidate, current));
    }

    /// <summary>
    /// The bug this exists to prevent. As text "1.0.10" sorts before "1.0.9",
    /// so a string comparison stops offering updates at the tenth build and
    /// never says why.
    /// </summary>
    [Fact]
    public void TenIsNewerThanNine()
    {
        Assert.True(Updater.IsNewer("1.0.10", "1.0.9"));
        Assert.False(Updater.IsNewer("1.0.9", "1.0.10"));
    }

    [Fact]
    public void ShorterVersionsCountMissingPartsAsZero()
    {
        Assert.False(Updater.IsNewer("1.0", "1.0.0"));
        Assert.True(Updater.IsNewer("1.1", "1.0.9"));
    }

    /// <summary>
    /// Tags arrive as "v1.0.2-beta" and are trimmed before they get here, but a
    /// stray suffix must degrade to a number rather than throw on someone's
    /// machine mid-launch.
    /// </summary>
    [Theory]
    [InlineData("1.0.3rc1", "1.0.2", true)]
    [InlineData("", "1.0.2", false)]
    [InlineData("nonsense", "1.0.2", false)]
    public void SurvivesATagItDoesNotUnderstand(string candidate, string current, bool newer)
    {
        Assert.Equal(newer, Updater.IsNewer(candidate, current));
    }

    /// <summary>
    /// The version in code is what build-mac.sh stamps into Info.plist, so it
    /// has to stay in the shape that script greps for.
    /// </summary>
    [Fact]
    public void VersionIsPlainDottedNumbers()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", Updater.Version);
    }

    [Fact]
    public void SizeReadsInMegabytes()
    {
        var update = new Available("1.0.3", "notes", "https://example.invalid/x.tar.gz", 90_925_413);

        Assert.Equal("87 MB", update.SizeText);
    }
}
