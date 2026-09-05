using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JinxyMac.Core;

/// <summary>
/// A key, or a short cycle of keys, sent over and over on a timer.
/// </summary>
/// <remarks>
/// One shape covers both features people ask for. A macro that spams a single
/// key is the macro creator; a macro that alternates two keys is the inventory
/// switcher — sword to crossbow, pickaxe to gumdrop. Building the general one
/// gets the specific one for nothing, and the alternative was two engines that
/// drift apart.
/// </remarks>
public sealed class KeyMacro
{
    /// <summary>Fastest a macro may repeat. Below this it is a key that never comes up.</summary>
    public const int MinIntervalMs = 5;

    /// <summary>Slowest worth offering — beyond this a hotkey is easier.</summary>
    public const int MaxIntervalMs = 10_000;

    public KeyMacro(string name, IEnumerable<int> keys, string keysText, int intervalMs,
                    int[]? holdsMs = null, int clicksWanted = 0, int equipMs = DefaultEquipMs,
                    HotkeyBinding? hotkey = null, bool enabled = true)
    {
        Enabled = enabled;
        Name = name;
        // These are the platform's own key codes — CGKeyCodes here, virtual-key
        // codes on Windows. Both spaces pass this same 0 < k < 256 filter while
        // meaning entirely different keys; nothing this narrow can tell them
        // apart, which is why MacroStore's file format has to.
        Keys = keys.Where(k => k is > 0 and < 256).ToArray();
        KeysText = keysText;
        IntervalMs = Math.Clamp(intervalMs, MinIntervalMs, MaxIntervalMs);

        HoldsMs = holdsMs?.Select(h => Math.Clamp(h, MinIntervalMs, MaxIntervalMs)).ToArray();

        ClicksWanted = Math.Max(0, clicksWanted);
        EquipMs = Math.Clamp(equipMs, 0, MaxIntervalMs);

        Hotkey = hotkey ?? HotkeyBinding.Unbound;
    }

    /// <summary>
    /// Whether this macro may run at all.
    /// </summary>
    /// <remarks>
    /// Switched off one macro at a time, rather than only by the master switch.
    /// Somebody with six macros bound usually wants five of them live and one
    /// out of the way — turning the lot off to silence a single key means
    /// remembering which were on before, and turning them back on one by one.
    ///
    /// Separate from being stopped: a disabled macro cannot be started by its
    /// key, by its switch, or by anything else, and stays that way across
    /// restarts.
    /// </remarks>
    public bool Enabled { get; }

    /// <summary>
    /// Toggles this macro on and off when pressed. Unbound means no shortcut —
    /// the switch on the card is the only way to start it.
    /// </summary>
    /// <remarks>
    /// The same type the global hotkeys use, so the polling loop, rebind flow,
    /// and collision check all treat a macro's toggle as one more binding
    /// rather than a parallel thing they had to learn about.
    /// </remarks>
    public HotkeyBinding Hotkey { get; }

    /// <summary>
    /// Clicks the first key must actually receive before the cycle moves on.
    /// </summary>
    /// <remarks>
    /// The point this feature turns on. A dwell measured in milliseconds is a
    /// guess about someone else's frame rate, equip animation and click rate,
    /// and it is wrong in both directions at once — too short and the weapon
    /// never fires, too long and it sits in your hand while the sword should
    /// be swinging.
    ///
    /// Counting the clicks the clicker has actually delivered replaces the
    /// guess with the thing the guess was approximating. The dip lasts exactly
    /// as long as it takes to fire and not one tick longer, whatever the CPS
    /// happens to be.
    ///
    /// Zero means time only, which is what an ordinary macro wants.
    /// </remarks>
    public int ClicksWanted { get; }

    /// <summary>Time to allow for the weapon to appear before clicks count.</summary>
    public int EquipMs { get; }

    /// <summary>
    /// How long to stay on each key, when they should not be equal.
    /// </summary>
    /// <remarks>
    /// The switcher needs this and an even rotation cannot give it. Half a
    /// second on the crossbow and half on the sword means the crossbow is in
    /// hand half the time and re-drawn on every return — which is why rotating
    /// fires slower than switching once by hand.
    ///
    /// Measurement turned this around. A recording of it done by hand shows the
    /// sword held for one to two seconds and the crossbow dipped to for about a
    /// sixth of one — the opposite of the guess this was built on. Null means
    /// every key gets IntervalMs, which is what an ordinary macro wants.
    /// </remarks>
    public int[]? HoldsMs { get; }

