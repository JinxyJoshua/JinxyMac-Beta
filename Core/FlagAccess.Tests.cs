using System;
using System.IO;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Saying why Roblox's settings could not be written.
/// </summary>
/// <remarks>
/// This exists because the old answer was a bare false, shown to the user as
/// "close Roblox and try again" whatever the cause — including the common one on
/// a Mac, where macOS refuses to let one app change another and closing Roblox
/// changes nothing. The point of these is that each cause reaches the user as
/// its own instruction.
/// </remarks>
public class FlagAccessTests
{
    // ---- reading an exception as a cause ----

    /// <summary>
    /// macOS answers a blocked bundle write with EPERM, which .NET raises as
    /// this. It is the whole reason the type exists.
    /// </summary>
    [Fact]
    public void ARefusedWriteReadsAsBlocked()
    {
        FlagAccessReport report = FlagAccessReport.From(
            new UnauthorizedAccessException("Operation not permitted"), "/some/path");

        Assert.Equal(FlagAccess.Blocked, report.State);
        Assert.False(report.CanWrite);
    }

    [Fact]
    public void AHeldFileReadsAsLocked()
    {
        Assert.Equal(FlagAccess.Locked,
            FlagAccessReport.From(new IOException("in use"), "/some/path").State);
    }

    [Fact]
    public void AMissingFolderReadsAsNoRoblox()
    {
        Assert.Equal(FlagAccess.NoRoblox,
            FlagAccessReport.From(new DirectoryNotFoundException(), "/some/path").State);
    }

    /// <summary>
    /// Anything unexpected is carried verbatim rather than guessed at, so an
    /// unfamiliar failure arrives in a report instead of vanishing.
    /// </summary>
    [Fact]
    public void AnythingElseIsUnknownAndKeepsItsMessage()
    {
        FlagAccessReport report = FlagAccessReport.From(
            new InvalidOperationException("something odd"), "/some/path");

        Assert.Equal(FlagAccess.Unknown, report.State);
        Assert.Contains("something odd", report.Detail);
    }

    /// <summary>
    /// DirectoryNotFoundException derives from IOException, so the order these
    /// are tested in decides the answer. Pinned, because getting it the other
    /// way round would report a missing Roblox as a running one.
    /// </summary>
    [Fact]
    public void AMissingFolderIsNotMistakenForALockedFile()
    {
        Assert.IsAssignableFrom<IOException>(new DirectoryNotFoundException());

        Assert.NotEqual(FlagAccess.Locked,
            FlagAccessReport.From(new DirectoryNotFoundException(), null).State);
    }

    // ---- what the user is told ----

    /// <summary>Every cause says something, and no two causes say the same thing.</summary>
    [Fact]
    public void EachCauseExplainsItselfDifferently()
    {
        var said = new System.Collections.Generic.HashSet<string>();

        foreach (FlagAccess state in Enum.GetValues<FlagAccess>())
        {
            string explain = new FlagAccessReport(state).Explain;

            Assert.False(string.IsNullOrWhiteSpace(explain), state.ToString());
            Assert.True(said.Add(explain), $"{state} repeats another cause's wording");
        }
    }

