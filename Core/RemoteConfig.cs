using System.Net.Http;
using System.Text.Json;

namespace JinxyMac.Core;

/// <summary>
/// Settings that can be changed without shipping a new build.
/// </summary>
/// <remarks>
/// <b>What this is for.</b> Some problems are a wrong number rather than wrong
/// code — the hit-fix floor being 15 ms when it should be 12. Those can be
/// fixed by editing one file on GitHub, and every copy picks it up next time it
/// opens. That is worth more here than on Windows: this app is unsigned, so
/// replacing the bundle makes it a new app to Accessibility and the user has to
/// go and re-grant it by hand. A release is expensive for them; an edit is not.
///
/// <b>What this is not for, and cannot do.</b> It cannot fix code. A crash, a
/// wrong calculation, a broken layout — none of those are a number, and no
/// amount of remote configuration reaches them. Anyone promising otherwise is
/// describing downloading and running new code on other people's machines,
/// which is indistinguishable from what malware does.
///
/// So the rule is strict and worth stating: <b>this file can only move numbers
/// within bounds the shipped build already agreed to, and turn features off.</b>
/// Every value is clamped by the app, not by the file.
///
/// Everything fails to the shipped defaults — no network, bad JSON, a value out
/// of range, a key nobody recognises. The app must work perfectly with this
/// whole mechanism unreachable, because for anyone offline it is.
/// </remarks>
public sealed class RemoteConfig
{
    /// <summary>Where the config lives — a plain file in the public repository.</summary>
    public static string Url =>
        $"https://raw.githubusercontent.com/{Updater.Owner}/{Updater.Repo}/main/config.json";

    public static bool IsTrusted(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>What is in force. Replaced once at startup, then read-only.</summary>
    public static RemoteConfig Current { get; private set; } = new();

    /// <summary>The shortest press the hit fix will allow, in milliseconds.</summary>
    public double HitFixMinDownMs { get; init; } = ClickTimings.DefaultHitFixMinDownMs;

    /// <summary>The shortest gap between clicks the hit fix will allow.</summary>
    public double HitFixMinUpMs { get; init; } = ClickTimings.DefaultHitFixMinUpMs;

    /// <summary>Whether the recorder may be used at all.</summary>
    /// <remarks>
    /// A switch rather than a number, for the case where the encoder turns out
    /// to be broken on machines nobody could test on. ffmpeg and Screen
    /// Recording have already produced two real bugs here, and turning the
    /// feature off beats leaving people with an app that fails every time they
    /// press Record.
    /// </remarks>
    public bool RecorderEnabled { get; init; } = true;

    /// <summary>Whether the kit wheel may fetch missing kit pictures from the wiki.</summary>
    /// <remarks>
    /// A switch rather than a number, for the same reason <see cref="RecorderEnabled"/>
    /// is one: the fetch reaches a site nobody here controls, and if it ever
    /// starts saving something broken, turning it off beats waiting for
    /// everyone to install a fix.
    /// </remarks>
    public bool KitArtFetchEnabled { get; init; } = true;

    /// <summary>
    /// A short line shown in the app, for saying "known issue, fix coming".
    /// </summary>
    /// <remarks>
    /// Length-capped so a mistake in the file cannot produce a wall of text in
    /// the interface, and it is only ever displayed as text — never parsed,
    /// never used as an address, never run.
    /// </remarks>
    public string Notice { get; init; } = "";

    public const int MaxNoticeLength = 200;

    /// <summary>
    /// Reads a config, clamping every value into a range the app accepts.
    /// </summary>
    /// <remarks>
    /// Clamped rather than validated-and-rejected: a single silly number should
    /// not throw away the rest of the file, and a value pinned to a bound is a
    /// value the app would have accepted anyway. Anything missing keeps the
    /// shipped default, so a config naming one setting changes only that one.
    /// </remarks>
    public static RemoteConfig Parse(string json)
    {
        var fallback = new RemoteConfig();

        try
        {
            JsonElement root = JsonDocument.Parse(json).RootElement;

            if (root.ValueKind != JsonValueKind.Object) return fallback;

            return new RemoteConfig
            {
                HitFixMinDownMs = Clamped(root, "hitFixMinDownMs", fallback.HitFixMinDownMs, 1, 100),
                HitFixMinUpMs = Clamped(root, "hitFixMinUpMs", fallback.HitFixMinUpMs, 1, 100),
                RecorderEnabled = Flag(root, "recorderEnabled", fallback.RecorderEnabled),
                KitArtFetchEnabled = Flag(root, "kitArtFetchEnabled", fallback.KitArtFetchEnabled),
                Notice = Text(root, "notice")
            };
        }
        catch
        {
            return fallback;
        }
    }

    private static double Clamped(JsonElement root, string name, double fallback, double low, double high)
    {
        if (!root.TryGetProperty(name, out JsonElement value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number) return fallback;
        if (!value.TryGetDouble(out double number)) return fallback;
        if (double.IsNaN(number) || double.IsInfinity(number)) return fallback;

        return Math.Clamp(number, low, high);
    }

    private static bool Flag(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static string Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)) return "";
        if (value.ValueKind != JsonValueKind.String) return "";

        string text = (value.GetString() ?? "").Trim();

        return text.Length <= MaxNoticeLength ? text : text[..MaxNoticeLength];
    }

    /// <summary>
    /// Fetches the config and puts it in force. Never throws, never blocks
    /// anything that matters.
    /// </summary>
    public static async Task LoadAsync(CancellationToken token)
    {
        try
        {
            if (!IsTrusted(Url)) return;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("JinxyMac");

            Current = Parse(await http.GetStringAsync(Url, token).ConfigureAwait(false));
        }
        catch
        {
            // Offline, rate limited, or no config published. The shipped
            // defaults are already in force and are known to work.
        }
    }
}
