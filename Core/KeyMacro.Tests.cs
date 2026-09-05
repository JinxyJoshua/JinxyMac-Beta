using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Macros, and the parsing that turns what someone typed into one.
/// </summary>
/// <remarks>
/// The sending itself is a SendInput call and cannot be tested without a
/// desktop to send into. Everything deciding *what* gets sent is arithmetic and
/// text, and that is where a macro goes wrong — a bad interval that pins a core,
/// or a key list that silently parses to nothing.
/// </remarks>
public class KeyMacroTests
{
    [Fact]
    public void KeepsTheKeysInTheOrderGiven()
    {
        var macro = new KeyMacro("Switcher", new[] { 0x31, 0x32 }, "1, 2", 500);

        Assert.Equal(new[] { 0x31, 0x32 }, macro.Keys);
    }

    /// <summary>
    /// A zero interval is a key that never comes up and a core pinned sending
    /// it. The clamp is what stops a typo becoming a hang.
    /// </summary>
    [Theory]
    [InlineData(0, KeyMacro.MinIntervalMs)]
    [InlineData(-50, KeyMacro.MinIntervalMs)]
    [InlineData(999999, KeyMacro.MaxIntervalMs)]
    [InlineData(250, 250)]
    public void ClampsTheInterval(int given, int expected)
    {
        Assert.Equal(expected, new KeyMacro("M", new[] { 0x52 }, "R", given).IntervalMs);
    }

    /// <summary>
    /// 0 is kept, not dropped: it is the A key on macOS, and the sentinel for
    /// "no key" lives at -1 now (HotkeyBinding.Unbound), not 0. Only a
    /// negative code or one at/above 256 is out of range.
    /// </summary>
    [Fact]
    public void DropsKeyCodesOutsideTheValidRange()
    {
        var macro = new KeyMacro("M", new[] { 0, 0x52, 300, -1 }, "R", 100);

        Assert.Equal(new[] { 0, 0x52 }, macro.Keys);
    }

    /// <summary>
    /// A is a real, bindable key: KeyCodes.For('A') is 0 on macOS, and a
    /// macro's own Keys filter (>= 0 and &lt; 256) admits it like any other
    /// letter — this is the limitation the sentinel move exists to remove.
    /// </summary>
    [Fact]
    public void AMacroCanBeBuiltWithAAsOneOfItsKeys()
    {
        int? a = KeyCodes.For('A');
        Assert.NotNull(a);

        var macro = new KeyMacro("Spam A", new[] { a!.Value }, "A", 100);

        Assert.Equal(new[] { a.Value }, macro.Keys);
        Assert.True(macro.IsUsable);
    }

    [Fact]
    public void IsNotUsableWithoutKeysOrAName()
    {
        Assert.False(new KeyMacro("M", Array.Empty<int>(), "", 100).IsUsable);
        Assert.False(new KeyMacro("   ", new[] { 0x52 }, "R", 100).IsUsable);
        Assert.True(new KeyMacro("M", new[] { 0x52 }, "R", 100).IsUsable);
    }

    [Fact]
    public void SaysWhenItCycles()
    {
        Assert.Contains("cycles", new KeyMacro("S", new[] { 0x31, 0x32 }, "1, 2", 500).SummaryText);
        Assert.DoesNotContain("cycles", new KeyMacro("R", new[] { 0x52 }, "R", 120).SummaryText);
    }

    [Theory]
    [InlineData(120, "every 120 ms")]
    [InlineData(1000, "every 1s")]
    [InlineData(1500, "every 1.5s")]
    public void ReadsTheRateAtAHumanScale(int interval, string expected)
    {
        Assert.Equal(expected, new KeyMacro("M", new[] { 0x52 }, "R", interval).RateText);
    }

    // ---- parsing what the user typed ----

    [Fact]
    public void ReadsASingleKey()
    {
        (int[] keys, string text) = MacroStore.ParseKeys("R")!.Value;

        // Not (int)'R': that's the Windows virtual-key code by coincidence
        // and the wrong assertion everywhere else. KeyCodes.For is what
        // ParseKeys is actually supposed to produce.
        Assert.Equal(new[] { KeyCodes.For('R')!.Value }, keys);
        Assert.Equal("R", text);
    }

