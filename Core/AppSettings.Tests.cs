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
    /// Pins the exact regression this schema exists to prevent: a real A
    /// binding (HotkeyCode 0) already at CurrentSchema must survive a second
    /// load unchanged — both in memory and on disk. A future change that
    /// migrated by value instead of by version (rewriting any stored 0, not
    /// just ones below CurrentSchema) would turn this legitimate A binding
    /// back into "unbound" the second time the file is read, and nothing
    /// else in this suite checks a repeat load of an already-current file
    /// with code 0 specifically — <see cref="LoadingASettingsFileTwiceDoesNotDoubleMigrateOrLoseARealBinding"/>
    /// uses 15.
    /// </summary>
    [Fact]
    public void LoadingAnAlreadyCurrentHotkeyCodeZeroFileTwiceStillReadsAsABothTimes()
    {
        WithSettingsFile(
            $$"""{"SchemaVersion":{{AppSettings.CurrentSchema}},"HotkeyCode":0,"HotkeyName":"A"}""",
            () =>
            {
                AppSettings first = AppSettings.Load();
                Assert.Equal(0, first.HotkeyCode);
                Assert.True(new HotkeyBinding(first.HotkeyCode, first.HotkeyName).IsValid);

                AppSettings second = AppSettings.Load();
                Assert.Equal(0, second.HotkeyCode);
                Assert.True(new HotkeyBinding(second.HotkeyCode, second.HotkeyName).IsValid);

                string raw = File.ReadAllText(SettingsFilePath);
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                Assert.Equal(0, doc.RootElement.GetProperty("HotkeyCode").GetInt32());
                Assert.Equal(AppSettings.CurrentSchema, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
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

    /// <summary>
    /// Reproduces the ResetEverything() bug in MainWindow.axaml.cs (~line
    /// 3139) directly at the AppSettings level: "reset settings" reflection-
    /// copies every readable/writable property from a freshly constructed
    /// AppSettings onto the live settings, including SchemaVersion. A plain
    /// <c>new AppSettings()</c> defaults that to 0, so a reset would drop the
    /// live settings from CurrentSchema back to 0 — and the very next load
    /// would see a stored 0 and "migrate" a hotkey the user bound to A right
    /// after the reset (HotkeyCode 0) straight back to -1, unbinding it even
    /// though HotkeyName still reads "A".
    ///
    /// This cannot drive the bug through a real MainWindow: the test project
    /// links files in by source (see Testing/JinxyMac.Tests.csproj) and does
    /// not compile MainWindow.axaml.cs, and even if it did, MainWindow's
    /// constructor starts a native hotkey-polling thread and fires a real
    /// network request — not something a deterministic unit test should do.
    /// So this drives the identical operation — the same reflection copy,
    /// over the same class — that both the bug and the fix live in. The
    /// local copy below is written to mirror MainWindow.axaml.cs exactly;
    /// keep the two in sync if that loop ever changes.
    /// </summary>
    [Fact]
    public void ResettingThenBindingAThenReloadingKeepsTheBindingBound()
    {
        WithSettingsFile(
            $$"""{"SchemaVersion":{{AppSettings.CurrentSchema}},"HotkeyCode":15,"HotkeyName":"R"}""",
            () =>
            {
                AppSettings settings = AppSettings.Load();

                // The exact reflection copy ResetEverything() runs, from a
                // fresh AppSettings onto the live settings.
                var fresh = new AppSettings { SchemaVersion = AppSettings.CurrentSchema };

                foreach (System.Reflection.PropertyInfo property in typeof(AppSettings).GetProperties())
                {
                    if (property.CanRead && property.CanWrite)
                        property.SetValue(settings, property.GetValue(fresh));
                }

                settings.Save();

                // The user then binds the start/stop hotkey to A.
                settings.HotkeyCode = 0;
                settings.HotkeyName = "A";
                settings.Save();

                // Next launch.
                AppSettings reloaded = AppSettings.Load();

                Assert.Equal(0, reloaded.HotkeyCode);
                Assert.True(new HotkeyBinding(reloaded.HotkeyCode, reloaded.HotkeyName).IsValid);
            });
    }

    /// <summary>
    /// The catch path (a corrupt or unreadable settings file) must stamp
    /// CurrentSchema exactly like the missing-file path does. Without that,
    /// a hotkey bound against those defaults — including A, HotkeyCode 0 —
    /// would look like a pre-schema file on the very next launch and get
    /// migrated back to -1, silently wiping the binding.
    /// </summary>
    [Fact]
    public void ACorruptFileStampsCurrentSchemaSoARealABindingSurvivesTheNextLoad()
    {
        WithSettingsFile(
            "{ this is not valid json",
            () =>
            {
                AppSettings afterCorruption = AppSettings.Load();
                Assert.Equal(AppSettings.CurrentSchema, afterCorruption.SchemaVersion);

                // Simulate binding A against those defaults and saving, as a
                // user would.
                afterCorruption.HotkeyCode = 0;
                afterCorruption.HotkeyName = "A";
                afterCorruption.Save();

                AppSettings reloaded = AppSettings.Load();

                Assert.Equal(0, reloaded.HotkeyCode);
                Assert.True(new HotkeyBinding(reloaded.HotkeyCode, reloaded.HotkeyName).IsValid);
            });
    }
}