    /// <summary>How long to wait after pressing the key at this position.</summary>
    public int DwellFor(int index) =>
        HoldsMs != null && index < HoldsMs.Length ? HoldsMs[index] : IntervalMs;

    /// <summary>
    /// How long a weapon must stay in hand to be guaranteed to fire.
    /// </summary>
    /// <remarks>
    /// Swapping to a weapon does not arm it. Roblox plays an equip animation
    /// first, and a click that lands during it does nothing — so a rotation
    /// faster than the animation produces a weapon that is drawn over and over
    /// and never fired.
    ///
    /// Derived rather than guessed. The equip is a property of the game; the
    /// click period is a property of the clicker, which this app already knows.
    /// Two clicks rather than one because the first can land in the same
    /// instant the equip completes and be swallowed by the boundary — the
    /// second is what makes "every time" true instead of "usually".
    /// </remarks>
    public static int MinimumDwellMs(double clickPeriodMs, int equipMs = DefaultEquipMs, int clicks = 2)
    {
        if (double.IsNaN(clickPeriodMs) || clickPeriodMs <= 0) clickPeriodMs = 100;

        double needed = equipMs + clickPeriodMs * clicks;

        return (int)Math.Clamp(Math.Ceiling(needed), MinIntervalMs, MaxIntervalMs);
    }

    /// <summary>
    /// How long Roblox takes to put a weapon in your hand.
    /// </summary>
    /// <remarks>
    /// Measured off a recording of someone doing it by hand: the crossbow was
    /// dipped to for 130 to 200 milliseconds and fired reliably every time, so
    /// the equip has to cost well under that. An earlier guess of 250 was wrong
    /// by roughly four times and would have forced a rotation slower than the
    /// hand it was meant to copy.
    /// </remarks>
    public const int DefaultEquipMs = 60;

    public string Name { get; }

    /// <summary>Virtual key codes, sent in order and then from the top again.</summary>
    public int[] Keys { get; }

    /// <summary>The keys as the user typed them, for the card and for editing.</summary>
    public string KeysText { get; }

    public int IntervalMs { get; }

    public bool IsUsable => Keys.Length > 0 && Name.Trim().Length > 0;

    /// <summary>What the card says under the name.</summary>
    public string RateText => IntervalMs >= 1000
        ? $"every {IntervalMs / 1000.0:0.##}s"
        : $"every {IntervalMs} ms";

    public string SummaryText => Keys.Length > 1
        ? $"{KeysText}  ·  {RateText}  ·  cycles"
        : $"{KeysText}  ·  {RateText}";
}

/// <summary>
/// The saved macros, kept beside the click presets.
/// </summary>
public static class MacroStore
{
    private static readonly string MacrosFile = SettingsPath.For("macros.json");

    /// <summary>What this build's key codes mean.</summary>
    /// <remarks>
    /// Written into the file and checked on the way back in. KeyMacro's key
    /// filter accepts 0-255 on both platforms, but those are CGKeyCodes here
    /// and virtual-key codes on Windows — a file carried across would load
    /// without complaint and press entirely different keys.
    ///
    /// Deliberately unlike history.json, which was kept identical across
    /// platforms on purpose so someone using both could copy their numbers
    /// over. Numbers mean the same thing everywhere; key codes do not.
    ///
    /// A mismatch loads as no macros rather than as an error. A macro pressing
    /// an unintended key in a game is worse than a macro that is missing.
    /// </remarks>
    private const string PlatformTag = "macos";

    private sealed class StoredMacro
    {
        public string Name { get; set; } = "";
        public int[] Keys { get; set; } = Array.Empty<int>();
        public string KeysText { get; set; } = "";
        public int IntervalMs { get; set; } = 100;
        // Zero means unbound. Absent from files written before this feature
        // shipped, which defaults them to unbound on load — no migration.
        public int HotkeyCode { get; set; }
        public string HotkeyName { get; set; } = "";

        // Absent from files written before this shipped, and a missing bool
        // reads as false — which would silently disable every existing macro.
        // Stored as "Disabled" so the old files' absence means enabled.
        public bool Disabled { get; set; }
    }

