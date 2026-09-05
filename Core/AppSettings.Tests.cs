using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The settings file's schema migration — the point of moving
/// <see cref="HotkeyBinding.Unbound"/>'s sentinel to -1. On macOS 0 is the A
/// key, and every settings.json already on disk uses 0 in its six hotkey-code
/// fields to mean unbound; <see cref="AppSettings.SchemaVersion"/> is what
/// tells those two meanings apart on the way in.
/// </summary>
public class AppSettingsTests
{
    private static string SettingsFilePath => SettingsPath.For("settings.json");

    /// <summary>
    /// Backs up whatever is at the real settings file and restores it
    /// afterwards — the same isolation pattern <c>KeyMacro.Tests.cs</c> uses
    /// for macros.json, since <see cref="AppSettings.Load"/> reads a fixed
    /// path rather than one this test can point elsewhere.
    /// </summary>
    private static void WithSettingsFile(string? json, Action assertion)
    {
        string path = SettingsFilePath;
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

    /// <summary>
    /// A settings file with no SchemaVersion member deserializes with it at
    /// the default, 0 — below CurrentSchema, so a stored HotkeyCode of 0
    /// must still read as unbound, the only meaning 0 ever had before this
    /// feature.
    /// </summary>
    [Fact]
    public void APreSchemaSettingsFileWithHotkeyCodeZeroLoadsUnbound()
    {
        WithSettingsFile(
            """{"HotkeyCode":0,"ComboCode":0,"BuildCode":0,"RecordCode":0,"ReplayCode":0,"SwitcherHotkeyCode":0}""",
            () =>
            {
                AppSettings loaded = AppSettings.Load();

                Assert.Equal(-1, loaded.HotkeyCode);
                Assert.False(new HotkeyBinding(loaded.HotkeyCode, loaded.HotkeyName).IsValid);
            });
    }

    /// <summary>
    /// After that same load, the file on disk must hold -1 in every one of
    /// the six fields (not 0) and CurrentSchema — the migration rewrites what
    /// is actually stored, not just what this run interpreted in memory.
    /// </summary>
    [Fact]
    public void MigratingASettingsFileRewritesEveryHotkeyFieldAndStampsTheSchema()
    {
        WithSettingsFile(
            """{"HotkeyCode":0,"ComboCode":0,"BuildCode":0,"RecordCode":0,"ReplayCode":0,"SwitcherHotkeyCode":0}""",
            () =>
            {
                AppSettings.Load();

                string raw = File.ReadAllText(SettingsFilePath);
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;

                Assert.Equal(-1, root.GetProperty("HotkeyCode").GetInt32());
                Assert.Equal(-1, root.GetProperty("ComboCode").GetInt32());
                Assert.Equal(-1, root.GetProperty("BuildCode").GetInt32());
                Assert.Equal(-1, root.GetProperty("RecordCode").GetInt32());
                Assert.Equal(-1, root.GetProperty("ReplayCode").GetInt32());
                Assert.Equal(-1, root.GetProperty("SwitcherHotkeyCode").GetInt32());
                Assert.Equal(AppSettings.CurrentSchema, root.GetProperty("SchemaVersion").GetInt32());
            });
    }

    /// <summary>A settings file already at CurrentSchema reads its own HotkeyCode of 0 as A.</summary>
    [Fact]
    public void ASettingsFileAlreadyAtTheCurrentSchemaWithHotkeyCodeZeroLoadsAsA()
    {
        WithSettingsFile(
            $$"""{"SchemaVersion":{{AppSettings.CurrentSchema}},"HotkeyCode":0,"HotkeyName":"A"}""",
            () =>
            {
                AppSettings loaded = AppSettings.Load();

                Assert.Equal(0, loaded.HotkeyCode);
                Assert.True(new HotkeyBinding(loaded.HotkeyCode, loaded.HotkeyName).IsValid);
            });
    }

    /// <summary>
    /// Loading a pre-schema file twice must not re-run the migration (the
    /// file is already at CurrentSchema after the first load) and must not
    /// disturb a real, already-bound hotkey along the way — only a stored 0
    /// is rewritten, never a genuine code.
    /// </summary>
    [Fact]
    public void LoadingASettingsFileTwiceDoesNotDoubleMigrateOrLoseARealBinding()
    {
        WithSettingsFile(
            """{"HotkeyCode":15,"HotkeyName":"R","ComboCode":0,"BuildCode":0,"RecordCode":0,"ReplayCode":0,"SwitcherHotkeyCode":0}""",
            () =>
            {
                AppSettings first = AppSettings.Load();
                Assert.Equal(15, first.HotkeyCode);

                AppSettings second = AppSettings.Load();
                Assert.Equal(15, second.HotkeyCode);

                string raw = File.ReadAllText(SettingsFilePath);
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                Assert.Equal(AppSettings.CurrentSchema, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
            });
    }
}
