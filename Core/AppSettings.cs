using System.Text.Json;

namespace JinxyMac.Core;

/// <summary>
/// Everything the user tunes, so a session starts where the last one left off.
/// </summary>
/// <remarks>
/// Deliberately the same shape as the Windows build's file, minus the settings
/// that cannot exist here — registry tweaks, the graphics API selector. Someone
/// moving between the two should find their rates and presets recognisable.
/// </remarks>
public sealed class AppSettings
{
    public double Cps { get; set; } = 10;
    public double Cdc { get; set; } = 50;

    public bool HitFix { get; set; } = true;
    public bool UltraAccuracy { get; set; }

    /// <summary>Which button the clicker presses. Stored by name, not number.</summary>
    /// <remarks>
    /// A name so a settings file stays readable and a reordered enum cannot
    /// silently change what someone's saved configuration does.
    /// </remarks>
    public string ClickButton { get; set; } = "Left";

    /// <summary>Hold the key to click, rather than pressing once to latch.</summary>
    public bool HoldMode { get; set; }

    public bool Shake { get; set; }
    public double ShakeLeft { get; set; } = 7;
    public double ShakeRight { get; set; } = 9;
    public double ShakeUp { get; set; } = 6;
    public double ShakeDown { get; set; } = 5;
    public double ShakeSpeed { get; set; } = 50;

    /// <summary>Key code of the start/stop hotkey, or -1 for unbound. See <see cref="SchemaVersion"/>.</summary>
    public int HotkeyCode { get; set; } = -1;
    public string HotkeyName { get; set; } = "Not set";

    /// <summary>Starts and stops the clicker and shake together.</summary>
    public int ComboCode { get; set; } = -1;
    public string ComboName { get; set; } = "Not set";

    /// <summary>Clicks at the fixed building rate, ignoring both sliders.</summary>
    public int BuildCode { get; set; } = -1;
    public string BuildName { get; set; } = "Not set";

    /// <summary>Starts and stops a recording.</summary>
    public int RecordCode { get; set; } = -1;
    public string RecordName { get; set; } = "Not set";

    /// <summary>Saves what the replay buffer already holds.</summary>
    public int ReplayCode { get; set; } = -1;
    public string ReplayName { get; set; } = "Not set";

    /// <summary>Turns the auto switcher on and off from inside the game.</summary>
    public int SwitcherHotkeyCode { get; set; } = -1;
    public string SwitcherHotkeyName { get; set; } = "Not set";

    /// <summary>
    /// The version of this file's own shape — specifically, of what a stored
    /// 0 means in the six hotkey-code fields above.
    /// </summary>
    /// <remarks>
    /// Below <see cref="CurrentSchema"/>, a stored 0 in any of those fields
    /// means "unbound" — the only meaning 0 ever had, from back when
    /// <c>kVK_ANSI_A</c>'s collision with that sentinel made A unbindable
    /// outright. At or above <see cref="CurrentSchema"/>, 0 means the A key,
    /// because the sentinel moved to -1 (see <see cref="HotkeyBinding.Unbound"/>)
    /// specifically so 0 could stop being special.
    ///
    /// <see cref="Load"/> migrates a file below <see cref="CurrentSchema"/>
    /// exactly once — rewriting a stored 0 to -1 in each of the six fields,
    /// setting this to <see cref="CurrentSchema"/>, and saving immediately so
    /// the check never has to run again for that file. Migrating on read,
    /// rather than assuming an absent or low version always means "no A
    /// binding to worry about", is what stops someone's already-unbound
    /// hotkey being silently reinterpreted as a bound A the first time this
    /// build opens their old settings file.
    /// </remarks>
    public int SchemaVersion { get; set; }

    public const int CurrentSchema = 1;

    /// <summary>The first slot the switcher presses — a hotbar key, typed as a single letter or digit.</summary>
    public string SwitcherSlotA { get; set; } = "3";

    /// <summary>The second slot. Typically the one held longer — see <see cref="SwitcherIntervalBMs"/>.</summary>
    public string SwitcherSlotB { get; set; } = "1";

    /// <summary>How long to hold the first slot, before it is raised to whatever the clicker's own rate demands.</summary>
    public int SwitcherIntervalMs { get; set; } = 150;

    /// <summary>How long to hold the second slot.</summary>
    public int SwitcherIntervalBMs { get; set; } = 900;

