namespace JinxyMac.Core;

/// <summary>
/// Every colour in the app except the accent, as one swappable set.
/// </summary>
/// <remarks>
/// Named by role rather than by colour — Panel, Sunken, TextMuted — which is
/// the only reason a light mode is possible at all. A palette full of names
/// like "DarkGrey" cannot be swapped for a light one without every name
/// becoming a lie.
///
/// The accent is deliberately not in here. It is chosen independently and has
/// to survive a mode change, so it lives on its own.
/// </remarks>
public sealed record Palette(
    string Backdrop,
    string Panel,
    string Panel2,
    string Rail,
    string Sunken,
    string Control,
    string ControlAlt,
    string Track,
    string Divider,
    string Hairline,
    string Hover,
    string Text,
    string TextBright,
    string TextSoft,
    string TextDim,
    string TextMuted)
{
    /// <summary>The shipped look, and what the Windows build uses.</summary>
    public static Palette Dark { get; } = new(
        Backdrop: "#0F1528",
        Panel: "#171F38",
        Panel2: "#131A30",
        Rail: "#121930",
        Sunken: "#0B1120",
        Control: "#212B4B",
        ControlAlt: "#1B2440",
        Track: "#2C3860",
        Divider: "#27324F",
        Hairline: "#1AFFFFFF",
        Hover: "#12FFFFFF",
        Text: "#EEF2FF",
        TextBright: "#FFFFFF",
        TextSoft: "#A4AFD2",
        TextDim: "#C9D2EC",
        TextMuted: "#8B95B9");

    public static Palette Light { get; } = new(
        Backdrop: "#ECEFF9",
        Panel: "#FFFFFF",
        Panel2: "#F5F7FE",
        Rail: "#E3E7F6",
        Sunken: "#EBEFFA",
        Control: "#DFE4F3",
        ControlAlt: "#D5DBEF",
        Track: "#C0C8E2",
        Divider: "#DAE0F1",
        Hairline: "#14000000",
        Hover: "#0D000000",
        Text: "#141A2E",
        TextBright: "#070B16",
        TextSoft: "#49536F",
        TextDim: "#313B55",
        TextMuted: "#69739A");

    public static Palette For(bool dark) => dark ? Dark : Light;

    /// <summary>
    /// The palette as resource-key / colour pairs, for applying in one loop.
    /// </summary>
    /// <remarks>
    /// A list rather than fifteen assignments, so adding a colour means adding
    /// it in one place instead of two — the second of which is easy to forget
    /// and shows up only when someone switches mode.
    /// </remarks>
    public IEnumerable<(string Key, string Hex)> Entries()
    {
        yield return ("Backdrop", Backdrop);
        yield return ("Panel", Panel);
        yield return ("Panel2", Panel2);
        yield return ("Rail", Rail);
        yield return ("Sunken", Sunken);
        yield return ("Control", Control);
        yield return ("ControlAlt", ControlAlt);
        yield return ("Track", Track);
        yield return ("Divider", Divider);
        yield return ("Hairline", Hairline);
        yield return ("Hover", Hover);
        yield return ("Text", Text);
        yield return ("TextBright", TextBright);
        yield return ("TextSoft", TextSoft);
        yield return ("TextDim", TextDim);
        yield return ("TextMuted", TextMuted);
    }
}

/// <summary>The twelve accent choices, with names for their tooltips.</summary>
public static class Accents
{
    public static IReadOnlyList<(string Name, string Hex)> All { get; } = new[]
    {
        ("Crimson", "#FF4B52"),
        ("Ember", "#FF7A45"),
        ("Amber", "#FFC53D"),
        ("Mint", "#4BD47B"),
        ("Teal", "#2FD4A8"),
        ("Sky", "#45B7FF"),
        ("Indigo", "#5B7CFA"),
        ("Violet", "#9B5BFA"),
        ("Pink", "#FF5BC8"),
        ("Silver", "#C3CDDD"),
        // Deep and pale blues. Sky sits between them at #45B7FF, so these are
        // spaced far enough either side to be distinguishable on the row.
        ("Deep blue", "#1E5AA8"),
        ("Ice blue", "#8FD4FF")
    };

    /// <summary>The name for a hex, or the hex itself when it is a custom one.</summary>
    public static string NameOf(string hex) =>
        All.FirstOrDefault(a => string.Equals(a.Hex, hex, StringComparison.OrdinalIgnoreCase)).Name
        ?? hex.ToUpperInvariant();
}
