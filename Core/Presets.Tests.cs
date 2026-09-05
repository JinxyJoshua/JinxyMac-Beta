using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The preset list and the rules for editing it.
/// </summary>
/// <remarks>
/// Pinned because the Windows build shipped a version of this where the
/// defaults had drifted from what the app actually used, and nobody noticed
/// until a preset silently reset settings nobody had chosen. A list of numbers
/// is exactly the thing that rots quietly.
///
/// Load and Save touch a real file in the user's application data and are left
/// alone here; everything worth checking is in the list operations, which take
/// their list as an argument.
/// </remarks>
public class PresetsTests
{
    [Fact]
    public void ShipsTwelveOfThem()
    {
        // Was eleven; the Measured preset added a twelfth. Existing users only
        // see it after Restore, because PresetStore persists the whole list and
        // deleted defaults are meant to stay deleted.
        Assert.Equal(12, PresetStore.Defaults().Count);
    }

    [Fact]
    public void NamesAreDistinct()
    {
        List<ClickPreset> presets = PresetStore.Defaults();

        Assert.Equal(presets.Count, presets.Select(p => p.Name).Distinct().Count());
    }

    /// <summary>
    /// Every name is capitalised, which was asked for explicitly and is the kind
    /// of thing that comes back the next time the list is edited.
    /// </summary>
    [Fact]
    public void NamesStartWithACapital()
    {
        Assert.All(PresetStore.Defaults(), p => Assert.True(char.IsUpper(p.Name[0]), p.Name));
    }

    [Fact]
    public void CarriesTheRequestedYoNoobLikeRates()
    {
        ClickPreset preset = PresetStore.Defaults().Single(p => p.Name == "YoNoobLike");

        Assert.Equal(85.86, preset.Cps);
        Assert.Equal(64.25, preset.Cdc);
    }

    /// <summary>
    /// True of every default except Measured, whose hold mode is part of the
    /// configuration it was measured from, not an oversight.
    /// </summary>
    [Fact]
    public void DefaultsAreToggleRatherThanHoldExceptMeasured()
    {
        Assert.All(
            PresetStore.Defaults().Where(p => p.Name != "Measured"),
            p => Assert.Equal("Toggle", p.ModeText));

        Assert.Equal("Hold", PresetStore.Defaults().Single(p => p.Name == "Measured").ModeText);
    }

    // ---- the bar ----

    /// <summary>
    /// The bar has to stay inside its track, or a fast preset draws over the
    /// card next to it.
    /// </summary>
    [Fact]
    public void BarNeverOutgrowsItsTrack()
    {
        Assert.All(PresetStore.Defaults(), p => Assert.InRange(p.BarWidth, 1, 134));

        Assert.InRange(new ClickPreset("Absurd", 1000, 50).BarWidth, 1, 134);
    }

    /// <summary>A preset at zero still shows something, or the card looks broken.</summary>
    [Fact]
    public void BarStaysVisibleAtZero()
    {
        Assert.True(new ClickPreset("Idle", 0, 50).BarWidth > 0);
    }

    // ---- editing ----

    [Fact]
    public void UpsertAddsSomethingNew()
    {
        var presets = new List<ClickPreset> { new("Sky", 52.62, 82.62) };

        PresetStore.Upsert(presets, new ClickPreset("Mine", 40, 60));

        Assert.Equal(2, presets.Count);
    }

    /// <summary>
    /// Saving over a name is how the pencil works. A second "Sky" would leave
    /// two cards that look identical and behave differently.
    /// </summary>
    [Fact]
    public void UpsertReplacesByName()
    {
        var presets = new List<ClickPreset> { new("Sky", 52.62, 82.62) };

        PresetStore.Upsert(presets, new ClickPreset("Sky", 99, 10));

        ClickPreset only = Assert.Single(presets);
        Assert.Equal(99, only.Cps);
    }

    [Fact]
    public void UpsertMatchesNamesRegardlessOfCase()
    {
        var presets = new List<ClickPreset> { new("Sky", 52.62, 82.62) };

        PresetStore.Upsert(presets, new ClickPreset("sky", 12, 34));

        Assert.Single(presets);
    }

    /// <summary>
    /// Order matters on the page — a replaced preset should stay where it was
    /// rather than jumping to the end of the grid.
    /// </summary>
    [Fact]
    public void UpsertKeepsThePositionOfWhatItReplaces()
    {
        var presets = new List<ClickPreset>
        {
            new("One", 10, 50),
            new("Two", 20, 50),
            new("Three", 30, 50)
        };

        PresetStore.Upsert(presets, new ClickPreset("Two", 99, 50));

        Assert.Equal("Two", presets[1].Name);
        Assert.Equal(99, presets[1].Cps);
    }

