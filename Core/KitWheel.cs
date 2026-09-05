using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JinxyMac.Core;

/// <summary>
/// The kit list for the randomizer, and the run through it.
/// </summary>
/// <remarks>
/// Nothing ships in the list. Kit rosters change every season, so a built-in one
/// would be wrong within months and would read as the app being out of date
/// rather than as a list somebody has not filled in yet.
/// </remarks>
public static class KitWheel
{
    /// <summary>Below two there is nothing to choose between.</summary>
    public const int MinKits = 2;

    /// <summary>
    /// The roster can be long — the game has well over a hundred kits — because
    /// what goes on the wheel is the ticked ones, not all of them.
    /// </summary>
    public const int MaxKits = 200;

    public const int MaxNameLength = 24;

    /// <summary>
    /// Cleans up a typed name, or refuses it.
    /// </summary>
    /// <returns>The name to store, or null if it cannot be used.</returns>
    public static string? CleanName(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        // Collapsed rather than only trimmed: "Melody  " and "Melody" are the
        // same kit, and storing both puts it in the pool twice.
        string cleaned = string.Join(" ",
            typed.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (cleaned.Length == 0) return null;

        return cleaned.Length <= MaxNameLength ? cleaned : cleaned[..MaxNameLength].TrimEnd();
    }

    /// <summary>
    /// Adds a kit if it is usable and not already there.
    /// </summary>
    /// <returns>True if the list changed.</returns>
    public static bool Add(List<string> kits, string? typed)
    {
        string? name = CleanName(typed);

        if (name == null || kits.Count >= MaxKits) return false;

        // Case-insensitive: "Melody" and "melody" in the pool together is one
        // kit with two chances, which is a silent thumb on the scale.
        if (kits.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase))) return false;

        kits.Add(name);