    /// <summary>
    /// The file on disk: which platform wrote it, alongside the macros
    /// themselves. An object rather than a bare array so the platform tag has
    /// somewhere to live without hijacking the macro list's own shape.
    /// </summary>
    private sealed class StoredFile
    {
        [JsonPropertyName("platform")]
        public string? Platform { get; set; }

        [JsonPropertyName("macros")]
        public List<StoredMacro> Macros { get; set; } = new();
    }

    /// <summary>
    /// Nothing. A fresh install starts with an empty list.
    /// </summary>
    /// <remarks>
    /// Shipping example macros was a mistake worth naming: they arrive looking
    /// like features the app provides rather than things the user made, and the
    /// first instinct is to delete them. A macro is only useful if it is one
    /// somebody chose.
    /// </remarks>
    public static List<KeyMacro> Defaults() => new();

    /// <summary>
    /// The auto switcher used to live in this list before it became its own
    /// page. Anyone who ran that build has it saved, and it would show up twice.
    /// </summary>
    private static bool IsLegacySwitcher(string name) =>
        name.Trim().Equals("Auto Switcher", StringComparison.OrdinalIgnoreCase);

    public static List<KeyMacro> Load()
    {
        try
        {
            if (!File.Exists(MacrosFile)) return Defaults();

            var file = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(MacrosFile));
            if (file == null) return Defaults();

            // A file whose platform is missing or is anything other than this
            // build's own is treated as if it held no macros at all — never
            // partially loaded, never an error. See PlatformTag for why.
            if (file.Platform != PlatformTag) return Defaults();

            return file.Macros
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .Where(m => !IsLegacySwitcher(m.Name))
                .Select(m => new KeyMacro(
                    m.Name, m.Keys, m.KeysText, m.IntervalMs,
                    hotkey: m.HotkeyCode > 0 ? new HotkeyBinding(m.HotkeyCode, m.HotkeyName) : HotkeyBinding.Unbound,
                    enabled: !m.Disabled))
                .ToList();
        }
        catch
        {
            return Defaults();
        }
    }

    public static void Save(IEnumerable<KeyMacro> macros)
    {
        try
        {
            var stored = macros
                .Select(m => new StoredMacro
                {
                    Name = m.Name,
                    Keys = m.Keys,
                    KeysText = m.KeysText,
                    IntervalMs = m.IntervalMs,
                    HotkeyCode = m.Hotkey.Code,
                    HotkeyName = m.Hotkey.Name,
                    Disabled = !m.Enabled
                })
                .ToList();

            var file = new StoredFile { Platform = PlatformTag, Macros = stored };

            File.WriteAllText(MacrosFile,
                JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>Adds a macro, or replaces the one that already has that name.</summary>
    public static void Upsert(List<KeyMacro> macros, KeyMacro macro)
    {
        int existing = macros.FindIndex(m =>
            string.Equals(m.Name, macro.Name, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0) macros[existing] = macro;
        else macros.Add(macro);
    }

    /// <summary>
    /// Reads keys typed as "1, 2" or "R" into virtual key codes.
    /// </summary>
    /// <remarks>
    /// Letters and digits only, which covers every hotbar slot and every action
    /// key in the game this is for. Accepting the whole keyboard would mean
    /// parsing "Left Shift" and deciding what a macro that holds a modifier
    /// even means.
    /// </remarks>
    public static (int[] Keys, string Text)? ParseKeys(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        var keys = new List<int>();
        var names = new List<string>();

        // The separators as an explicit array. Written as Split(',', ' ', options)
        // it binds to the (char, int count, options) overload instead — a space
        // converts to int silently — and splits on commas only, with a limit of
        // thirty-two. It compiles, and "1 2" quietly parses as one key.
        char[] separators = { ',', ' ' };

        foreach (string piece in typed.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string one = piece.Trim().ToUpperInvariant();

            if (one.Length != 1) return null;

            char c = one[0];

            if (!char.IsLetterOrDigit(c)) return null;

            keys.Add(c);
            names.Add(one);
        }

        return keys.Count == 0 ? null : (keys.ToArray(), string.Join(", ", names));
    }

    /// <summary>Reads an interval, or null when it is not one.</summary>
    public static int? ParseInterval(string? typed)
    {
        if (!int.TryParse(typed?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int ms)
            && !int.TryParse(typed?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms))
        {
            return null;
        }

        return ms is < KeyMacro.MinIntervalMs or > KeyMacro.MaxIntervalMs ? null : ms;
    }
}
