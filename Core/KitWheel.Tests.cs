using System;
using System.Linq;
using System.Collections.Generic;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The kit list, and the arithmetic that decides where the wheel stops.
/// </summary>
/// <remarks>
/// The spin itself is an animation and needs a window. What is worth testing is
/// that the wheel does not lie: the segment it stops on has to be the kit that
/// was chosen. A wheel that announces one kit and points at another is worse
/// than one that does not spin at all.
/// </remarks>
public class KitWheelTests
{
    // ---- the list ----

    [Theory]
    [InlineData("Melody", "Melody")]
    [InlineData("  Melody  ", "Melody")]
    [InlineData("Void  Reaver", "Void Reaver")]
    public void TidiesUpWhatWasTyped(string typed, string expected)
    {
        Assert.Equal(expected, KitWheel.CleanName(typed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RefusesANameThatIsNotOne(string? typed)
    {
        Assert.Null(KitWheel.CleanName(typed));
    }

    [Fact]
    public void TrimsANameTooLongToFitOnASlice()
    {
        string? name = KitWheel.CleanName(new string('x', 200));

        Assert.NotNull(name);
        Assert.True(name!.Length <= KitWheel.MaxNameLength);
    }

    /// <summary>
    /// The same kit twice is two slices for one kit, which quietly doubles its
    /// odds. Refused rather than allowed and deduplicated later.
    /// </summary>
    [Theory]
    [InlineData("Melody")]
    [InlineData("melody")]
    [InlineData("  MELODY ")]
    public void WillNotAddTheSameKitTwice(string second)
    {
        var kits = new List<string> { "Melody" };

        Assert.False(KitWheel.Add(kits, second));
        Assert.Single(kits);
    }

    [Fact]
    public void AddsAKitThatIsNotThereYet()
    {
        var kits = new List<string> { "Melody" };

        Assert.True(KitWheel.Add(kits, "Yuzi"));
        Assert.Equal(new[] { "Melody", "Yuzi" }, kits);
    }

    [Fact]
    public void StopsAtThePointSlicesBecomeUnreadable()
    {
        var kits = new List<string>();

        for (int i = 0; i < KitWheel.MaxKits + 5; i++) KitWheel.Add(kits, $"Kit {i}");

        Assert.Equal(KitWheel.MaxKits, kits.Count);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void NeedsTwoKitsBeforeThereIsAnythingToChoose(int count, bool expected)
    {
        var kits = new List<string>();
        for (int i = 0; i < count; i++) kits.Add($"Kit {i}");

        Assert.Equal(expected, KitWheel.CanSpin(kits));
    }

    // ---- rolling without replacement ----

    private static readonly string[] Three = { "Melody", "Yuzi", "Evelynn" };

    [Fact]
    public void EverythingIsStillToComeBeforeTheFirstRoll()
    {
        Assert.Equal(3, KitWheel.Remaining(Three, Array.Empty<string>()).Count);
    }

    /// <summary>
    /// The whole point of the run. A kit already rolled is out of the pool, or
    /// the remaining count means nothing and the run never ends.
    /// </summary>
    [Fact]
    public void ARolledKitIsOutOfThePool()
    {
        List<string> left = KitWheel.Remaining(Three, new[] { "Yuzi" });

        Assert.Equal(new[] { "Melody", "Evelynn" }, left);
    }

    [Fact]
    public void MatchesAnAlreadyRolledKitWhateverItsCase()
    {
        Assert.Equal(2, KitWheel.Remaining(Three, new[] { "yuzi" }).Count);
    }

    /// <summary>Never returns something already rolled, at any position.</summary>
    [Fact]
    public void NeverRollsTheSameKitTwice()
    {
        var used = new List<string>();

        for (int i = 0; i < Three.Length; i++)
        {
            string? rolled = KitWheel.Roll(Three, used, _ => 0);

            Assert.NotNull(rolled);
            Assert.DoesNotContain(rolled!, used);

            used.Add(rolled!);
        }

        Assert.Equal(3, used.Count);
    }

    [Fact]
    public void HasNothingLeftToRollWhenTheRunIsDone()
    {
        Assert.Null(KitWheel.Roll(Three, Three, _ => 0));
    }

    /// <summary>
    /// The picker is a random source. An index outside the pool must end a roll,
    /// not a run.
    /// </summary>
    [Theory]
    [InlineData(-5)]
    [InlineData(99)]
    public void SurvivesAPickerThatReturnsNonsense(int index)
    {
        Assert.NotNull(KitWheel.Roll(Three, Array.Empty<string>(), _ => index));
    }

    [Theory]
    [InlineData(13, 0, 0.0)]
    [InlineData(13, 1, 1.0 / 13.0)]
    [InlineData(13, 13, 1.0)]
    [InlineData(0, 0, 0.0)]
    public void ReportsHowFarThroughTheRunItIs(int total, int done, double expected)
    {
        Assert.Equal(expected, KitWheel.Progress(total, done), 6);
    }

    [Fact]
    public void ProgressCannotRunPastTheEnd()
    {
        Assert.Equal(1.0, KitWheel.Progress(5, 99), 6);
    }

    [Fact]
    public void KnowsWhenEveryKitHasBeenRolled()
    {
        Assert.False(KitWheel.IsComplete(Three, new[] { "Melody" }));
        Assert.True(KitWheel.IsComplete(Three, Three));
    }

    /// <summary>An empty list is not a finished run — there was nothing to do.</summary>
    [Fact]
    public void AnEmptyListIsNotACompletedRun()
    {
        Assert.False(KitWheel.IsComplete(Array.Empty<string>(), Array.Empty<string>()));
    }

    // ---- the starter roster ----

    /// <summary>
    /// A head start, not a source of truth. It only has to be usable: real
    /// names, no duplicates, and inside the cap.
    /// </summary>
    [Fact]
    public void TheStarterRosterIsUsableAsShipped()
    {
        List<string> roster = KitWheel.StarterRoster();

        Assert.True(roster.Count >= KitWheel.MinKits);
        Assert.True(roster.Count <= KitWheel.MaxKits);
        Assert.All(roster, k => Assert.Equal(k, KitWheel.CleanName(k)));
    }

    /// <summary>
    /// A duplicate would be one kit with two chances in the pool, and Add would
    /// silently drop it — leaving a roster shorter than the list that made it.
    /// </summary>
    [Fact]
    public void TheStarterRosterHasNoDuplicates()
    {
        List<string> roster = KitWheel.StarterRoster();
        var rebuilt = new List<string>();

        foreach (string kit in roster) KitWheel.Add(rebuilt, kit);

        Assert.Equal(roster.Count, rebuilt.Count);
    }

    /// <summary>
    /// The roster has to hold far more than a run does — the game has well over
    /// a hundred kits, and the wheel is whichever few were ticked.
    /// </summary>
    [Fact]
    public void TheRosterCapIsBiggerThanAnyRun()
    {
        Assert.True(KitWheel.MaxKits >= 100);
        Assert.True(KitWheel.StarterRoster().Count <= KitWheel.MaxKits);
    }

    /// <summary>
    /// Every name survives cleaning unchanged, including the awkward ones.
    /// </summary>
    /// <remarks>
    /// The roster carries an accent, an apostrophe and brackets because that is
    /// how the game spells them. A name that CleanName would rewrite — too long,
    /// or double-spaced — would be stored under one spelling and looked up under
    /// another, so its picture would never be found.
    /// </remarks>
    [Theory]
    [InlineData("Lucía")]
    [InlineData("Xu'rot")]
    [InlineData("Zeno (Wizard)")]
    [InlineData("Star Collector Stella")]
    public void KeepsTheGamesOwnSpellings(string kit)
    {
        Assert.Contains(kit, KitWheel.StarterRoster());
        Assert.Equal(kit, KitWheel.CleanName(kit));
    }

    /// <summary>
    /// "None" is the no-kit option on the game's screen, not a kit. Rolling it
    /// would mean playing without one.
    /// </summary>
    [Fact]
    public void DoesNotOfferTheNoKitOption()
    {
        Assert.DoesNotContain("None", KitWheel.StarterRoster());
    }

    /// <summary>Sorted, because a hundred kits in no order is unusable.</summary>
    [Fact]
    public void ShipsTheRosterInOrder()
    {
        List<string> roster = KitWheel.StarterRoster();

        Assert.Equal(roster.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(), roster);
    }

    // ---- saved wheels ----

    [Fact]
    public void SavesThePickedKitsUnderAName()
    {
        var presets = new List<KitPreset>();

        Assert.True(KitWheel.SavePreset(presets, "Nightmare", new[] { "Melody", "Yuzi" }));

        KitPreset only = Assert.Single(presets);
        Assert.Equal("Nightmare", only.Name);
        Assert.Equal(new[] { "Melody", "Yuzi" }, only.Kits);
    }

    /// <summary>
    /// Saving twice under one name updates it. Two entries with the same name
    /// would look identical on the page and load different wheels.
    /// </summary>
    [Theory]
    [InlineData("Nightmare")]
    [InlineData("nightmare")]
    public void SavingAgainReplacesRatherThanRepeats(string second)
    {
        var presets = new List<KitPreset>();

        KitWheel.SavePreset(presets, "Nightmare", new[] { "Melody" });
        KitWheel.SavePreset(presets, second, new[] { "Yuzi", "Evelynn" });

        KitPreset only = Assert.Single(presets);
        Assert.Equal(new[] { "Yuzi", "Evelynn" }, only.Kits);
    }

    /// <summary>An empty wheel is not worth a name — it would load as nothing.</summary>
    [Fact]
    public void WillNotSaveAWheelWithNoKitsOnIt()
    {
        var presets = new List<KitPreset>();

        Assert.False(KitWheel.SavePreset(presets, "Empty", Array.Empty<string>()));
        Assert.Empty(presets);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void WillNotSaveAWheelWithoutAName(string? name)
    {
        var presets = new List<KitPreset>();

        Assert.False(KitWheel.SavePreset(presets, name, new[] { "Melody" }));
        Assert.Empty(presets);
    }

    [Fact]
    public void StopsAtTheLimitButStillUpdatesExistingOnes()
    {
        var presets = new List<KitPreset>();

        for (int i = 0; i < KitWheel.MaxPresets; i++)
            KitWheel.SavePreset(presets, $"Wheel {i}", new[] { "Melody" });

        Assert.False(KitWheel.SavePreset(presets, "One More", new[] { "Yuzi" }));
        Assert.True(KitWheel.SavePreset(presets, "Wheel 0", new[] { "Yuzi" }));
        Assert.Equal(KitWheel.MaxPresets, presets.Count);
    }

    /// <summary>
    /// A snapshot, not a live view. If the saved list moved with the ticks, it
    /// would never differ from what is already on screen.
    /// </summary>
    [Fact]
    public void ASavedWheelDoesNotFollowLaterChanges()
    {
        var presets = new List<KitPreset>();
        var picked = new List<string> { "Melody" };

        KitWheel.SavePreset(presets, "Snapshot", picked);
        picked.Add("Yuzi");

        Assert.Equal(new[] { "Melody" }, presets[0].Kits);
    }

    /// <summary>
    /// The case that matters when a roster changes. A wheel saved before a kit
    /// was removed must not put that name back in the pool — nothing on the page
    /// could show it or take it out again.
    /// </summary>
    [Fact]
    public void LoadingSkipsKitsNoLongerOnTheRoster()
    {
        List<string> applied = KitWheel.ApplyPreset(
            roster: new[] { "Melody", "Yuzi" },
            saved: new[] { "Melody", "Retired Kit", "Yuzi" });

        Assert.Equal(new[] { "Melody", "Yuzi" }, applied);
    }

    [Fact]
    public void LoadingMatchesTheRosterWhateverTheCase()
    {
        Assert.Single(KitWheel.ApplyPreset(new[] { "Melody" }, new[] { "melody" }));
    }

    // ---- what a brand new install starts with ----

    /// <summary>
    /// Nothing ticked on a first run. The whole roster arrives, but the person
    /// chooses from it — a wheel pre-loaded with 113 kits would roll something
    /// they have never played on the very first spin.
    /// </summary>
    [Fact]
    public void AFirstRunHasNoKitsSelected()
    {
        KitRoster fresh = KitWheelStore.Fresh();

        Assert.Empty(fresh.Selected);
    }

    /// <summary>But the list itself is there to choose from.</summary>
    [Fact]
    public void AFirstRunStillOffersTheWholeRoster()
    {
        KitRoster fresh = KitWheelStore.Fresh();

        Assert.Equal(KitWheel.StarterRoster().Count, fresh.Kits.Count);
        Assert.Empty(fresh.Presets);
    }

    /// <summary>A stored roster with nothing ticked stays that way.</summary>
    [Fact]
    public void KeepsAnEmptySelectionWhenReadingItBack()
    {
        const string stored = """
        {"Kits":["Melody","Ember"],"Selected":[],"Presets":[]}
        """;

        Assert.Empty(KitWheelStore.Parse(stored).Selected);
    }

    /// <summary>
    /// The one exception, and it is for upgrades only: the original file was a
    /// bare list of names written before anything could be ticked, so every
    /// name in it meant "on the wheel".
    /// </summary>
    [Fact]
    public void TreatsTheOldFormatAsEverythingSelected()
    {
        KitRoster roster = KitWheelStore.Parse("""["Melody","Ember"]""");

        Assert.Equal(2, roster.Selected.Count);
    }

    /// <summary>Unreadable stored data must not tick everything by accident.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"Kits":["Melody"]}""")]
    public void NeverInventsASelection(string json)
    {
        Assert.Empty(KitWheelStore.Parse(json).Selected);
    }

    /// <summary>
    /// A hand-edited kit_wheel.json with <c>"Kits":null</c> deserializes
    /// KitRoster.Kits to null despite its own field initialiser — System.Text.Json
    /// overwrites an initialiser whenever the JSON carries an explicit null.
    /// Parse used to throw on that (a bare foreach over a null list), which sent
    /// Load's caller to Fresh() and replaced the roster, the ticks, AND every
    /// saved wheel with the starter defaults, made permanent on the next tile
    /// click. It must instead degrade to an empty roster — never crash, and
    /// never silently substitute the full starter list.
    /// </summary>
    [Fact]
    public void ANullKitsListParsesAsEmptyRatherThanCrashing()
    {
        KitRoster roster = KitWheelStore.Parse("""{"Kits":null,"Selected":["Zola"]}""");

        Assert.Empty(roster.Kits);
        Assert.NotEqual(KitWheel.StarterRoster().Count, roster.Kits.Count);
    }

    /// <summary>The same guard for the other two lists a hand-edited file can null out.</summary>
    [Fact]
    public void ANullSelectedOrPresetsListParsesAsEmptyRatherThanCrashing()
    {
        KitRoster roster = KitWheelStore.Parse(
            """{"Kits":["Melody"],"Selected":null,"Presets":null}""");

        Assert.Equal(new[] { "Melody" }, roster.Kits);
        Assert.Empty(roster.Selected);
        Assert.Empty(roster.Presets);
    }

    /// <summary>A null element inside "Presets" is skipped rather than crashing the whole load.</summary>
    [Fact]
    public void ANullPresetElementIsSkippedRatherThanCrashing()
    {
        KitRoster roster = KitWheelStore.Parse(
            """{"Kits":["Melody","Ember"],"Selected":[],"Presets":[null,{"Name":"Ok","Kits":["Melody"]}]}""");

        KitPreset only = Assert.Single(roster.Presets);
        Assert.Equal("Ok", only.Name);
    }

    // ---- reading and writing the real file ----
    //
    // KitWheelStore.Load/Save read and write a fixed path (SettingsPath.For
    // ("kit_wheel.json")), same as MacroStore and AppSettings — see those
    // Tests.cs files for the backup/restore isolation this reuses.

    private static string StoreFilePath => SettingsPath.For("kit_wheel.json");

    /// <summary>
    /// A zero-length file — exactly what a force-quit mid-write used to leave
    /// behind before writes went through SettingsPath.WriteAtomic — must load
    /// as the fresh-install defaults rather than throwing.
    /// </summary>
    [Fact]
    public void AZeroLengthFileLoadsAsFreshRatherThanCrashing()
    {
        WithStoreFile("", () =>
        {
            KitRoster loaded = KitWheelStore.Load();

            Assert.Equal(KitWheel.StarterRoster().Count, loaded.Kits.Count);
            Assert.Empty(loaded.Selected);
        });
    }

    /// <summary>
    /// Save() must leave the previous file exactly as it was when the write
    /// fails partway — the same contract SettingsPath.WriteAtomic.Tests.cs
    /// pins directly, exercised here through the real caller.
    /// </summary>
    [Fact]
    public void AFailedSaveLeavesThePreviousRosterFileIntact()
    {
        WithStoreFile("""{"Kits":["Melody"],"Selected":["Melody"],"Presets":[]}""", () =>
        {
            string temp = StoreFilePath + ".tmp";

            // Block the rename by putting a directory where the temp file
            // needs to land, so File.Move throws partway through Save.
            Directory.CreateDirectory(temp);

            try
            {
                KitWheelStore.Save(new KitRoster { Kits = new List<string> { "Somebody Else" } });

                string raw = File.ReadAllText(StoreFilePath);
                Assert.Contains("Melody", raw);
                Assert.DoesNotContain("Somebody Else", raw);
            }
            finally
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            }
        });
    }

    /// <summary>
    /// Backs up whatever is at the real kit_wheel.json and restores it
    /// afterwards — the isolation pattern <c>AppSettings.Tests.cs</c> and
    /// <c>KeyMacro.Tests.cs</c> use for their own fixed-path files.
    /// </summary>
    private static void WithStoreFile(string? json, Action assertion)
    {
        string path = StoreFilePath;
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

    // ---- searching the roster ----

    private static readonly List<string> Sample =
        new() { "Melody", "Axolotl Amy", "Ember", "Frost Reaper", "Amethyst" };

    [Fact]
    public void FindsAKitByTheStartOfItsName()
    {
        Assert.Equal(new[] { "Melody" }, KitWheel.Matching(Sample, "Mel"));
    }

    /// <summary>
    /// Contains, not starts-with. Half the roster is two words and somebody
    /// hunting for "Axolotl Amy" is as likely to type the second one.
    /// </summary>
    [Fact]
    public void FindsAKitByAWordInTheMiddleOfItsName()
    {
        Assert.Contains("Axolotl Amy", KitWheel.Matching(Sample, "Amy"));
    }

    [Theory]
    [InlineData("ember")]
    [InlineData("EMBER")]
    [InlineData("EmBeR")]
    public void DoesNotCareAboutCase(string term)
    {
        Assert.Contains("Ember", KitWheel.Matching(Sample, term));
    }

    /// <summary>Surrounding spaces come free with typing and must not matter.</summary>
    [Fact]
    public void IgnoresSpaceAroundWhatWasTyped()
    {
        Assert.Equal(new[] { "Melody" }, KitWheel.Matching(Sample, "  Melody  "));
    }

    /// <summary>
    /// An empty box is no filter rather than no matches — clearing the search
    /// has to put the whole roster back, not empty the list.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ShowsEverythingWhenNothingIsTyped(string? term)
    {
        Assert.Equal(Sample.Count, KitWheel.Matching(Sample, term).Count);
    }

    [Fact]
    public void FindsNothingWhenNothingMatches()
    {
        Assert.Empty(KitWheel.Matching(Sample, "zzz"));
    }

    /// <summary>One term can legitimately match several kits.</summary>
    [Fact]
    public void ReturnsEveryKitThatMatches()
    {
        List<string> hits = KitWheel.Matching(Sample, "me");

        Assert.Contains("Melody", hits);
        Assert.Contains("Amethyst", hits);
    }

    /// <summary>The order of the roster is kept, so the tiles do not reshuffle.</summary>
    [Fact]
    public void KeepsTheRosterOrder()
    {
        Assert.Equal(new[] { "Melody", "Amethyst" }, KitWheel.Matching(Sample, "me"));
    }

    /// <summary>A search on the real roster has to actually find something.</summary>
    [Fact]
    public void WorksAgainstTheShippedRoster()
    {
        Assert.NotEmpty(KitWheel.Matching(KitWheel.StarterRoster(), "a"));
    }
}