    /// <summary>Both separators, because people type both.</summary>
    [Theory]
    [InlineData("1, 2")]
    [InlineData("1 2")]
    [InlineData("1,2")]
    public void ReadsACycleHoweverItIsSeparated(string typed)
    {
        (int[] keys, _) = MacroStore.ParseKeys(typed)!.Value;

        Assert.Equal(new[] { KeyCodes.For('1')!.Value, KeyCodes.For('2')!.Value }, keys);
    }

    [Fact]
    public void UppercasesSoTheCodesAreConsistent()
    {
        (int[] lower, _) = MacroStore.ParseKeys("r")!.Value;
        (int[] upper, _) = MacroStore.ParseKeys("R")!.Value;

        Assert.Equal(upper, lower);
    }

    /// <summary>
    /// Refused rather than silently dropped. A macro that parsed "Shift" to
    /// nothing would save, sit in the list, and do nothing at all.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Shift")]
    [InlineData("F5")]
    [InlineData("1, Shift")]
    [InlineData("!")]
    public void RefusesWhatItCannotSend(string? typed)
    {
        Assert.Null(MacroStore.ParseKeys(typed));
    }

    [Theory]
    [InlineData("100", 100)]
    [InlineData(" 250 ", 250)]
    public void ReadsAnInterval(string typed, int expected)
    {
        Assert.Equal(expected, MacroStore.ParseInterval(typed));
    }

    [Theory]
    [InlineData("4")]
    [InlineData("99999")]
    [InlineData("fast")]
    [InlineData("")]
    public void RefusesAnIntervalOutsideTheRange(string typed)
    {
        Assert.Null(MacroStore.ParseInterval(typed));
    }

    // ---- the list ----

    [Fact]
    public void UpsertReplacesByNameRatherThanAdding()
    {
        var macros = new List<KeyMacro> { new("Spam R", new[] { 0x52 }, "R", 120) };

        MacroStore.Upsert(macros, new KeyMacro("spam r", new[] { 0x54 }, "T", 300));

        KeyMacro only = Assert.Single(macros);
        Assert.Equal(300, only.IntervalMs);
    }

    [Fact]
    public void UpsertKeepsThePositionOfWhatItReplaces()
    {
        var macros = new List<KeyMacro>
        {
            new("One", new[] { 0x31 }, "1", 100),
            new("Two", new[] { 0x32 }, "2", 100),
            new("Three", new[] { 0x33 }, "3", 100)
        };

        MacroStore.Upsert(macros, new KeyMacro("Two", new[] { 0x39 }, "9", 400));

        Assert.Equal("Two", macros[1].Name);
        Assert.Equal(400, macros[1].IntervalMs);
    }

    [Fact]
    public void FindMatchesByNameCaseInsensitively()
    {
        var macros = new List<KeyMacro> { new("Spam R", new[] { 0x52 }, "R", 120) };

        Assert.Same(macros[0], MacroStore.Find(macros, "spam r"));
    }

    [Fact]
    public void FindReturnsNullWhenNoMacroHasThatName()
    {
        var macros = new List<KeyMacro> { new("Spam R", new[] { 0x52 }, "R", 120) };

        Assert.Null(MacroStore.Find(macros, "Spam T"));
    }

    // ---- carrying a hotkey across a same-named save ----
    //
    // SaveMacro's own form defaults its pending hotkey to Unbound. Saving
    // under a name that already exists must never let that default silently
    // erase a working hotkey — see ResolveSaveHotkey's own remarks.

    [Fact]
    public void ResolveSaveHotkeyPassesThePendingPickThroughWhenNothingExistsUnderThatNameYet()
    {
        (HotkeyBinding hotkey, string? notice) =
            MacroStore.ResolveSaveHotkey(existing: null, pending: HotkeyBinding.Unbound);

        Assert.Equal(HotkeyBinding.Unbound, hotkey);
        Assert.Null(notice);
    }

    [Fact]
    public void ResolveSaveHotkeyCarriesOverTheExistingHotkeyWhenTheFormLeftItsSlotEmpty()
    {
        var existing = new KeyMacro("Spam", new[] { 0x52 }, "R", 120, hotkey: new HotkeyBinding(99, "F7"));

        (HotkeyBinding hotkey, string? notice) =
            MacroStore.ResolveSaveHotkey(existing, pending: HotkeyBinding.Unbound);

        Assert.Equal(99, hotkey.Code);
        Assert.Equal("F7", hotkey.Name);
        Assert.Contains("F7", notice);
        Assert.Contains("Spam", notice);
    }

