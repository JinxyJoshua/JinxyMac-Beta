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

    /// <summary>Key code of the start/stop hotkey, or 0 for unbound.</summary>
    public int HotkeyCode { get; set; }
    public string HotkeyName { get; set; } = "Not set";

    /// <summary>Starts and stops the clicker and shake together.</summary>
    public int ComboCode { get; set; }
    public string ComboName { get; set; } = "Not set";

    /// <summary>Clicks at the fixed building rate, ignoring both sliders.</summary>
    public int BuildCode { get; set; }
    public string BuildName { get; set; } = "Not set";

    /// <summary>Starts and stops a recording.</summary>
    public int RecordCode { get; set; }
    public string RecordName { get; set; } = "Not set";

    /// <summary>Saves what the replay buffer already holds.</summary>
    public int ReplayCode { get; set; }
    public string ReplayName { get; set; } = "Not set";

    public int RecordFps { get; set; } = 60;

    public bool ReplayEnabled { get; set; }
    public int ReplaySeconds { get; set; } = 30;

    /// <summary>avfoundation index of the screen to capture, or -1 for the first one found.</summary>
    public int RecordScreen { get; set; } = -1;

    public string AccentColor { get; set; } = "#FF4B52";

    /// <summary>Dark mode. The palette swaps; the accent does not.</summary>
    public bool Dark { get; set; } = true;

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
            if (!System.IO.File.Exists(File)) return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(File))
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            System.IO.File.WriteAllText(File,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Losing a preference is not worth taking the app down for.
        }
    }
}