    /// <summary>
    /// The one that was being got wrong: a blocked write must not tell people to
    /// close Roblox, because that is not the problem and does not fix it.
    /// </summary>
    [Fact]
    public void ABlockedWriteDoesNotBlameRoblox()
    {
        FlagAccessReport report = new(FlagAccess.Blocked, "/path");

        Assert.DoesNotContain("Quit Roblox", report.Explain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("close Roblox", report.Explain, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("macOS", report.Explain);
    }

    [Fact]
    public void ALockedFileIsTheOneThatAsksForRobloxToBeQuit()
    {
        Assert.Contains("Quit Roblox", new FlagAccessReport(FlagAccess.Locked).Explain);
    }

    /// <summary>A cause the user can act on comes with the action.</summary>
    [Fact]
    public void OnlyABlockedWriteOffersAFix()
    {
        Assert.NotNull(new FlagAccessReport(FlagAccess.Blocked, "/path").Fix);

        foreach (FlagAccess state in Enum.GetValues<FlagAccess>())
            if (state != FlagAccess.Blocked)
                Assert.Null(new FlagAccessReport(state, "/path").Fix);
    }

    // ---- the Terminal fallback ----

    /// <summary>
    /// Offered because macOS does not always list an unsigned app under App
    /// Management at all, and then the switch the notice asks for does not
    /// exist. It has to name the real folder, or it would not work.
    /// </summary>
    [Fact]
    public void TheTerminalFallbackNamesTheFolderItIsFixing()
    {
        const string path = "/Applications/Roblox.app/Contents/MacOS/ClientSettings";

        string? command = new FlagAccessReport(FlagAccess.Blocked, path).TerminalFix;

        Assert.NotNull(command);
        Assert.Contains(path, command);
        Assert.Contains("mkdir -p", command);
        Assert.Contains("chown", command);
    }

    /// <summary>
    /// Quoted, because the path can hold a space — somebody's Roblox living in
    /// a folder with one would otherwise get a command that does the wrong
    /// thing, or something worse.
    /// </summary>
    [Fact]
    public void TheTerminalFallbackQuotesThePath()
    {
        string? command = new FlagAccessReport(
            FlagAccess.Blocked, "/Users/me/My Apps/Roblox.app/Contents/MacOS/ClientSettings")
            .TerminalFix;

        Assert.NotNull(command);
        Assert.Contains("\"/Users/me/My Apps/Roblox.app/Contents/MacOS/ClientSettings\"", command);
    }

    [Fact]
    public void WithNoPathThereIsNoCommandToOffer()
    {
        Assert.Null(new FlagAccessReport(FlagAccess.Blocked).TerminalFix);
    }

    [Fact]
    public void OnlyABlockedWriteOffersACommand()
    {
        foreach (FlagAccess state in Enum.GetValues<FlagAccess>())
            if (state != FlagAccess.Blocked)
                Assert.Null(new FlagAccessReport(state, "/path").TerminalFix);
    }

    // ---- the diagnostics line ----

    [Fact]
    public void TheDiagnosticsLineCarriesTheRealErrorWhenThereIsOne()
    {
        var report = new FlagAccessReport(FlagAccess.Blocked, "/path", "Operation not permitted");

        Assert.Contains("Blocked", report.Line);
        Assert.Contains("Operation not permitted", report.Line);
    }

    [Fact]
    public void TheDiagnosticsLineIsStillReadableWithNoError()
    {
        Assert.Equal("Roblox settings: Ready", new FlagAccessReport(FlagAccess.Ready).Line);
    }

    // ---- probing a real folder ----

    [Fact]
    public void ProbingAWritableInstallSaysReady()
    {
        using var fake = new FakeRoblox();

        FlagAccessReport report = new RobloxFastFlags(new[] { fake.Root }).Probe();

        Assert.Equal(FlagAccess.Ready, report.State);
        Assert.True(report.CanWrite);
    }

    [Fact]
    public void ProbingWithNoRobloxSaysSo()
    {
        using var empty = new FakeRoblox(withRoblox: false);

        Assert.Equal(FlagAccess.NoRoblox,
            new RobloxFastFlags(new[] { empty.Root }).Probe().State);
    }

    /// <summary>The probe must not leave its test file behind in the bundle.</summary>
    [Fact]
    public void ProbingLeavesNoFileBehind()
    {
        using var fake = new FakeRoblox();

        var flags = new RobloxFastFlags(new[] { fake.Root });
        flags.Probe();

        string folder = flags.FindClientSettingsFolder()!;

        Assert.Empty(Directory.GetFiles(folder));
    }

    /// <summary>
    /// A successful probe clears an earlier failure, so a page refreshed after
    /// the permission is granted stops showing the old complaint.
    /// </summary>
    [Fact]
    public void AGoodProbeForgetsTheLastFailure()
    {
        using var empty = new FakeRoblox(withRoblox: false);
        using var fake = new FakeRoblox();

        var missing = new RobloxFastFlags(new[] { empty.Root });
        missing.Probe();
        Assert.NotNull(missing.LastFailure);

        var working = new RobloxFastFlags(new[] { fake.Root });
        working.Probe();
        Assert.Null(working.LastFailure);
    }

    /// <summary>A failed write records why, rather than only that it failed.</summary>
    [Fact]
    public void AFailedApplyRecordsTheReason()
    {
        using var empty = new FakeRoblox(withRoblox: false);

        var flags = new RobloxFastFlags(new[] { empty.Root });

        Assert.False(flags.Apply(RobloxFastFlags.FpsBoost));
        Assert.Equal(FlagAccess.NoRoblox, flags.LastFailure!.State);
    }

    [Fact]
    public void ASuccessfulApplyForgetsAnEarlierReason()
    {
        using var fake = new FakeRoblox();

        var flags = new RobloxFastFlags(new[] { fake.Root });

        Assert.True(flags.Apply(RobloxFastFlags.FpsBoost));
        Assert.Null(flags.LastFailure);
    }

    /// <summary>A folder with a Roblox.app in it, or without one.</summary>
    private sealed class FakeRoblox : IDisposable
    {
        public FakeRoblox(bool withRoblox = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "jinxy-flags-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Root);

            if (withRoblox)
                Directory.CreateDirectory(
                    Path.Combine(Root, "Roblox.app", "Contents", "MacOS"));
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A temp folder left behind is harmless.
            }
        }
    }
}
