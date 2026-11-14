using System;
using System.Collections.Generic;
using System.Linq;

namespace JinxyMac.Core;

/// <summary>One entry in the sidebar: its id, and what it is called by default.</summary>
/// <param name="Id">
/// Stable and never shown. The saved layout is written in these, so renaming what
/// a page is called on screen cannot scramble somebody's saved order.
/// </param>
public readonly record struct NavEntry(string Id, string DefaultLabel);

/// <summary>
/// The sidebar as the user has arranged it: order, what is hidden, what is
/// renamed.
/// </summary>
/// <remarks>
/// All of it pure, over plain lists, with no window anywhere near it. That is
/// what makes the awkward half testable: an order saved by an older build, read
/// by a newer one that has pages the old one never had and has dropped one the
/// old one did.
///
/// <para>The rule throughout is that the app as it is now decides what exists,
/// and the saved layout only decides arrangement. A saved layout can therefore
/// never hide a new feature by not mentioning it, nor resurrect a page that has
/// been removed.</para>
/// </remarks>
public static class NavLayout
{
    /// <summary>
    /// The longest a renamed entry may be.
    /// </summary>
    /// <remarks>
    /// The sidebar is a fixed width, and a longer name does not wrap — it is
    /// simply cut off mid-word, which looks like a bug rather than like a limit.
    /// Cutting it here means the editor can show the real result as it is typed.
    /// </remarks>
    public const int MaxLabelLength = 22;

    /// <summary>
    /// The order to show, merging a saved order with the pages that exist now.
    /// </summary>
    /// <remarks>
    /// Saved entries first, in their saved order, skipping any that no longer
    /// exist and any repeated by a hand-edited file. Then anything the saved
    /// order never mentioned, in the app's own order, at the end.
    ///
    /// <para>New pages landing at the bottom is deliberate. The alternative —
    /// slotting each one next to where it sits by default — moves somebody's
    /// arrangement around on their behalf, and "new things appear at the bottom"
    /// is a rule a person can learn in one go.</para>
    /// </remarks>
    public static List<string> Reconcile(
        IEnumerable<string>? saved, IReadOnlyList<NavEntry> actual)
    {
        var exists = new HashSet<string>(actual.Select(e => e.Id), StringComparer.Ordinal);

        var order = new List<string>(actual.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);

        if (saved != null)
            foreach (string id in saved)
                if (exists.Contains(id) && placed.Add(id))
                    order.Add(id);

        foreach (NavEntry entry in actual)
            if (placed.Add(entry.Id))
                order.Add(entry.Id);

        return order;
    }

    /// <summary>
    /// What to actually show, with the hidden entries taken out.
    /// </summary>
    /// <remarks>
    /// Hiding everything leaves the whole sidebar showing instead. The editor
    /// will not let the last entry be hidden, so this only happens to a
    /// hand-edited settings file — and an app with no navigation at all would be
    /// unusable in a way the user could not undo from inside it.
    /// </remarks>
    public static List<string> Visible(
        IReadOnlyList<string> order, IEnumerable<string>? hidden)
    {
        if (hidden == null) return new List<string>(order);

        var hide = new HashSet<string>(hidden, StringComparer.Ordinal);

        var shown = order.Where(id => !hide.Contains(id)).ToList();

        return shown.Count == 0 ? new List<string>(order) : shown;
    }

    /// <summary>Whether hiding this entry would leave nothing showing.</summary>
    public static bool IsLastShown(
        IReadOnlyList<string> order, IEnumerable<string>? hidden, string id)
    {
        var hide = new HashSet<string>(hidden ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

        return order.Count(x => !hide.Contains(x)) <= 1 && !hide.Contains(id);
    }

    /// <summary>
    /// What an entry is called: the user's name for it, or the built-in one.
    /// </summary>
    /// <remarks>
    /// A stored name that is blank, or that has been cut to nothing, falls back
    /// to the default rather than leaving a nameless row in the sidebar.
    /// </remarks>
    public static string Label(
        string id, string defaultLabel, IReadOnlyDictionary<string, string>? labels)
    {
        if (labels == null || !labels.TryGetValue(id, out string? custom)) return defaultLabel;

        string cleaned = Shorten(custom);

        return cleaned.Length == 0 ? defaultLabel : cleaned;
    }

    /// <summary>
    /// Tidies a typed name, or returns null when it is not worth storing.
    /// </summary>
    /// <remarks>
    /// Null for blank, and null for a name that matches the default — so
    /// clearing the box, or typing the original name back, removes the override
    /// rather than storing a rename that renames nothing.
    /// </remarks>
    public static string? CleanLabel(string? typed, string defaultLabel)
    {
        string cleaned = Shorten(typed ?? "");

        return cleaned.Length == 0 || cleaned == defaultLabel ? null : cleaned;
    }

    private static string Shorten(string text)
    {
        // Collapsed as well as trimmed: a name pasted out of a document can
        // arrive with a newline or a run of spaces in it, and the sidebar would
        // render that as a gap rather than as a word.
        string tidy = string.Join(' ',
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return tidy.Length <= MaxLabelLength ? tidy : tidy[..MaxLabelLength].TrimEnd();
    }

    /// <summary>
    /// Moves an entry up or down, and says whether anything moved.
    /// </summary>
    /// <remarks>
    /// Moving the top entry up, or the bottom one down, does nothing and reports
    /// nothing — so the caller can skip saving a file that would be identical,
    /// and the buttons at the ends simply have no effect rather than wrapping
    /// the list around.
    /// </remarks>
    public static bool Move(List<string> order, string id, int delta)
    {
        int from = order.IndexOf(id);
        if (from < 0 || delta == 0) return false;

        int to = Math.Clamp(from + delta, 0, order.Count - 1);
        if (to == from) return false;

        order.RemoveAt(from);
        order.Insert(to, id);

        return true;
    }
}
