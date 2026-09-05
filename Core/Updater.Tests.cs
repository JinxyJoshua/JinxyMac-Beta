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

    // ---- pinning the release asset host ----

    /// <summary>
    /// What browser_download_url actually looks like in a real GitHub API
    /// reply, and the CDN host a real download of one redirects to.
    /// </summary>
    [Theory]
    [InlineData("https://github.com/JinxyJoshua/JinxyMac-Beta/releases/download/v1.2.2/JinxyMac.tar.gz")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1/abc")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1/abc")]
    public void TrustsGitHubsOwnHosts(string url)
    {
        Assert.True(Updater.IsTrustedAssetUrl(url));
    }

    /// <summary>
    /// The bug this exists to prevent: the address in a JSON reply is just a
    /// string until something checks it, and this is the one that gets
    /// downloaded and unpacked over the running app.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://github.com/JinxyJoshua/JinxyMac-Beta/releases/download/v1.2.2/JinxyMac.tar.gz")] // http, not https
    [InlineData("https://github.com.evil.example/JinxyJoshua/JinxyMac-Beta/x.tar.gz")] // host merely starts with github.com
    [InlineData("https://notgithubusercontent.com/x.tar.gz")] // suffix trick without the dot
    [InlineData("https://evil.example/x.tar.gz")]
    [InlineData("file:///etc/passwd")]
    public void RejectsEverythingElse(string? url)
    {
        Assert.False(Updater.IsTrustedAssetUrl(url));
    }

    // ---- quoting the swap script's own paths ----

    /// <summary>
    /// The mechanism the swap script leans on: single quotes stop everything a
    /// shell would otherwise do with the characters inside them, unlike the
    /// double quotes the script used before, which still let <c>$</c>,
    /// backticks and backslashes through.
    /// </summary>
    [Theory]
    [InlineData("/Applications/JinxyMac.app", "'/Applications/JinxyMac.app'")]
    [InlineData("$HOME/x", "'$HOME/x'")]
    [InlineData("`whoami`", "'`whoami`'")]
    [InlineData("a\\b", "'a\\b'")]
    [InlineData("say \"hi\"", "'say \"hi\"'")]
    public void QuotesOrdinaryAndHostileCharactersLiterally(string value, string expected)
    {
        Assert.Equal(expected, Updater.ShellQuote(value));
    }

    /// <summary>
    /// The one character single quotes cannot contain on their own — closed,
    /// escaped, reopened, the standard POSIX way.
    /// </summary>
    [Fact]
    public void EscapesAnEmbeddedSingleQuote()
    {
        Assert.Equal("'/Users/bob'\\''s Mac/JinxyMac.app'", Updater.ShellQuote("/Users/bob's Mac/JinxyMac.app"));
    }

    [Fact]
    public void QuotingAnEmptyPathStillProducesAValidToken()
    {
        Assert.Equal("''", Updater.ShellQuote(""));
    }
}
