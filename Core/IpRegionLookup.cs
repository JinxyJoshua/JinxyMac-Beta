using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace JinxyMac.Core;

/// <summary>
/// Turns a server IP into a <see cref="ServerRegion"/>, and remembers what it
/// learns so the same server is never looked up twice.
/// </summary>
/// <remarks>
/// The free ip-api endpoint is keyless with a generous per-minute limit, which
/// suits this exactly: the only value ever sent is the game server's public
/// address — never anything of the user's — and a lookup happens at most once per
/// server joined, which is rare next to the limit. Results are cached for the
/// life of the app because a server's location does not move.
///
/// <para>Every failure path returns null rather than throwing: a blocked request,
/// a rate limit, an address the service cannot place. The page then shows no
/// region, which is the honest outcome.</para>
/// </remarks>
public sealed class IpRegionLookup
{
    private const string Fields = "status,message,country,countryCode,regionName,city,lat,lon";

    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(6) };

    private readonly HttpClient _http;
    private readonly Dictionary<string, ServerRegion> _cache = new();

    public IpRegionLookup(HttpClient? http = null) => _http = http ?? Shared;

    /// <summary>The URL a lookup of this address goes to.</summary>
    /// <remarks>
    /// Public and separate from the request so a test can assert what is sent —
    /// specifically that a lookup of a server carries the server's address and
    /// nothing else.
    /// </remarks>
    public static string UrlFor(string ip) => $"http://ip-api.com/json/{ip}?fields={Fields}";

    public async Task<ServerRegion?> LookupAsync(string ip, CancellationToken ct = default)
    {
        // An empty address asks ip-api where the *caller* is. This app never wants
        // that: it reports where a game server is, and looking up the user's own
        // location would be collecting something it has no use for.
        if (string.IsNullOrWhiteSpace(ip)) return null;

        lock (_cache)
            if (_cache.TryGetValue(ip, out ServerRegion? cached))
                return cached;

        string json;

        try
        {
            json = await _http.GetStringAsync(UrlFor(ip), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                      or OperationCanceledException)
        {
            return null;
        }

        ServerRegion? region = ServerRegion.FromApiJson(json, ip);

        if (region != null)
            lock (_cache)
                _cache[ip] = region;

        return region;
    }
}
