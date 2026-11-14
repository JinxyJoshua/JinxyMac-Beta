using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Writing Roblox's own client settings, and putting them back.
/// </summary>
/// <remarks>
/// Tested against a fake Roblox.app in a temp folder. These write inside
/// somebody else's application bundle, so "leaves what it did not put there
/// alone" and "can always be undone" are the properties that matter most.
/// </remarks>
public class RobloxFastFlagsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "JinxyFlagTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>A Roblox.app, optionally with settings already in it.</summary>
    private RobloxFastFlags Flags(Dictionary<string, string>? existing = null)
    {
        string apps = Path.Combine(_root, "Applications");
        string settings = Path.Combine(apps, "Roblox.app", "Contents", "MacOS", "ClientSettings");
        Directory.CreateDirectory(settings);

        if (existing != null)
        {
            File.WriteAllText(Path.Combine(settings, "ClientAppSettings.json"),
                JsonSerializer.Serialize(existing));
        }

        return new RobloxFastFlags(new[] { apps });
    }

    private string SettingsFile =>
        Path.Combine(_root, "Applications", "Roblox.app", "Contents", "MacOS",
            "ClientSettings", "ClientAppSettings.json");

    // ---- finding Roblox ----

    [Fact]
    public void FindsTheClientSettingsFolderInsideTheBundle()
    {
        Assert.True(Flags().RobloxFound);
    }

    [Fact]
    public void SaysSoWhenRobloxIsNotInstalled()
    {
        string empty = Path.Combine(_root, "Nothing");
        Directory.CreateDirectory(empty);

        var flags = new RobloxFastFlags(new[] { empty });

        Assert.False(flags.RobloxFound);
        Assert.Null(flags.FindClientSettingsFolder());
        Assert.False(flags.Apply(RobloxFastFlags.FpsBoost));
    }

    // ---- the allowlist, which decides whether any of this does anything ----

    /// <summary>
    /// The guard that the frame-rate setting needed and did not have.
    /// </summary>
    /// <remarks>
    /// Roblox limited local settings to an approved list in September 2025 and
    /// ignores anything else <em>in silence</em> — no error, nothing in the file
    /// to see. So writing a flag and reading it back proves only that you wrote
    /// it, which is precisely how 1.2.8 shipped a frame-rate target that did
    /// nothing and reported success.
    ///
    /// <para>This fails the build instead. Anything offered on the Performance
    /// page has to be a flag Roblox will actually act on.</para>
    /// </remarks>
    [Fact]
    public void EverySettingTheAppWritesIsOneRobloxWillActOn()
    {
        var written = new List<string> { RobloxFastFlags.SkyGrayFlag };
        written.AddRange(RobloxFastFlags.FpsBoost.Select(f => f.Name));

        List<string> ignored = written
            .Where(name => !RobloxFastFlags.Allowlist.Contains(name))
            .ToList();

        Assert.True(ignored.Count == 0,
            "Roblox ignores these, so offering them would promise nothing: "
            + string.Join(", ", ignored));
    }

    /// <summary>
    /// And the other half of the same guard: the flags recorded as dead really
    /// are off the list. If Roblox ever approves one, this fails and says so,
    /// which is the signal to offer it again.
    /// </summary>
    [Fact]
    public void TheFlagsRecordedAsDeadAreTheOnesRobloxIgnores()
    {
        Assert.NotEmpty(RobloxFastFlags.DeadFlags);

        List<string> nowAllowed = RobloxFastFlags.DeadFlags
            .Where(RobloxFastFlags.Allowlist.Contains)
            .ToList();

        Assert.True(nowAllowed.Count == 0,
            "Roblox now allows these, so they could be offered again: "
            + string.Join(", ", nowAllowed));
    }

    /// <summary>The frame-rate target by name, so its removal cannot be undone quietly.</summary>
    [Fact]
    public void TheFrameRateTargetIsNotOffered()
    {
        Assert.Contains("DFIntTaskSchedulerTargetFps", RobloxFastFlags.DeadFlags);

        Assert.DoesNotContain("DFIntTaskSchedulerTargetFps",
            RobloxFastFlags.FpsBoost.Select(f => f.Name));
    }

    // ---- clearing up after the version that wrote it ----

    [Fact]
    public void AnOldFrameRateEntryIsNoticed()
    {
        var flags = Flags(OldVersionsFile());

        Assert.Equal(new[] { "DFIntTaskSchedulerTargetFps" }, flags.DeadFlagsPresent());
    }

    [Fact]
    public void AnOldFrameRateEntryCanBeTakenOut()
    {
        var flags = Flags(OldVersionsFile());

        Assert.True(flags.ClearDeadFlags());

        Assert.Empty(flags.DeadFlagsPresent());
        Assert.False(flags.Read().ContainsKey("DFIntTaskSchedulerTargetFps"));
    }

    /// <summary>
    /// Nothing to clear is not a failure, and must not be reported as having
    /// done something.
    /// </summary>
    [Fact]
    public void WithNoOldEntryThereIsNothingToTakeOut()
    {
        var flags = Flags();

        Assert.False(flags.ClearDeadFlags());
        Assert.Empty(flags.DeadFlagsPresent());
    }

    /// <summary>Taking it out leaves everything else in the file alone.</summary>
    [Fact]
    public void TakingOutTheOldEntryKeepsTheRest()
    {
        var file = OldVersionsFile();
        file["FFlagSomethingOfTheirs"] = "True";

        var flags = Flags(file);
        flags.ClearDeadFlags();

        Assert.Equal("True", flags.Read()["FFlagSomethingOfTheirs"]);
    }

    /// <summary>
    /// A dead entry still counts as something this app put there, so the page
    /// does not claim nothing is applied while one sits in the file.
    /// </summary>
    [Fact]
    public void AnOldFrameRateEntryCountsAsSomethingApplied()
    {
        Assert.True(Flags(OldVersionsFile()).AnythingApplied());
    }

    /// <summary>A settings file as version 1.2.8 would have left it.</summary>
    private static Dictionary<string, string> OldVersionsFile() =>
        new() { ["DFIntTaskSchedulerTargetFps"] = "9999" };

    // ---- living in someone else's file ----

    /// <summary>
    /// The one that matters most: this writes into a file a bootstrapper or the
    /// user may also be using. Anything already there has to survive.
    /// </summary>
    [Fact]
    public void KeepsSettingsItDidNotPutThere()
    {
        var flags = Flags(new Dictionary<string, string> { ["FFlagSomethingOfTheirs"] = "True" });

        flags.Apply(RobloxFastFlags.FpsBoost);

        Assert.Equal("True", flags.Read()["FFlagSomethingOfTheirs"]);
    }

    [Fact]
    public void RestoreGivesBackExactlyTheFileThatWasThereBefore()
    {
        var before = new Dictionary<string, string> { ["FFlagSomethingOfTheirs"] = "True" };
        var flags = Flags(before);

        flags.Apply(RobloxFastFlags.FpsBoost);

        // Written the way 1.2.8 wrote it, so Restore is shown to clear up after
        // that version too rather than leaving its dead entry behind.
        flags.Apply(new[] { new FastFlag("DFIntTaskSchedulerTargetFps", "9999", "") });

        Assert.True(flags.Restore());

        Dictionary<string, string> after = flags.Read();
        Assert.Equal("True", after["FFlagSomethingOfTheirs"]);
        Assert.Single(after);
        Assert.False(flags.AnythingApplied());
    }

    /// <summary>
    /// The backup is taken once, so switching settings several times still
    /// restores the client's own file rather than this app's earlier output.
    /// </summary>
    [Fact]
    public void BacksUpTheOriginalOnceRatherThanOnEveryApply()
    {
        var flags = Flags(new Dictionary<string, string> { ["FFlagTheirs"] = "Original" });

        flags.Apply(new[] { new FastFlag("FFlagTheirs", "Changed", "") });
        flags.Apply(new[] { new FastFlag("FFlagTheirs", "ChangedAgain", "") });
        flags.Restore();

        Assert.Equal("Original", flags.Read()["FFlagTheirs"]);
    }

    /// <summary>
    /// With no file of their own to restore, only the settings this app knows
    /// how to write are removed — deleting the whole file could throw away
    /// something set elsewhere.
    /// </summary>
    [Fact]
    public void WithNoBackupItRemovesOnlyItsOwnSettings()
    {
        var flags = Flags();

        flags.Apply(RobloxFastFlags.FpsBoost);

        // Something arriving after this app first wrote, so it is not in any backup.
        Dictionary<string, string> withTheirs = flags.Read();
        withTheirs["FFlagArrivedLater"] = "True";
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(withTheirs));
        File.Delete(Path.Combine(Path.GetDirectoryName(SettingsFile)!,
            "ClientAppSettings.jinxy-backup.json"));

        flags.Restore();

        Assert.Equal("True", flags.Read()["FFlagArrivedLater"]);
        Assert.False(flags.AnythingApplied());
    }

    [Fact]
    public void SurvivesASettingsFileThatIsNotValidJson()
    {
        var flags = Flags();
        File.WriteAllText(SettingsFile, "{ not json at all");

        Assert.Empty(flags.Read());
        Assert.True(flags.Apply(RobloxFastFlags.FpsBoost));
        Assert.True(flags.IsApplied(RobloxFastFlags.FpsBoost));
    }

    [Fact]
    public void KnowsWhetherTheBoostIsOn()
    {
        var flags = Flags();

        Assert.False(flags.IsApplied(RobloxFastFlags.FpsBoost));
        flags.Apply(RobloxFastFlags.FpsBoost);
        Assert.True(flags.IsApplied(RobloxFastFlags.FpsBoost));
    }

    [Fact]
    public void AnythingAppliedFollowsWhatIsActuallyInTheFile()
    {
        var flags = Flags();

        Assert.False(flags.AnythingApplied());

        flags.Apply(RobloxFastFlags.FpsBoost);
        Assert.True(flags.AnythingApplied());

        flags.Restore();
        Assert.False(flags.AnythingApplied());
    }

    // ---- what is offered at all ----

    /// <summary>
    /// Only Roblox's published allowlist, and nothing touching character
    /// physics: that is the category that gets accounts banned, and the reason
    /// the list is a fixed list rather than anything a user can type.
    /// </summary>
    [Fact]
    public void OffersNothingThatTouchesCharacterPhysics()
    {
        foreach (FastFlag flag in RobloxFastFlags.FpsBoost)
        {
            foreach (string banned in new[] { "Physics", "Humanoid", "Gravity", "Walk", "Jump", "Hipheight" })
            {
                Assert.DoesNotContain(banned, flag.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Every flag carries a reason in plain words, because the page shows them
    /// and a setting nobody can explain should not be offered.
    /// </summary>
    [Fact]
    public void EveryFlagSaysWhatItDoes()
    {
        foreach (FastFlag flag in RobloxFastFlags.FpsBoost)
        {
            Assert.False(string.IsNullOrWhiteSpace(flag.Why), flag.Name);
            Assert.False(string.IsNullOrWhiteSpace(flag.Value), flag.Name);
        }
    }

    /// <summary>
    /// The Windows build offers a graphics backend choice — Direct3D, Vulkan,
    /// OpenGL. None of those exist on macOS, and there is no Metal equivalent on
    /// the allowlist, so none should have come across in the port.
    /// </summary>
    [Fact]
    public void DoesNotOfferWindowsOnlyGraphicsBackends()
    {
        foreach (FastFlag flag in RobloxFastFlags.FpsBoost)
        {
            foreach (string windowsOnly in new[] { "D3D11", "Vulkan", "OpenGL" })
            {
                Assert.DoesNotContain(windowsOnly, flag.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
