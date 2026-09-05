using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace JinxyMac.Core;

/// <summary>
/// Where a kit's picture comes from when the app has not got one yet.
/// </summary>
/// <remarks>
/// The install ships a picture for every kit on the roster, so on a normal
/// launch none of this runs. It is what covers the gap afterwards: a kit added
/// to the roster in an update, or one whose picture failed to install. Fetching
/// was once the only source and left every kit blank when the network was not
/// there, which is exactly why the pictures are shipped now.
///
/// Only the naming and parsing live here. The request itself needs a network
/// and is in <see cref="KitArtFetch"/>.
/// </remarks>
public static class KitArt
{
    public const string Wiki = "https://robloxbedwars.fandom.com";

    /// <summary>Titles the wiki files a kit under, best first.</summary>
    /// <remarks>
    /// Several kits share a name with an item, and the wiki disambiguates those
    /// with a "(kit)" suffix — the plain "Ember.png" is a lump of rock, not the
    /// character. Asking for the suffixed name first and falling back means new
    /// clashes are handled without anyone noticing them one at a time.
    /// </remarks>
    public static IEnumerable<string> TitlesFor(string kit)
    {
        yield return $"File:{kit}_(kit).png";
        yield return $"File:{kit}.png";

        // The game labels some kits with a bracketed qualifier the wiki does
        // not use — "Zeno (Wizard)" is filed as plain "Zeno". Trying the name
        // without it catches those without a list of aliases to keep current.
        string plain = WithoutQualifier(kit);

        if (plain != kit && plain.Length > 0) yield return $"File:{plain}.png";
    }

    /// <summary>A kit's name with any trailing bracketed qualifier removed.</summary>
    public static string WithoutQualifier(string kit)
    {
        int open = kit.LastIndexOf('(');

        if (open <= 0 || !kit.TrimEnd().EndsWith(")")) return kit;

        // Underscores trimmed as well as spaces: the name arrives with the
        // spaces already swapped for underscores, so "Zeno_(Wizard)" would
        // otherwise leave a trailing one and ask for "Zeno_.png".
        return kit[..open].Trim().TrimEnd('_', ' ').Replace(' ', '_');
    }

    /// <summary>One API call asking about many files at once.</summary>
    public static string QueryUrl(IEnumerable<string> titles) =>
        $"{Wiki}/api.php?action=query&prop=imageinfo&iiprop=url&format=json&titles="
        + Uri.EscapeDataString(string.Join("|", titles));

    /// <summary>
    /// Reads the wiki's reply into a kit name and a picture address.
    /// </summary>
    /// <remarks>
    /// Titles come back with spaces where the request had underscores, and a
    /// page the wiki has never heard of comes back with no imageinfo at all
    /// rather than as an error — so both are handled here rather than being
    /// allowed to look like a failed download.
    /// </remarks>
    public static Dictionary<string, string> ReadUrls(string json)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            JsonElement root = JsonDocument.Parse(json).RootElement;

            if (!root.TryGetProperty("query", out JsonElement query)
                || !query.TryGetProperty("pages", out JsonElement pages))
            {
                return found;
            }

            foreach (JsonProperty page in pages.EnumerateObject())
            {
                if (!page.Value.TryGetProperty("title", out JsonElement titleElement)) continue;
                if (!page.Value.TryGetProperty("imageinfo", out JsonElement info)) continue;
                if (info.ValueKind != JsonValueKind.Array || info.GetArrayLength() == 0) continue;

                string? url = info[0].TryGetProperty("url", out JsonElement u) ? u.GetString() : null;
                if (url == null || !IsWikiImage(url)) continue;

                string kit = KitFromTitle(titleElement.GetString() ?? "");
                if (kit.Length == 0) continue;

                // The suffixed title is asked for first and is the better
                // picture, so it wins if both come back.
                if (!found.ContainsKey(kit) || (titleElement.GetString() ?? "").Contains("(kit)"))
                    found[kit] = url;
            }
        }
        catch
        {
            // A malformed reply is no pictures, not a crash. This runs in the
            // background and nobody asked for it.
        }

        return found;
    }

    /// <summary>"File:Ember (kit).png" becomes "Ember".</summary>
    public static string KitFromTitle(string title)
    {
        string name = title;

        if (name.StartsWith("File:", StringComparison.OrdinalIgnoreCase)) name = name[5..];
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        // Spaces, because that is how the API returns what was sent with
        // underscores.
        name = name.Replace(" (kit)", "", StringComparison.OrdinalIgnoreCase)
                   .Replace("_(kit)", "", StringComparison.OrdinalIgnoreCase)
                   .Replace('_', ' ');

        return name.Trim();
    }

    /// <summary>
    /// Whether a picture address is one of the wiki's own.
    /// </summary>
    /// <remarks>
    /// The address arrives in a JSON reply and the app then writes whatever is
    /// behind it to disk, so the host is pinned rather than trusted — the same
    /// rule the updater follows before running an installer.
    /// </remarks>
    public static bool IsWikiImage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("static.wikia.nocookie.net", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".fandom.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The extension for what was actually downloaded, read from its first bytes.
    /// </summary>
    /// <remarks>
    /// Not decoration. Every kit picture was saved as ".png" regardless of what
    /// came back, and what comes back from this wiki is WebP — so the whole
    /// folder was WebP wearing a PNG extension. Windows decodes it anyway, by
    /// sniffing rather than by trusting the name, but down a path that behaves
    /// differently: it discarded the transparency, and every kit drew as a solid
    /// coloured rectangle.
    ///
    /// The name is fixed at the point the bytes arrive, because that is the only
    /// place the truth is known. An empty result means it is not a picture this
    /// app can show, and nothing should be written at all.
    /// </remarks>
    public static string ExtensionFor(byte[] bytes)
    {
        if (bytes.Length < 12) return "";

        if (bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G')
            return ".png";

        // "RIFF" .... "WEBP" — the size field sits between the two.
        if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
            return ".webp";

        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";

        if (bytes[0] == 'B' && bytes[1] == 'M') return ".bmp";

        return "";
    }

    /// <summary>How many files to ask about in one request.</summary>
    /// <remarks>
    /// The wiki accepts fifty titles a call and each kit costs two — the
    /// suffixed name and the plain one — so twenty-four kits fit.
    /// </remarks>
    public const int BatchSize = 24;

    /// <summary>The kits with no picture yet, in batches the API will accept.</summary>
    public static List<List<string>> Batches(IEnumerable<string> kits)
    {
        var batches = new List<List<string>>();
        var current = new List<string>();

        foreach (string kit in kits)
        {
            current.Add(kit);

            if (current.Count < BatchSize) continue;

            batches.Add(current);
            current = new List<string>();
        }

        if (current.Count > 0) batches.Add(current);

        return batches;
    }
}