    // ---- what the user types ----

    [Theory]
    [InlineData("85.86", 85.86)]
    [InlineData("  40  ", 40)]
    [InlineData("0", 0)]
    public void ReadsARateThatIsOne(string typed, double expected)
    {
        Assert.Equal(expected, PresetStore.ParseRate(typed, 1000));
    }

    /// <summary>
    /// Refusing rather than coercing. A blank CPS read as zero would save a
    /// preset that silently stops the clicker.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("fast")]
    [InlineData("-5")]
    [InlineData("1001")]
    public void RefusesARateThatIsNot(string? typed)
    {
        Assert.Null(PresetStore.ParseRate(typed, 1000));
    }

    [Fact]
    public void RefusesADutyCycleOverAHundred()
    {
        Assert.Null(PresetStore.ParseRate("101", 100));
        Assert.Equal(100, PresetStore.ParseRate("100", 100));
    }

    // ---- reading and writing the real file ----
    //
    // Load()/Save() read and write a fixed path (SettingsPath.For
    // ("click_presets.json")), same as AppSettings and MacroStore — see
    // those Tests.cs files for the backup/restore isolation this reuses.

    private static string PresetsFilePath => SettingsPath.For("click_presets.json");

    /// <summary>
    /// A null element in the stored array — a hand-edited
    /// "[null,{...}]" — used to reach p.Name in Load()'s filter and throw,
    /// which sent the whole load to the catch and Defaults(), resurrecting
    /// every preset the user had deliberately deleted. This is the exact
    /// contract PresetStore's own class remarks promise: an empty (or here,
    /// partly-null) list on disk must not be regenerated from code.
    /// </summary>
    [Fact]
    public void ANullElementIsSkippedRatherThanResurrectingDeletedDefaults()
    {
        WithPresetsFile(
            """[null,{"Name":"Custom","Cps":10,"Cdc":50,"HoldMode":false}]""",
            () =>
            {
                ClickPreset only = Assert.Single(PresetStore.Load());
                Assert.Equal("Custom", only.Name);
            });
    }

    /// <summary>
    /// A zero-length file — what a kill mid-write used to leave behind
    /// before Save() went through SettingsPath.WriteAtomic — must load as
    /// the shipped defaults, the same as a missing file, rather than crash.
    /// </summary>
    [Fact]
    public void AZeroLengthFileLoadsAsDefaultsRatherThanCrashing()
    {
        WithPresetsFile("", () =>
        {
            Assert.Equal(PresetStore.Defaults().Count, PresetStore.Load().Count);
        });
    }

    /// <summary>
    /// Save() must leave the previous presets file exactly as it was when
    /// the write fails partway, the same contract pinned directly at the
    /// shared helper in SettingsPath.Tests.cs and exercised here through the
    /// real caller.
    /// </summary>
    [Fact]
    public void AFailedSaveLeavesThePreviousPresetsFileIntact()
    {
        WithPresetsFile(
            """[{"Name":"Keep Me","Cps":10,"Cdc":50,"HoldMode":false}]""",
            () =>
            {
                string temp = PresetsFilePath + ".tmp";
                Directory.CreateDirectory(temp);

                try
                {
                    PresetStore.Save(new List<ClickPreset> { new("Somebody Else", 1, 1) });

                    string raw = File.ReadAllText(PresetsFilePath);
                    Assert.Contains("Keep Me", raw);
                    Assert.DoesNotContain("Somebody Else", raw);
                }
                finally
                {
                    if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
                }
            });
    }

    /// <summary>
    /// Backs up whatever is at the real click_presets.json and restores it
    /// afterwards — the isolation pattern <c>AppSettings.Tests.cs</c> and
    /// <c>KeyMacro.Tests.cs</c> use for their own fixed-path files.
    /// </summary>
    private static void WithPresetsFile(string? json, Action assertion)
    {
        string path = PresetsFilePath;
        bool existed = File.Exists(path);
        string? original = existed ? File.ReadAllText(path) : null;

        try
        {
            if (json != null) File.WriteAllText(path, json);
            assertion();
        }
        finally
        {
            if (existed) File.WriteAllText(path, original!);
            else if (File.Exists(path)) File.Delete(path);
        }
    }
}