    [Fact]
    public void ResolveSaveHotkeyKeepsTheFormsOwnPickWhenItSetOne()
    {
        var existing = new KeyMacro("Spam", new[] { 0x52 }, "R", 120, hotkey: new HotkeyBinding(99, "F7"));
        var pending = new HotkeyBinding(50, "F5");

        (HotkeyBinding hotkey, string? notice) = MacroStore.ResolveSaveHotkey(existing, pending);

        Assert.Equal(pending, hotkey);
        Assert.Contains("F5", notice);
    }

    [Fact]
    public void ResolveSaveHotkeyStillNoticesAPlainReplaceWithNoHotkeyInvolved()
    {
        var existing = new KeyMacro("Spam", new[] { 0x52 }, "R", 120);

        (HotkeyBinding hotkey, string? notice) =
            MacroStore.ResolveSaveHotkey(existing, pending: HotkeyBinding.Unbound);

        Assert.Equal(HotkeyBinding.Unbound, hotkey);
        Assert.NotNull(notice);
        Assert.Contains("Spam", notice);
    }

    // ---- who owns a hotkey code ----
    //
    // Symmetric with the fixed hotkeys' own clash check: a macro's hotkey
    // must be found the same way whichever direction is asking, or a fixed
    // hotkey can be rebound onto a key a macro already owns and permanently
    // shadow it (Fire() tries the fixed hotkeys first).

    [Fact]
    public void FindByHotkeyCodeReturnsTheMacroThatOwnsIt()
    {
        var macro = new KeyMacro("Spam", new[] { 0x52 }, "R", 120, hotkey: new HotkeyBinding(99, "F7"));
        var macros = new List<KeyMacro> { macro };

        Assert.Same(macro, MacroStore.FindByHotkeyCode(macros, 99));
    }

    [Fact]
    public void FindByHotkeyCodeReturnsNullWhenNoMacroOwnsIt()
    {
        var macros = new List<KeyMacro> { new("Spam", new[] { 0x52 }, "R", 120, hotkey: new HotkeyBinding(99, "F7")) };

        Assert.Null(MacroStore.FindByHotkeyCode(macros, 50));
    }

    [Fact]
    public void FindByHotkeyCodeSkipsTheExcludedMacro()
    {
        var macro = new KeyMacro("Spam", new[] { 0x52 }, "R", 120, hotkey: new HotkeyBinding(99, "F7"));
        var macros = new List<KeyMacro> { macro };

        Assert.Null(MacroStore.FindByHotkeyCode(macros, 99, excluding: macro));
    }

    /// <summary>
    /// A disabled macro still owns its key here — the same rule
    /// <c>HotkeyHolder</c> in <c>MainWindow.Macros.cs</c> already applies.
    /// Letting a disabled macro's key go to something else would collide the
    /// moment it is re-enabled.
    /// </summary>
    [Fact]
    public void FindByHotkeyCodeMatchesADisabledMacroToo()
    {
        var macro = new KeyMacro("Spam", new[] { 0x52 }, "R", 120,
            hotkey: new HotkeyBinding(99, "F7"), enabled: false);
        var macros = new List<KeyMacro> { macro };

        Assert.Same(macro, MacroStore.FindByHotkeyCode(macros, 99));
    }

    // ---- a toggle that would trap itself ----
    //
    // Fire() (MainWindow.axaml.cs) refuses any code a running macro's own
    // RunningKeys() reports before it ever checks whose toggle that code is.
    // A macro whose toggle is one of its own keys is caught by that same
    // guard the moment it starts: the press that should stop it never gets
    // as far as MacroWithHotkey. TrapsOwnToggle is the check that refuses the
    // binding up front instead — see its remarks for why Fire() itself is not
    // the place to special-case this.

    [Fact]
    public void TrapsOwnToggleRefusesAHotkeyMatchingOneOfTheMacrosOwnKeys()
    {
        var hotkey = new HotkeyBinding(KeyCodes.For('R')!.Value, "R");

        Assert.True(MacroStore.TrapsOwnToggle(new[] { KeyCodes.For('R')!.Value }, hotkey));
    }

