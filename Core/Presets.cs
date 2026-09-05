using System.Globalization;
using System.Text.Json;

namespace JinxyMac.Core;

/// <summary>
/// A saved CPS / CDC pair. Built-in presets ship with the app; custom ones are
/// user-created and persisted alongside them.
/// </summary>
public sealed class ClickPreset
{
    /// <summary>Widest CPS the bar is scaled against.</summary>
    private const double BarScaleCps = 150.0;
    private const double BarTrackWidth = 134.0;

    public ClickPreset(string name, double cps, double cdc, bool holdMode = false)
    {
        Name = name;
        Cps = cps;
        Cdc = cdc;
        HoldMode = holdMode;
    }

    public string Name { get; }
    public double Cps { get; }
    public double Cdc { get; }

    /// <summary>Hold to click, rather than press once to start and again to stop.</summary>
    public bool HoldMode { get; }

    /// <summary>Second line on the card: what this preset does beyond the rates.</summary>
    public string ModeText => HoldMode ? "Hold" : "Toggle";

    // Two decimals, matching the Clicker page, so a preset saved from the
    // sliders reads back identically rather than looking rounded.
    public string CpsText => Cps.ToString("0.00", CultureInfo.CurrentCulture);
    public string CdcText => Cdc.ToString("0.00", CultureInfo.CurrentCulture);

    /// <summary>Bar length, so a preset's speed is legible before reading the number.</summary>
    public double BarWidth => Math.Clamp(Cps / BarScaleCps, 0.015, 1.0) * BarTrackWidth;
}

/// <summary>
/// Persists the whole preset list, not just user-created entries.
/// </summary>
/// <remarks>
/// Any preset can be deleted, so the defaults have to be able to stay deleted.
/// Regenerating them from code on every launch would resurrect what the user
/// removed, which is the bug this shape exists to avoid.
/// </remarks>
public static class PresetStore
{
    private static string File => SettingsPath.For("click_presets.json");

    /// <summary>
    /// A class with initialisers rather than a positional record, so a file
    /// written before a field existed still loads with a sensible value instead
    /// of a zero.
    /// </summary>
    private sealed class StoredPreset
    {
        public string Name { get; set; } = "";
        public double Cps { get; set; }
        public double Cdc { get; set; }
        public bool HoldMode { get; set; }
    }

    /// <summary>The list a fresh install starts from, and what Restore adds back.</summary>
    public static List<ClickPreset> Defaults() => new()
    {
        // Measured, not chosen. Frame by frame off a match where this exact
        // configuration landed 34 hits on a run that a 193 CPS setup landed 33
        // — the whole point being that the slower one won. Hold mode and the
        // duty cycle are as load-bearing as the rate, so it ships as all three.
        //
        // First in the list because it is the only entry here with a number
        // behind it, and because the rates below are the ones that lose.
        new ClickPreset("Measured", 41.2, 77.37, holdMode: true),
        new ClickPreset("Ish", 193.62, 73.52),
        new ClickPreset("Snoopy", 75.65, 91.21),
        new ClickPreset("Stunned", 72.92, 17.16),
        new ClickPreset("Sky", 52.62, 82.62),
        new ClickPreset("Ara", 82.72, 27.28),
        new ClickPreset("Spooky", 29.28, 83.62),
        new ClickPreset("Milo", 53.87, 28.53),
        new ClickPreset("Lee", 29.62, 92.72),
        new ClickPreset("Sharkiffy", 72.53, 55.73),
        new ClickPreset("AraStxr", 65.33, 42.55),
        new ClickPreset("YoNoobLike", 85.86, 64.25)
    };

    /// <summary>
    /// A missing file means first run, so seed the defaults. A file that exists
    /// but holds an empty list means the user deleted everything — respect it.
    /// </summary>
    public static List<ClickPreset> Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return Defaults();

            List<StoredPreset>? stored =
                JsonSerializer.Deserialize<List<StoredPreset>>(System.IO.File.ReadAllText(File));

            if (stored == null) return Defaults();

            return stored
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new ClickPreset(p.Name, p.Cps, p.Cdc, p.HoldMode))
                .ToList();
        }
        catch
        {
            return Defaults();
        }
    }

    public static void Save(IEnumerable<ClickPreset> presets)
    {
        try
        {
            List<StoredPreset> stored = presets
                .Select(p => new StoredPreset
                {
                    Name = p.Name,
                    Cps = p.Cps,
                    Cdc = p.Cdc,
                    HoldMode = p.HoldMode
                })
                .ToList();

            System.IO.File.WriteAllText(File,
                JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Losing a preset is not worth taking the app down for.
        }
    }

    /// <summary>
    /// Adds a preset, or replaces the one that already has that name.
    /// </summary>
    /// <remarks>
    /// Replacing rather than refusing is what makes the pencil work: editing a
    /// preset is saving over it, and the two are the same operation from here.
    /// Names are matched case-insensitively so "sky" does not become a second
    /// entry beside "Sky".
    /// </remarks>
    public static void Upsert(List<ClickPreset> presets, ClickPreset preset)
    {
        int existing = presets.FindIndex(p =>
            string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0) presets[existing] = preset;
        else presets.Add(preset);
    }

    /// <summary>
    /// Reads a rate the user typed, or null if it is not one.
    /// </summary>
    /// <remarks>
    /// Empty and unparseable are the same answer — do not save this. A blank CPS
    /// silently becoming zero would produce a preset that stops the clicker.
    /// </remarks>
    public static double? ParseRate(string? text, double max)
    {
        if (!double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
            && !double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return null;
        }

        if (double.IsNaN(value) || value < 0 || value > max) return null;

        return value;
    }
}
