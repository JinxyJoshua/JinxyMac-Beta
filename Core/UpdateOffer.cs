namespace JinxyMac.Core;

/// <summary>
/// The words the launch-time update prompt shows, decided independently of
/// the window that displays them.
/// </summary>
/// <remarks>
/// This exists because the wording is the one part of the offer that is
/// worth getting right on its own, away from Avalonia: the headline, and how
/// much of a release's notes actually fit in a small window before they are
/// cut rather than left to overflow it. The warning text is fixed rather
/// than built, and is exactly what the Settings page's manual update box
/// already says (<c>UpdateWarningText</c> in <c>MainWindow.axaml</c>) — see
/// <see cref="Updater"/>'s own remarks for why the update can never be
/// silent: replacing the bundle makes this a new app to Accessibility and
/// Screen Recording, and the person who just updated is the one who finds
/// out the hard way if nothing says so up front.
/// </remarks>
public static class UpdateOffer
{
    /// <summary>
    /// What installing costs, in the one sentence the offer must not omit.
    /// </summary>
    public const string AccessibilityWarning =
        "Installing replaces the app, and because this build is unsigned macOS will treat " +
        "it as a new one — you will have to grant Accessibility again afterwards, or " +
        "clicking will silently stop working.";

    /// <summary>The one-line summary of what is on offer.</summary>
    public static string Headline(Available update) =>
        $"{update.Version} is available  ·  {update.SizeText}";

    /// <summary>
    /// Trims release notes to a length a small window can show whole.
    /// </summary>
    /// <remarks>
    /// Prefers to cut at the last line break at or before the limit, then
    /// the last word break, so a truncated note still reads as whole lines
    /// or whole words with an ellipsis after them rather than a word cut in
    /// half. Only falls back to a hard cut at the limit when neither exists
    /// in range — a single overlong line with no spaces, which is the one
    /// case where there is nothing better to cut on.
    /// </remarks>
    /// <param name="notes">The release body, as GitHub returns it.</param>
    /// <param name="maxLength">
    /// The longest text the caller wants back, ellipsis included.
    /// </param>
    public static string Notes(string notes, int maxLength = 500)
    {
        notes = notes.Trim();
        if (notes.Length <= maxLength) return notes;

        int cut = notes.LastIndexOf('\n', maxLength - 1);
        if (cut < 0) cut = notes.LastIndexOf(' ', maxLength - 1);
        if (cut < 0) cut = maxLength;

        return notes[..cut].TrimEnd() + "…";
    }
}