    /// <summary>Not this trap — a code another macro sends is an ordinary clash, not a self-trap.</summary>
    [Fact]
    public void TrapsOwnToggleAllowsAHotkeyMatchingADifferentMacrosKeys()
    {
        var hotkey = new HotkeyBinding(KeyCodes.For('R')!.Value, "R");

        Assert.False(MacroStore.TrapsOwnToggle(new[] { KeyCodes.For('T')!.Value }, hotkey));
    }

    [Fact]
    public void TrapsOwnToggleAllowsAnUnboundToggle()
    {
        Assert.False(MacroStore.TrapsOwnToggle(new[] { KeyCodes.For('R')!.Value }, HotkeyBinding.Unbound));
    }

    /// <summary>
    /// The edit-direction route: the toggle was bound to R first (SaveMacro
    /// carries it over via ResolveSaveHotkey when the form's own slot is
    /// left empty), and only afterwards is the macro's Keys box edited to
    /// include R. Same trap, same check — SaveMacro runs it against the
    /// freshly parsed keys and the hotkey ResolveSaveHotkey settled on, not
    /// only at bind time.
    /// </summary>
    [Fact]
    public void TrapsOwnToggleCatchesKeysEditedToIncludeAnAlreadyBoundToggle()
    {
        var existing = new KeyMacro("Spam", new[] { KeyCodes.For('Q')!.Value }, "Q", 120,
            hotkey: new HotkeyBinding(KeyCodes.For('R')!.Value, "R"));

        (HotkeyBinding hotkey, _) = MacroStore.ResolveSaveHotkey(existing, pending: HotkeyBinding.Unbound);
        (int[] editedKeys, _) = MacroStore.ParseKeys("R")!.Value;

        Assert.True(MacroStore.TrapsOwnToggle(editedKeys, hotkey));
    }

    /// <summary>
    /// Nothing ships. An example macro reads as a feature of the app rather
    /// than something the user made, and the first instinct is to delete it.
    /// </summary>
    [Fact]
    public void ShipsWithNoMacrosAtAll()
    {
        Assert.Empty(MacroStore.Defaults());
    }

    /// <summary>
    /// The switcher lived in this list before it became its own page. Anyone
    /// who ran that build has it saved, and without this it appears on both
    /// pages at once.
    /// </summary>
    [Fact]
    public void DoesNotOfferTheSwitcherAsAMacro()
    {
        Assert.DoesNotContain(MacroStore.Defaults(),
            m => m.Name.Contains("Switcher", StringComparison.OrdinalIgnoreCase));
    }

    // ---- the platform guard ----
    //
    // MacroStore.Load() reads a fixed path (SettingsPath.For("macros.json")),
    // so these write to that real file directly, the same way
    // Wallpaper.Tests.cs exercises Store/Clear against the real settings
    // folder — and restore whatever was there before in a finally block.

    private static string MacrosFilePath => SettingsPath.For("macros.json");

    [Fact]
    public void AMacosFileLoadsItsMacros()
    {
        WithMacrosFile("""{"platform":"macos","macros":[{"Name":"Spam R","Keys":[82],"KeysText":"R","IntervalMs":120}]}""", () =>
        {
            KeyMacro only = Assert.Single(MacroStore.Load());
            Assert.Equal("Spam R", only.Name);
        });
    }

    /// <summary>
    /// A file written by the Windows build uses the same 0-255 key filter to
    /// mean virtual-key codes, not CGKeyCodes. Loading it here would press
    /// entirely different keys, so it loads as no macros rather than as an
    /// error or a partial load.
    /// </summary>
    [Fact]
    public void AWindowsFileLoadsEmpty()
    {
        WithMacrosFile("""{"platform":"windows","macros":[{"Name":"Spam R","Keys":[82],"KeysText":"R","IntervalMs":120}]}""", () =>
        {
            Assert.Empty(MacroStore.Load());
        });
    }

    /// <summary>A file with no platform member is exactly as foreign as one from Windows.</summary>
    [Fact]
    public void AFileWithNoPlatformMemberLoadsEmpty()
    {
        WithMacrosFile("""{"macros":[{"Name":"Spam R","Keys":[82],"KeysText":"R","IntervalMs":120}]}""", () =>
        {
            Assert.Empty(MacroStore.Load());
        });
    }