        return true;
    }

    /// <summary>Whether there is enough on the list to roll between.</summary>
    public static bool CanSpin(IReadOnlyCollection<string> kits) => kits.Count >= MinKits;

    /// <summary>
    /// A starting roster, so the page is a list to tick rather than a hundred
    /// names to type.
    /// </summary>
    /// <remarks>
    /// Read off the game's own kit screen rather than from a guide, so the
    /// spellings match what the game shows — accents, apostrophes and brackets
    /// included. Still editable: rosters change every season, so anything added
    /// later gets typed in and anything retired gets removed.
    ///
    /// "None" is deliberately absent. It is the no-kit option on that screen,
    /// not a kit, and rolling it would mean playing without one.
    ///
    /// Nothing is ticked by default. A run is a set somebody chose, and
    /// pre-selecting a hundred kits would mean unticking most of them first.
    /// </remarks>
    public static List<string> StarterRoster() => new()
    {
        "Abaddon", "Adetunde", "Aery", "Agni", "Alchemist", "Arachne", "Archer",
        "Ares", "Axolotl Amy", "Baker", "Barbarian", "Beekeeper Beatrix",
        "Bekzat", "Bounty Hunter", "Builder", "Caitlyn", "Cobalt", "Cogsworth",
        "Conqueror", "Crocowolf", "Crypt", "Cyber", "Death Adder",
        "Dino Tamer Dom", "Drill", "Eldertree", "Eldric", "Elektra", "Ember",
        "Evelynn", "Farmer Cletus", "Fisherman", "Flora", "Fortuna", "Freiya",
        "Frosty", "Gingerbread Man", "Gompy", "Grim Reaper", "Grove", "Hannah",
        "Hephaestus", "Ignis", "Infernal Shielder", "Isabel", "Jack", "Jade",
        "Kaida", "Kaliyah", "Krystal", "Lani", "Lassy", "Lian", "Lucía",
        "Lumen", "Lyla", "Marcel", "Marina", "Marrow", "Martin", "Melody",
        "Merchant Marco", "Metal Detector", "Milo", "Miner", "Nahla", "Nazar",
        "Noelle", "Nyoka", "Nyx", "Pirate Davey", "Pyro", "Ragnar", "Ramil",
        "Raven", "Santa", "Sheep Herder", "Sheila", "Sigrid", "Silas", "Skoll",
        "Smoke", "Sophia", "Spirit Catcher", "Star Collector Stella", "Styx",
        "Taliyah", "Terra", "Trapper", "Trinity", "Triton", "Trixie", "Uma",
        "Umbra", "Umeko", "Vanessa", "Void Knight", "Void Regent", "Vulcan",
        "Warden", "Warrior", "Whim", "Whisper", "Wren", "Xu'rot", "Yamini",
        "Yeti", "Yuzi", "Zarrah", "Zenith", "Zeno (Wizard)", "Zephyr", "Zola"
    };

    // ---- rolling without replacement ----

    /// <summary>
    /// The kits not yet rolled this run.
    /// </summary>
    /// <remarks>
    /// The run is getting through every kit once, so a roll takes one out of the
    /// pool rather than picking freely each time. Rolling the same kit twice
    /// would make the remaining count meaningless and the run never finish.
    /// </remarks>
    /// <summary>The kits whose name contains what was typed.</summary>
    /// <remarks>
    /// Contains rather than starts-with: the roster is full of two-word names,
    /// and somebody looking for "Axolotl Amy" is as likely to type "amy". Blank
    /// means no filter at all rather than no matches, so clearing the box puts
    /// the whole roster back.
    /// </remarks>
    public static List<string> Matching(IEnumerable<string> kits, string? search)
    {
        string term = (search ?? "").Trim();

        return term.Length == 0
            ? kits.ToList()
            : kits.Where(k => k.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static List<string> Remaining(IEnumerable<string> kits, IEnumerable<string> used)
    {
        var done = new HashSet<string>(used, StringComparer.OrdinalIgnoreCase);

        return kits.Where(k => !done.Contains(k)).ToList();
    }

    /// <summary>
    /// Picks one of the kits still to come.
    /// </summary>
    /// <param name="pick">
    /// Given the pool size, returns an index. Passed in so the choice can be
    /// tested without depending on what a random source happens to produce.
    /// </param>
    /// <returns>The kit rolled, or null when the run is finished.</returns>
    public static string? Roll(
        IEnumerable<string> kits, IEnumerable<string> used, Func<int, int> pick)
    {
        List<string> pool = Remaining(kits, used);

        if (pool.Count == 0) return null;

        int index = pick(pool.Count);

        // Clamped rather than thrown on. The caller is a random source, and a
        // run should not end on an exception.
        return pool[Math.Clamp(index, 0, pool.Count - 1)];
    }

    /// <summary>How far through the run, as a fraction of the whole list.</summary>
    public static double Progress(int total, int done) =>
        total <= 0 ? 0.0 : Math.Clamp((double)done / total, 0.0, 1.0);

    // ---- saved wheels ----

    /// <summary>Most saved wheels one person needs.</summary>
    public const int MaxPresets = 12;

    /// <summary>
    /// Saves the picked kits under a name, replacing any wheel of that name.
    /// </summary>
    /// <remarks>
    /// Replaces rather than adds, so saving twice under one name updates it
    /// instead of leaving two entries that look identical and behave
    /// differently. The kits are copied, not referenced — a saved wheel is a
    /// snapshot, and one that changed every time somebody ticked a box would be
    /// no use at all.
    /// </remarks>
    /// <returns>False when there is nothing to save, or no room.</returns>
    public static bool SavePreset(List<KitPreset> presets, string? name, IEnumerable<string> picked)
    {
        string? clean = CleanName(name);
        List<string> kits = picked.ToList();

        if (clean == null || kits.Count == 0) return false;

        int at = presets.FindIndex(p => p.Name.Equals(clean, StringComparison.OrdinalIgnoreCase));

        if (at >= 0)
        {
            presets[at] = new KitPreset { Name = clean, Kits = kits };
            return true;
        }

        if (presets.Count >= MaxPresets) return false;

        presets.Add(new KitPreset { Name = clean, Kits = kits });

        return true;
    }

    /// <summary>
    /// The kits a saved wheel selects, given the roster as it stands now.
    /// </summary>
    /// <remarks>
    /// Filtered against the roster rather than applied whole. A wheel saved
    /// before a kit was removed would otherwise put a name back in the pool that
    /// nothing on the page can show or untick — it would be rolled and never
    /// understood.
    /// </remarks>
    public static List<string> ApplyPreset(IEnumerable<string> roster, IEnumerable<string> saved)
    {
        var known = new HashSet<string>(roster, StringComparer.OrdinalIgnoreCase);

        return saved.Where(known.Contains).ToList();
    }

    /// <summary>Whether every kit has been rolled.</summary>
    public static bool IsComplete(IReadOnlyCollection<string> kits, IReadOnlyCollection<string> used) =>
        kits.Count > 0 && Remaining(kits, used).Count == 0;
}

/// <summary>A saved selection of kits, under a name.</summary>
public sealed class KitPreset
{
    public string Name { get; set; } = "";
    public List<string> Kits { get; set; } = new();
}

/// <summary>The roster, which of it is ticked, and any saved wheels.</summary>
public sealed class KitRoster
{
    public List<string> Kits { get; set; } = new();
    public List<string> Selected { get; set; } = new();
    public List<KitPreset> Presets { get; set; } = new();
}

/// <summary>Reading and writing the randomizer's roster.</summary>
public static class KitWheelStore
{
    private static readonly string StoreFile = SettingsPath.For("kit_wheel.json");

    /// <summary>
    /// The roster a machine that has never run this before starts with.
    /// </summary>
    /// <remarks>
    /// Every kit on the list, and <em>none of them ticked</em>. That is the
    /// point rather than an oversight: the wheel is meant to be the kits you
    /// actually play, so a first run asks the question instead of answering it
    /// with all 113 — which would make the first roll land on something the
    /// person has never used.
    ///
    /// Named rather than written inline twice, because both the no-file case
    /// and the unreadable-file case have to agree on it.
    /// </remarks>
    public static KitRoster Fresh() => new() { Kits = KitWheel.StarterRoster() };

    public static KitRoster Load()
    {
        try
        {
            if (!File.Exists(StoreFile)) return Fresh();

            return Parse(File.ReadAllText(StoreFile));
        }
        catch
        {
            return Fresh();
        }
    }

    /// <summary>Reads a stored roster, in either the current or the first format.</summary>
    public static KitRoster Parse(string json)
    {
        json = json.TrimStart();

        // The first version of this file was a bare array of names, written
        // before anything could be ticked. Read as a roster with everything
        // selected, which is what it meant at the time — the one case where a
        // roster arrives fully ticked, and only for people upgrading.
        KitRoster roster = json.StartsWith("[")
            ? LegacyList(json)
            : JsonSerializer.Deserialize<KitRoster>(json) ?? new KitRoster();

        return Clean(roster);
    }

    private static KitRoster LegacyList(string json)
    {
        List<string> names = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();

        return new KitRoster { Kits = names, Selected = new List<string>(names) };
    }

    /// <summary>
    /// Rebuilds a loaded roster rather than trusting it.
    /// </summary>
    /// <remarks>
    /// A hand-edited file could otherwise carry duplicates, blanks, or a kit
    /// ticked that is not on the roster at all — which would put a name in the
    /// pool that the list cannot show or untick.
    /// </remarks>
    private static KitRoster Clean(KitRoster roster)
    {
        var kits = new List<string>();
        foreach (string kit in roster.Kits) KitWheel.Add(kits, kit);

        var selected = kits
            .Where(k => roster.Selected.Any(s => s.Equals(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Saved wheels are cleaned the same way, so a hand-edited file cannot
        // leave a preset naming kits the roster has never heard of.
        var presets = new List<KitPreset>();

        foreach (KitPreset preset in roster.Presets)
        {
            string? name = KitWheel.CleanName(preset.Name);
            if (name == null) continue;

            List<string> allowed = KitWheel.ApplyPreset(kits, preset.Kits);
            if (allowed.Count == 0) continue;

            KitWheel.SavePreset(presets, name, allowed);
        }

        return new KitRoster { Kits = kits, Selected = selected, Presets = presets };
    }

    public static void Save(KitRoster roster)
    {
        try
        {
            File.WriteAllText(StoreFile, JsonSerializer.Serialize(roster));
        }
        catch
        {
            // A roster that cannot save is still a roster that rolls.
        }
    }
}
