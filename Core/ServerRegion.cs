using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace JinxyMac.Core;

/// <summary>
/// Where a game server is, as a place a person recognises rather than an address.
/// </summary>
/// <remarks>
/// Built from an ip-api.com lookup of the server's UDMUX address. Because that
/// address is a relay (see <see cref="RobloxServerLog"/>), this is the region the
/// server is served from — close to the real host, but not a claim about the
/// exact building.
///
/// <para>The fields are kept separate so the page can show a short headline and a
/// quieter line under it without re-parsing anything.</para>
///
/// <para>There is deliberately no ping here. It cannot be measured from outside
/// the game: Roblox's relay does not answer ICMP, and a TCP handshake against the
/// game port measures the relay rather than the round trip the player feels. A
/// number that looked like latency but was not one would be worse than no number,
/// so this shows where the server is and stops there.</para>
/// </remarks>
public sealed record ServerRegion(
    string Ip,
    string CountryCode,
    string Country,
    string Region,
    string City,
    double Lat = 0,
    double Lon = 0)
{
    /// <summary>The big line: a flag and the city, or the country if there is no city.</summary>
    public string Headline
    {
        get
        {
            string flag = FlagFor(CountryCode);
            string place = City.Length > 0 ? City : Country.Length > 0 ? Country : "Unknown";

            return flag.Length > 0 ? flag + " " + place : place;
        }
    }

    /// <summary>The quiet line: region and country, without repeating the city.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>();

            if (Region.Length > 0 && !Region.Equals(City, StringComparison.OrdinalIgnoreCase))
                parts.Add(Region);

            if (Country.Length > 0 && !Country.Equals(Region, StringComparison.OrdinalIgnoreCase))
                parts.Add(Country);

            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Parses one ip-api.com response, or null if the address could not be placed.
    /// </summary>
    /// <remarks>
    /// The free endpoint answers <c>{"status":"success",...}</c> on a hit and
    /// <c>{"status":"fail","message":"..."}</c> on a miss — a reserved range, say.
    /// A failed status, a missing status and malformed JSON all mean "no region"
    /// rather than an exception; the page then shows nothing, which is honest.
    /// </remarks>
    public static ServerRegion? FromApiJson(string? json, string ip)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("status", out JsonElement status)
                || status.ValueKind != JsonValueKind.String
                || status.GetString() != "success")
                return null;

            return new ServerRegion(
                ip,
                Str(root, "countryCode"),
                Str(root, "country"),
                Str(root, "regionName"),
                Str(root, "city"),
                Num(root, "lat"),
                Num(root, "lon"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? ""
            : "";

    private static double Num(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement e)
        && e.ValueKind == JsonValueKind.Number
        && e.TryGetDouble(out double d)
            ? d
            : 0;

    /// <summary>
    /// The flag emoji for a two-letter country code, or empty if it is not two
    /// A–Z letters.
    /// </summary>
    /// <remarks>
    /// A flag emoji is its country's two letters written as regional-indicator
    /// symbols, which sit 0x1F1E6 above plain A. Anything that is not exactly two
    /// letters — an empty code, a stray value — yields no flag rather than a
    /// wrong one.
    ///
    /// <para>These render in colour on macOS, where the system emoji font has the
    /// flags. The Windows app had to draw its own, because Windows ships no flag
    /// glyphs at all; here the platform does it properly.</para>
    /// </remarks>
    public static string FlagFor(string? countryCode)
    {
        if (countryCode is not { Length: 2 }) return "";

        char a = char.ToUpperInvariant(countryCode[0]);
        char b = char.ToUpperInvariant(countryCode[1]);

        if (a is < 'A' or > 'Z' || b is < 'A' or > 'Z') return "";

        const int baseCodePoint = 0x1F1E6; // regional indicator 'A'

        return new StringBuilder()
            .Append(char.ConvertFromUtf32(baseCodePoint + (a - 'A')))
            .Append(char.ConvertFromUtf32(baseCodePoint + (b - 'A')))
            .ToString();
    }
}