    // ---- schema migration: 0 used to mean unbound, now means A ----
    //
    // Every macros.json already on disk was written before HotkeyCode had a
    // schema version to go with it, so it deserializes with that version at
    // its default, 0 — below MacroStore's current schema. Below that, a
    // stored HotkeyCode of 0 must still load as unbound, the only meaning it
    // ever had; at or above it, 0 is A. This is the same danger the task
    // brief calls out for AppSettings, applied to the macro list instead:
    // skipping the migration would read every macro nobody bound a toggle to
    // as one silently bound to A.

    /// <summary>A stored HotkeyCode of 0 below the current schema is unbound, not A.</summary>
    [Fact]
    public void APreSchemaMacroWithHotkeyCodeZeroLoadsUnbound()
    {
        WithMacrosFile(
            """{"platform":"macos","macros":[{"Name":"Spam R","Keys":[82],"KeysText":"R","IntervalMs":120,"HotkeyCode":0,"HotkeyName":"Not set"}]}""",
            () =>
            {
                KeyMacro only = Assert.Single(MacroStore.Load());
                Assert.False(only.Hotkey.IsValid);
                Assert.Equal(HotkeyBinding.Unbound, only.Hotkey);
            });
    }

    /// <summary>
    /// After that same load, the file on disk must hold -1 (not 0) and the
    /// current schema — the migration is not just an in-memory reinterpretation,
    /// it rewrites what is actually stored.
    /// </summary>
    [Fact]
    public void MigratingAMacrosFileRewritesHotkeyCodeToMinusOneAndStampsTheSchema()
    {
        WithMacrosFile(
            """{"platform":"macos","macros":[{"Name":"Spam R","Keys":[82],"KeysText":"R","IntervalMs":120,"HotkeyCode":0,"HotkeyName":"Not set"}]}""",
            () =>
            {
                MacroStore.Load();

                string raw = File.ReadAllText(MacrosFilePath);
                using var doc = System.Text.Json.JsonDocument.Parse(raw);

                Assert.Equal(-1, doc.RootElement.GetProperty("macros")[0].GetProperty("HotkeyCode").GetInt32());
                Assert.True(doc.RootElement.GetProperty("schemaVersion").GetInt32() >= 1);
            });
    }

    /// <summary>A file already at the current schema reads its own HotkeyCode of 0 as A.</summary>
    [Fact]
    public void AMacrosFileAlreadyAtTheCurrentSchemaWithHotkeyCodeZeroLoadsAsA()
    {
        WithMacrosFile(
            """{"platform":"macos","schemaVersion":1,"macros":[{"Name":"Spam A","Keys":[0],"KeysText":"A","IntervalMs":120,"HotkeyCode":0,"HotkeyName":"A"}]}""",
            () =>
            {
                KeyMacro only = Assert.Single(MacroStore.Load());
                Assert.True(only.Hotkey.IsValid);
                Assert.Equal(0, only.Hotkey.Code);
                Assert.Equal("A", only.Hotkey.Name);
            });
    }

    /// <summary>
    /// Loading a pre-schema file twice must not migrate twice (which would
    /// be harmless here, but is the general shape of the bug this guards) and
    /// must not disturb a real, already-bound hotkey along the way.
    /// </summary>
    [Fact]
    public void LoadingAMacrosFileTwiceDoesNotDoubleMigrateOrLoseARealBinding()
    {
        WithMacrosFile(
            """{"platform":"macos","macros":[{"Name":"Spam R","Keys":[82],"KeysText":"R","IntervalMs":120,"HotkeyCode":15,"HotkeyName":"R"}]}""",
            () =>
            {
                KeyMacro first = Assert.Single(MacroStore.Load());
                Assert.Equal(15, first.Hotkey.Code);

                KeyMacro second = Assert.Single(MacroStore.Load());
                Assert.Equal(15, second.Hotkey.Code);

                string raw = File.ReadAllText(MacrosFilePath);
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
            });
    }

    // ---- the write half ----
    //
    // Load() is covered above: a macos file loads, a windows file loads
    // empty, a file with no platform member loads empty. Save() sets the
    // platform field but nothing asserted it — a wrong or missing tag would
    // produce a file that fails its own Load(), and every macro would
    // silently vanish on the next launch with no error at all.

