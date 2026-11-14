using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JinxyMac.Core;

/// <summary>
/// What came of reading somebody's pasted flags.
/// </summary>
/// <remarks>
/// Four outcomes rather than one list, because the whole point is telling people
/// apart from each other: what Roblox will act on, what it will quietly throw
/// away, what would be reckless to write, and what could not be read at all.
/// </remarks>
/// <param name="Supported">On Roblox's allowlist. The only ones worth writing.</param>
/// <param name="Ignored">Read by the client and discarded. Named so the user knows.</param>
/// <param name="Risky">Touches character movement. Refused, with a word about why.</param>
/// <param name="Unreadable">Keys that are not flag names, or values that are not values.</param>
public sealed record FlagImport(
    IReadOnlyList<FastFlag> Supported,
    IReadOnlyList<string> Ignored,
    IReadOnlyList<string> Risky,
    IReadOnlyList<string> Unreadable,
    string? Error = null)
{
    public static FlagImport Failed(string why) =>
        new(Array.Empty<FastFlag>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), why);

    public bool AnythingToApply => Supported.Count > 0;

    public int Total => Supported.Count + Ignored.Count + Risky.Count + Unreadable.Count;

    /// <summary>
    /// The one line the page shows.
    /// </summary>
    /// <remarks>
    /// Written here rather than in the window so it can be checked. It is the
    /// most important sentence in the feature: a preset pasted off a forum is
    /// usually mostly dead flags, and this is where somebody finds that out
    /// instead of believing they have just gained fifty frames.
    /// </remarks>
    public string Summary
    {
        get
        {
            if (Error != null) return Error;
            if (Total == 0) return "Nothing to import.";

            var parts = new List<string>();

            if (Supported.Count > 0) parts.Add($"{Supported.Count} Roblox will use");
            if (Ignored.Count > 0) parts.Add($"{Ignored.Count} it ignores");
            if (Risky.Count > 0) parts.Add($"{Risky.Count} refused");
            if (Unreadable.Count > 0) parts.Add($"{Unreadable.Count} unreadable");

            return Join(parts) + ".";
        }
    }

    private static string Join(List<string> parts) => parts.Count switch
    {
        1 => parts[0],
        2 => $"{parts[0]} and {parts[1]}",
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1]
    };
}

/// <summary>
/// Reads a pasted set of Roblox flags, and says which of them do anything.
/// </summary>
/// <remarks>
/// Presets get passed around as JSON on forums, in Discord and inside other
/// launchers' settings files. Importing one is easy; the useful part is the
/// verdict, because since Roblox's allowlist landed in September 2025 most of
/// what circulates is inert. A preset promising hundreds of frames is usually
/// thirty flags of which four still work.
///
/// <para>This app learned that the expensive way — it shipped a frame-rate
/// setting that did nothing for a year, because writing a flag and reading it
/// back proves only that you wrote it. An import that silently accepted
/// everything would be that same mistake, handed to the user to make
/// repeatedly. So nothing outside <see cref="RobloxFastFlags.Allowlist"/> is
/// written, and everything dropped is named.</para>
///
/// <para>Pure and parser-only: no file is touched here. Deliberately forgiving
/// about the shape of what arrives, since it is pasted by hand out of places
/// that are not careful about JSON.</para>
/// </remarks>
public static class FastFlagImport
{
    /// <summary>
    /// A Roblox flag name: an optional D or S, then F, then what it holds.
    /// </summary>
    /// <remarks>
    /// Covers the real prefixes — FFlag, DFFlag, SFFlag, FInt, DFInt, FString,
    /// DFString, FLog, DFLog. Anything else is somebody's comment, a stray
    /// setting from another program's config, or a typo, and is reported as
    /// unreadable rather than written into Roblox's file.
    /// </remarks>
    private static readonly Regex FlagName =
        new(@"^[DS]?F(Flag|Int|String|Log)[A-Za-z0-9_]+$", RegexOptions.Compiled);

    /// <summary>
    /// Words that mark a flag as touching how a character moves.
    /// </summary>
    /// <remarks>
    /// This is the category that gets accounts banned, and it is also the
    /// category every "god mode" preset is built from. None of these are on
    /// Roblox's allowlist, so they would be dropped anyway — they are called out
    /// separately because it tells the user something worth knowing about where
    /// they got the preset, rather than letting it pass as an ordinary
    /// unsupported flag.
    /// </remarks>
    public static IReadOnlyList<string> RiskyWords { get; } = new[]
    {
        "Physics", "Humanoid", "Gravity", "Walk", "Jump", "HipHeight",
        "Velocity", "Teleport", "Collision"
    };

