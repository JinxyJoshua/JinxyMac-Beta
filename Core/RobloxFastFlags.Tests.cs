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

    // ---- the frame rate, which is the point of the feature ----

    [Fact]
    public void SettingTheFrameRateWritesTheTargetRobloxReads()
    {
        var flags = Flags();

        Assert.True(flags.SetFpsCap(240));

        Assert.Equal(240, flags.CurrentFpsCap());
        Assert.Equal("240", flags.Read()[RobloxFastFlags.FpsCapFlagName]);
    }

    /// <summary>
    /// 60 and below is the client's own behaviour, so it is stored as nothing at
    /// all. Writing "60" would leave a setting behind that looks deliberate and
    /// outlives the reason for it.
    /// </summary>
    [Theory]
    [InlineData(60)]
    [InlineData(30)]
    [InlineData(null)]
    public void TheDefaultFrameRateIsStoredAsNoSettingAtAll(int? fps)
    {
        var flags = Flags();
        flags.SetFpsCap(240);

        flags.SetFpsCap(fps);

        Assert.Null(flags.CurrentFpsCap());
        Assert.False(flags.Read().ContainsKey(RobloxFastFlags.FpsCapFlagName));
    }

    [Fact]
    public void ChangingTheFrameRateReplacesItRatherThanAddingToIt()
    {
        var flags = Flags();

        flags.SetFpsCap(120);
        flags.SetFpsCap(RobloxFastFlags.UnlimitedFps);

        Assert.Equal(RobloxFastFlags.UnlimitedFps, flags.CurrentFpsCap());
    }

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
        flags.SetFpsCap(240);
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
        flags.SetFpsCap(144);

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

        flags.SetFpsCap(120);
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