    /// <summary>
    /// Save() then Load() carries a fully-populated macro back unchanged,
    /// <c>Enabled: false</c> included — a disabled macro must come back
    /// disabled, or a macro someone switched off would switch itself back on
    /// across a restart.
    /// </summary>
    /// <remarks>
    /// The macro under test is built with <c>HoldsMs</c>, <c>ClicksWanted</c>
    /// and <c>EquipMs</c> populated too, but nothing here asserts on them
    /// surviving: <see cref="MacroStore"/>'s <c>StoredMacro</c> has no fields
    /// for them at all (confirmed against the current source — this is not
    /// a mutant-testing gap, it's pre-existing in the ported Windows source
    /// too), so they are silently dropped by <c>Save()</c> today. That's a
    /// real, separate bug from the platform-tag one this task is about, and
    /// fixing it means changing <c>Core/KeyMacro.cs</c>, which is out of
    /// scope here. Populating them anyway proves a macro that uses these
    /// features doesn't crash the round trip; asserting on them would just
    /// make this test fail for a reason unrelated to what it's checking.
    /// </remarks>
    [Fact]
    public void SaveThenLoadRoundTripsAFullyPopulatedMacro()
    {
        WithMacrosFile(null, () =>
        {
            var macro = new KeyMacro(
                "Crossbow Switch",
                new[] { 0x31, 0x32 },
                "1, 2",
                intervalMs: 350,
                holdsMs: new[] { 1200, 180 },
                clicksWanted: 3,
                equipMs: 90,
                hotkey: new HotkeyBinding(42, "F13"),
                enabled: false);

            MacroStore.Save(new List<KeyMacro> { macro });

            KeyMacro loaded = Assert.Single(MacroStore.Load());

            Assert.Equal("Crossbow Switch", loaded.Name);
            Assert.Equal(new[] { 0x31, 0x32 }, loaded.Keys);
            Assert.Equal("1, 2", loaded.KeysText);
            Assert.Equal(350, loaded.IntervalMs);
            Assert.Equal(42, loaded.Hotkey.Code);
            Assert.Equal("F13", loaded.Hotkey.Name);
            Assert.False(loaded.Enabled);
        });
    }

    /// <summary>
    /// Asserted on the raw file text rather than on Load()'s behaviour — the
    /// point is to catch the tag being wrong independently of the reader, so
    /// a broken writer and a broken reader can't hide behind each other.
    /// </summary>
    [Fact]
    public void SaveWritesThePlatformTag()
    {
        WithMacrosFile(null, () =>
        {
            MacroStore.Save(new List<KeyMacro> { new("R", new[] { 0x52 }, "R", 120) });

            string raw = File.ReadAllText(MacrosFilePath);
            using var doc = System.Text.Json.JsonDocument.Parse(raw);

            Assert.Equal("macos", doc.RootElement.GetProperty("platform").GetString());
        });
    }

    /// <summary>
    /// Proves the writer and the reader agree on the same field name: a file
    /// this build wrote, then tampered to claim a different platform, loads
    /// exactly as empty as a file that platform actually wrote.
    /// </summary>
    [Fact]
    public void SaveThenLoadEmptiesOutIfThePlatformTagIsTamperedWith()
    {
        WithMacrosFile(null, () =>
        {
            MacroStore.Save(new List<KeyMacro> { new("R", new[] { 0x52 }, "R", 120) });

            string raw = File.ReadAllText(MacrosFilePath);
            string tampered = raw.Replace("\"macos\"", "\"windows\"");
            Assert.NotEqual(raw, tampered); // sanity: the replace actually matched
            File.WriteAllText(MacrosFilePath, tampered);

            Assert.Empty(MacroStore.Load());
        });
    }

    /// <summary>
    /// Backs up whatever is at the real macros file and restores it
    /// afterwards, same as every other test in this section. A null
    /// <paramref name="json"/> skips the initial write instead of writing
    /// literally "null" — for the Save()-side tests below, which want the
    /// real file left exactly as it was until Save() itself writes it.
    /// </summary>
    private static void WithMacrosFile(string? json, Action assertion)
    {
        string path = MacrosFilePath;
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