    /// <summary>How long the game takes to put a weapon in hand — see <see cref="KeyMacro.MinimumDwellMs"/>.</summary>
    public int SwitcherEquipMs { get; set; } = 60;

    /// <summary>
    /// The switcher's own switch, independent of the master hotkeys toggle —
    /// see <see cref="KeyMacro.Enabled"/>'s remarks for why a macro (and the
    /// switcher is one) gets this in addition to the master kill.
    /// </summary>
    public bool SwitcherDisabled { get; set; }

    public int RecordFps { get; set; } = 60;

    public bool ReplayEnabled { get; set; }
    public int ReplaySeconds { get; set; } = 30;

    /// <summary>avfoundation index of the screen to capture, or -1 for the first one found.</summary>
    public int RecordScreen { get; set; } = -1;

    public string AccentColor { get; set; } = "#FF4B52";

    /// <summary>Dark mode. The palette swaps; the accent does not.</summary>
    public bool Dark { get; set; } = true;

    /// <summary>Bare file name of the stored wallpaper, or empty for none.</summary>
    /// <remarks>
    /// A name rather than a path: the file is copied into the settings folder,
    /// so where it came from stops mattering the moment it is chosen.
    /// </remarks>
    public string WallpaperName { get; set; } = "";

    /// <summary>How far the wallpaper is darkened, as a percentage.</summary>
    public int WallpaperDimming { get; set; } = Wallpaper.DefaultDimming;

    public double Opacity { get; set; } = 1.0;

    /// <summary>Where clips go, or empty for the platform default.</summary>
    public string ClipFolder { get; set; } = "";

    /// <summary>Silences every hotkey without unbinding any of them.</summary>
    public bool HotkeysOn { get; set; } = true;

    /// <summary>Show the menu bar item. On Windows this is the notification area.</summary>
    public bool MenuBar { get; set; } = true;

    /// <summary>Look for a newer build when the app opens.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    private static string File => SettingsPath.For("settings.json");

    /// <summary>A missing or unreadable file just means defaults.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!System.IO.File.Exists(File))
            {
                // A missing settings file is the one reliable signal for a
                // brand new install. Someone who has settings and no wallpaper
                // cleared it on purpose, and reinstalling it under them would
                // read as the app deciding for itself. A fresh install has
                // nothing to migrate, so it starts at the current schema
                // rather than at the default 0 — which would otherwise read
                // as needing migration the moment it was ever saved and
                // reloaded.
                return new AppSettings
                {
                    WallpaperName = Wallpaper.InstallDefault(),
                    SchemaVersion = CurrentSchema
                };
            }

            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(File))
                                    ?? new AppSettings();

            // A file with no "schemaVersion" member deserializes with the
            // property at its default, 0 — below CurrentSchema, so this
            // always catches every settings file written before this
            // feature shipped. See SchemaVersion's remarks for why the
            // rewrite happens here, once, rather than being guessed at.
            if (settings.SchemaVersion < CurrentSchema) settings.Migrate();

            return settings;
        }
        catch
        {
            // Same reasoning as the missing-file path above: a fresh set of
            // defaults has nothing to migrate, so it starts at the current
            // schema. Leaving SchemaVersion at its default of 0 here would
            // make the next launch "migrate" a hotkey the user binds against
            // these defaults — including A, HotkeyCode 0 — right back to -1,
            // silently unbinding it the first time a corrupt or unreadable
            // file is replaced by a real one.
            return new AppSettings { SchemaVersion = CurrentSchema };
        }
    }

    /// <summary>
    /// Rewrites a pre-migration stored 0 to -1 in each hotkey-code field, then
    /// saves so this never has to run again for this file. See
    /// <see cref="SchemaVersion"/> for why this exists at all.
    /// </summary>
    private void Migrate()
    {
        if (HotkeyCode == 0) HotkeyCode = -1;
        if (ComboCode == 0) ComboCode = -1;
        if (BuildCode == 0) BuildCode = -1;
        if (RecordCode == 0) RecordCode = -1;
        if (ReplayCode == 0) ReplayCode = -1;
        if (SwitcherHotkeyCode == 0) SwitcherHotkeyCode = -1;

        SchemaVersion = CurrentSchema;
        Save();
    }

    public void Save()
    {
        try
        {
            SettingsPath.WriteAtomic(File,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Losing a preference is not worth taking the app down for.
        }
    }
}
