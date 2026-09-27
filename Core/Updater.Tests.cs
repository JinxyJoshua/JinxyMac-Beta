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
    // The hop every installed copy actually makes: 1.0.8 was the last release
    // for a long stretch, so this is the comparison the updater has to get
    // right in the field, not just in principle.
    [InlineData("1.2.2", "1.0.8")]
    public void NoticesANewerBuild(string candidate, string current)
    {
        Assert.True(Updater.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("1.0.2", "1.0.2")]
    [InlineData("1.0.1", "1.0.2")]
    [InlineData("0.9.9", "1.0.0")]
    [InlineData("1.0.8", "1.2.2")]
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

    /// <summary>
    /// The launch check must observe the caller's token, not only its own
    /// eight-second deadline. An already-cancelled token proves the linked
    /// source is actually wired through to the request: without it the call
    /// would go to the network and this would take a round trip instead of
    /// returning at once.
    /// </summary>
    [Fact]
    public async Task AnAlreadyCancelledTokenEndsTheCheckWithoutThrowing()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        Available? found = await Updater.CheckAsync(cancelled.Token);
        clock.Stop();

        // Null, never an exception: a check that cannot run is not an error
        // worth surfacing, and the caller is a fire-and-forget launch task.
        Assert.Null(found);

        // Five seconds, not two: this failed once on a machine that was busy
        // building, and a test that cries wolf under load teaches people to
        // ignore it. Well under the eight-second check timeout either way, so
        // it still proves the request was never made.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
    }

    // ---- picking the tarball for this Mac ----

    /// <summary>
    /// Since 1.2.3 a release carries two tarballs, one per architecture,
    /// because the single bundle that used to hold both could not be granted
    /// Accessibility at all. Picking the wrong one is not fatal — an Intel
    /// build runs on Apple silicon under Rosetta — but it is a slower app and
    /// a second copy of the runtime, so it is worth getting right.
    /// </summary>
    [Theory]
    [InlineData("JinxyMac-mac.tar.gz", false)]
    [InlineData("JinxyMac-mac_intel.tar.gz", true)]
    [InlineData("JinxyMac-mac-INTEL.tar.gz", true)]
    [InlineData("JinxyMac-mac_INTEL.tar.gz", true)]
    public void TellsTheIntelTarballFromTheAppleSiliconOne(string asset, bool isIntel)
    {
        bool x64 = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            == System.Runtime.InteropServices.Architecture.X64;

        // On an x64 process the Intel asset is the match; on anything else the
        // plain one is. Asserted against the running architecture rather than a
        // hardcoded answer, so this test means the same thing on the PC it was
        // written on and on the Mac it ships to.
        Assert.Equal(x64 == isIntel, Updater.IsForThisMac(asset));
    }

    /// <summary>
    /// The name every 1.0.8 and 1.2.2 install already looks for. It has to stay
    /// the Apple silicon build's name: those clients take the first tarball
    /// they see and have no idea there is a choice.
    /// </summary>
    [Fact]
    public void TheAppleSiliconAssetKeepsThePlainName()
    {
        Assert.False("JinxyMac-mac.tar.gz".Contains("intel", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// GitHub lists a release's assets alphabetically and every 1.0.8 and
    /// 1.2.2 client takes the first tarball it finds, so the Apple silicon
    /// build has to sort first or those users are handed an Intel app. This
    /// is the bug that shipped for twenty minutes: named with a hyphen, the
    /// Intel asset sorted first, because '-' comes before '.'.
    /// </summary>
    [Fact]
    public void TheAppleSiliconAssetSortsBeforeTheIntelOne()
    {
        Assert.True(
            string.CompareOrdinal("JinxyMac-mac.tar.gz", "JinxyMac-mac_intel.tar.gz") < 0,
            "the Apple silicon tarball must sort first for older clients");

        // What it must not go back to.
        Assert.True(
            string.CompareOrdinal("JinxyMac-mac.tar.gz", "JinxyMac-mac-intel.tar.gz") > 0,
            "a hyphen would put the Intel build first again");
    }

    /// <summary>
    /// Since 1.2.6 there is a build for Macs too old for the ordinary one
    /// (below macOS 12). A modern Mac must never be offered it: updating would
    /// quietly move it onto an older runtime.
    /// </summary>
    [Theory]
    [InlineData("JinxyMac-mac_older.tar.gz")]
    [InlineData("JinxyMac-mac_older_intel.tar.gz")]
    public void AModernMacIsNeverOfferedTheOlderMacOsBuild(string asset)
    {
        if (Updater.NeedsOlderMacBuild) return;

        Assert.False(Updater.IsForThisMac(asset));
    }

    /// <summary>
    /// And the reverse, which is the one that would strand someone: a Mac below
    /// macOS 12 must not be offered the ordinary build, because macOS refuses
    /// to open it and the update would leave them with an app that will not
    /// start.
    /// </summary>
    [Theory]
    [InlineData("JinxyMac-mac.tar.gz")]
    [InlineData("JinxyMac-mac_intel.tar.gz")]
    public void AnOldMacIsNeverOfferedTheOrdinaryBuild(string asset)
    {
        if (!Updater.NeedsOlderMacBuild) return;

        Assert.False(Updater.IsForThisMac(asset));
    }

    /// <summary>
    /// Every combination of Mac and asset, which is what decides whether an
    /// update helps someone or strands them. Each Mac has exactly one right
    /// answer out of the four downloads.
    /// </summary>
    [Theory]
    // A modern Apple silicon Mac.
    [InlineData("JinxyMac-mac.tar.gz", false, false, true)]
    [InlineData("JinxyMac-mac_intel.tar.gz", false, false, false)]
    [InlineData("JinxyMac-mac_older.tar.gz", false, false, false)]
    [InlineData("JinxyMac-mac_older_intel.tar.gz", false, false, false)]
    // A modern Intel Mac.
    [InlineData("JinxyMac-mac.tar.gz", false, true, false)]
    [InlineData("JinxyMac-mac_intel.tar.gz", false, true, true)]
    [InlineData("JinxyMac-mac_older.tar.gz", false, true, false)]
    [InlineData("JinxyMac-mac_older_intel.tar.gz", false, true, false)]
    // An Intel Mac on Big Sur, the one this was built for.
    [InlineData("JinxyMac-mac.tar.gz", true, true, false)]
    [InlineData("JinxyMac-mac_intel.tar.gz", true, true, false)]
    [InlineData("JinxyMac-mac_older.tar.gz", true, true, false)]
    [InlineData("JinxyMac-mac_older_intel.tar.gz", true, true, true)]
    // An Apple silicon Mac still on Big Sur, which is where an M1 shipped.
    [InlineData("JinxyMac-mac.tar.gz", true, false, false)]
    [InlineData("JinxyMac-mac_intel.tar.gz", true, false, false)]
    [InlineData("JinxyMac-mac_older.tar.gz", true, false, true)]
    [InlineData("JinxyMac-mac_older_intel.tar.gz", true, false, false)]
    public void EachMacHasExactlyOneRightDownload(string asset, bool oldMac, bool x64, bool expected)
    {
        Assert.Equal(expected, Updater.Matches(asset, oldMac, x64));
    }

    /// <summary>
    /// Asset order still has to put the ordinary Apple silicon build first, for
    /// the 1.0.8 and 1.2.2 clients that take whichever tarball they see first.
    /// </summary>
    [Fact]
    public void TheOrdinaryAppleSiliconBuildStillSortsFirst()
    {
        string[] assets =
        {
            "JinxyMac-mac.tar.gz",
            "JinxyMac-mac_intel.tar.gz",
            "JinxyMac-mac_older.tar.gz",
            "JinxyMac-mac_older_intel.tar.gz"
        };

        string[] sorted = (string[])assets.Clone();
        Array.Sort(sorted, StringComparer.Ordinal);

        Assert.Equal("JinxyMac-mac.tar.gz", sorted[0]);
    }
}
