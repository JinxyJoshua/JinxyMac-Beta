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
        Backdrop: "#0F141D",
        Panel: "#1A2230",
        Panel2: "#151C27",
        Rail: "#151B25",
        Sunken: "#101620",
        Control: "#202A39",
        ControlAlt: "#1E2634",
        Track: "#2A3445",
        Divider: "#232E3E",
        Hairline: "#1AFFFFFF",
        Hover: "#0DFFFFFF",
        Text: "#E9EDF4",
        TextBright: "#FFFFFF",
        TextSoft: "#AAB4C5",
        TextDim: "#C3CDDD",
        TextMuted: "#8D98AA");

    /// <summary>
    /// The same roles inverted.
    /// </summary>
    /// <remarks>
    /// Not a mechanical inversion of the dark values. Light interfaces need the
    /// panels lighter than the page rather than darker — the raised surface is
    /// the bright one — so Panel goes to white while Backdrop stays grey, which
    /// is the opposite of how the dark set is ordered.
    /// </remarks>
    public static Palette Light { get; } = new(
        Backdrop: "#EEF1F6",
        Panel: "#FFFFFF",
        Panel2: "#F6F8FC",
        Rail: "#E4E9F1",
        Sunken: "#EDF1F7",
        Control: "#E1E7F0",
        ControlAlt: "#D8E0EB",
        Track: "#C4CEDD",
        Divider: "#DCE3EC",
        Hairline: "#14000000",
        Hover: "#0D000000",
        Text: "#16202E",
        TextBright: "#0B1219",
        TextSoft: "#4A5666",
        TextDim: "#33404F",
        TextMuted: "#6B7787");

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
