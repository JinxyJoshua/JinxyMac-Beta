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

    [Fact]
    public void DropsKeyCodesOutsideTheValidRange()
    {
        var macro = new KeyMacro("M", new[] { 0, 0x52, 300, -1 }, "R", 100);

        Assert.Equal(new[] { 0x52 }, macro.Keys);
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