    public static bool LooksRisky(string name) =>
        RiskyWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>The most flags one paste may carry.</summary>
    /// <remarks>
    /// Presets run to a few dozen. A paste of thousands is somebody's whole
    /// settings dump or an accident, and writing it would bury the file in
    /// entries nobody chose.
    /// </remarks>
    public const int MaxFlags = 300;

    /// <summary>Reads pasted text and sorts the flags in it by what they will do.</summary>
    public static FlagImport Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return FlagImport.Failed("Paste some flags first.");

        string? json = JsonPart(text);

        if (json == null)
            return FlagImport.Failed("That does not look like flags. Paste the whole { } block.");

        Dictionary<string, string?>? entries = ReadEntries(json, out string? error);

        if (entries == null) return FlagImport.Failed(error!);

        if (entries.Count == 0)
            return FlagImport.Failed("No flags in that. Paste the whole { } block.");

        if (entries.Count > MaxFlags)
            return FlagImport.Failed($"That is {entries.Count} flags. {MaxFlags} is the most at once.");

        var supported = new List<FastFlag>();
        var ignored = new List<string>();
        var risky = new List<string>();
        var unreadable = new List<string>();

        foreach ((string name, string? value) in entries)
        {
            if (!FlagName.IsMatch(name)) { unreadable.Add(name); continue; }

            // No usable value: a nested object, a null, or an empty string.
            // Writing "" would put a flag in Roblox's file set to nothing,
            // which is worse than not importing it.
            if (string.IsNullOrEmpty(value)) { unreadable.Add(name); continue; }

            // Checked before the allowlist so the warning is specific, even
            // though the allowlist would drop these anyway.
            if (LooksRisky(name)) { risky.Add(name); continue; }

            if (!RobloxFastFlags.Allowlist.Contains(name)) { ignored.Add(name); continue; }

            supported.Add(new FastFlag(name, value, "Imported."));
        }

        return new FlagImport(supported, ignored, risky, unreadable);
    }

    /// <summary>
    /// The JSON object inside whatever was pasted.
    /// </summary>
    /// <remarks>
    /// People paste a code fence, a forum post, or a sentence of their own
    /// around the block. Taking the outermost braces finds the object in all of
    /// those, and fails on text that has none.
    /// </remarks>
    private static string? JsonPart(string text)
    {
        int open = text.IndexOf('{');
        int close = text.LastIndexOf('}');

        return open < 0 || close <= open ? null : text[open..(close + 1)];
    }

    private static Dictionary<string, string?>? ReadEntries(string json, out string? error)
    {
        error = null;

        // Forgiving on purpose: pasted presets routinely carry // notes and a
        // trailing comma, and refusing those would reject files that every other
        // launcher accepts.
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json, options);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "That is not a set of flags. Paste the whole { } block.";
                return null;
            }

            // Last one wins on a repeated name, which is what a JSON reader does
            // and what someone editing a preset by hand would expect.
            var entries = new Dictionary<string, string?>(StringComparer.Ordinal);

            // A nested object is another launcher's settings file wrapped around
            // the flags. Its name is reported rather than its contents guessed
            // at, which is why ValueOf returns null for one.
            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                entries[property.Name.Trim()] = ValueOf(property.Value);

            return entries;
        }
        catch (JsonException)
        {
            error = "That is not valid JSON. Check for a missing brace or quote.";
            return null;
        }
    }

    /// <summary>
    /// One flag's value as Roblox's file writes it.
    /// </summary>
    /// <remarks>
    /// Everything in <c>ClientAppSettings.json</c> is a string, but presets are
    /// written by hand and arrive with real booleans and bare numbers. Those are
    /// converted rather than rejected — and a boolean becomes "True" or "False"
    /// with the capital, because that is the spelling the client reads.
    /// </remarks>
    private static string? ValueOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()?.Trim(),
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        JsonValueKind.Number => value.TryGetInt64(out long whole)
            ? whole.ToString(CultureInfo.InvariantCulture)
            : value.GetRawText(),
        _ => null
    };
}
