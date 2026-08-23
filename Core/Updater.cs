using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace JinxyMac.Core;

/// <summary>What the release feed says is available.</summary>
public readonly record struct Available(string Version, string Notes, string Url, long Bytes)
{
    public string SizeText => $"{Bytes / 1024.0 / 1024.0:0} MB";
}

/// <summary>
/// Checks GitHub for a newer build, and can install one.
/// </summary>
/// <remarks>
/// The app is unsigned, and that shapes everything here.
///
/// It means the update cannot be silent. macOS identifies an unsigned app for
/// permission purposes by the hash of what is on disk, so replacing the bundle
/// makes it a different app as far as Accessibility and Screen Recording are
/// concerned, and both have to be granted again. An update that quietly broke
/// clicking until someone visited System Settings would be worse than no
/// update at all — so it is offered, explained, and never automatic.
///
/// It also means the download does not have to fight Gatekeeper. Quarantine is
/// attached by the application doing the downloading; a plain HTTP client sets
/// no such flag, so an update installed this way skips the right-click-to-open
/// dance the first download needed.
/// </remarks>
public static class Updater
{
    /// <summary>The running version, and the single place it is written down.</summary>
    /// <remarks>
    /// build-mac.sh reads this and writes it into Info.plist, so the number in
    /// the bundle cannot drift from the number the updater compares against.
    /// </remarks>
    public const string Version = "1.0.8";

    private const string Feed =
        "https://api.github.com/repos/JinxyJoshua/JinxyMac-Beta/releases/latest";

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };

        // GitHub rejects an API request with no User-Agent outright. The same
        // omission cost the clip uploader an afternoon.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"JinxyMac/{Version}");

        return client;
    }

    /// <summary>
    /// Asks the release feed what the newest build is.
    /// </summary>
    /// <returns>Null when this is already the newest, or when the check failed.</returns>
    /// <remarks>
    /// A failed check returns null rather than throwing. Being offline is not a
    /// problem worth a dialog, and an autoclicker that complains about the
    /// network on launch would be insufferable.
    /// </remarks>
    public static async Task<Available?> CheckAsync(CancellationToken token = default)
    {
        try
        {
            string json = await Client.GetStringAsync(Feed, token).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            string version = tag.TrimStart('v').Replace("-beta", "");

            if (!IsNewer(version, Version)) return null;

            foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";

                if (!name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) continue;

                return new Available(
                    version,
                    root.GetProperty("body").GetString() ?? "",
                    asset.GetProperty("browser_download_url").GetString() ?? "",
                    asset.GetProperty("size").GetInt64());
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Compares two dotted versions numerically.
    /// </summary>
    /// <remarks>
    /// Numerically, not as strings, because "1.0.10" sorts before "1.0.9" as
    /// text and the tenth build would never offer itself as an update.
    /// </remarks>
    internal static bool IsNewer(string candidate, string current)
    {
        int[] left = Parts(candidate);
        int[] right = Parts(current);

        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            int a = i < left.Length ? left[i] : 0;
            int b = i < right.Length ? right[i] : 0;

            if (a != b) return a > b;
        }

        return false;
    }

    private static int[] Parts(string version) => version
        .Split('.')
        .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 0)
        .ToArray();

    /// <summary>
    /// Downloads and installs an update, then relaunches.
    /// </summary>
    /// <remarks>
    /// The swap is done by a detached shell script rather than in process, for
    /// the obvious reason: this code lives inside the bundle being replaced.
    ///
    /// The script keeps the old bundle until the new one is in place and puts it
    /// back if anything fails. A half-finished update is the one outcome worse
    /// than no update, because the person it happens to has no working copy left
    /// to report it from.
    /// </remarks>
    /// <returns>An error to show, or null if the relaunch is under way.</returns>
    public static async Task<string?> InstallAsync(Available update, IProgress<double>? progress = null)
    {
        if (!OperatingSystem.IsMacOS()) return "Updating is a macOS path.";

        string bundle = BundlePath();
        if (bundle.Length == 0) return "Cannot find the running app bundle to replace.";

        string work = Path.Combine(Path.GetTempPath(), "JinxyMacUpdate");

        try
        {
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            Directory.CreateDirectory(work);

            string archive = Path.Combine(work, "update.tar.gz");

            await Download(update.Url, archive, update.Bytes, progress).ConfigureAwait(false);

            // Unpacked and checked before the installed copy is touched. A
            // truncated download must fail here, where nothing is lost.
            if (!Run("/usr/bin/tar", new[] { "xzf", archive, "-C", work }))
                return "The update downloaded but would not unpack.";

            string staged = Path.Combine(work, "JinxyMac.app");

            if (!File.Exists(Path.Combine(staged, "Contents", "MacOS", "JinxyMac")))
                return "The update did not contain a usable app.";

            string script = WriteSwapScript(work, staged, bundle);

            // Detached, so it outlives the process it is about to replace.
            Process.Start(new ProcessStartInfo("/bin/sh", script) { UseShellExecute = false });

            return null;
        }
        catch (Exception error)
        {
            return "Update failed: " + error.Message;
        }
    }

    private static async Task Download(string url, string path, long total, IProgress<double>? progress)
    {
        using HttpResponseMessage response =
            await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using FileStream file = File.Create(path);

        var buffer = new byte[81920];
        long done = 0;
        int read;

        while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);

            done += read;
            if (total > 0) progress?.Report(done / (double)total);
        }
    }

    /// <summary>
    /// The script that does the replacing, once this process is gone.
    /// </summary>
    /// <remarks>
    /// ditto rather than cp or mv: it is the tool that preserves an app
    /// bundle's permissions and extended attributes, and a bundle copied with
    /// anything else can arrive without its executable bit.
    /// </remarks>
    private static string WriteSwapScript(string work, string staged, string bundle)
    {
        string path = Path.Combine(work, "swap.sh");
        string backup = bundle + ".old";

        string script = string.Join('\n',
            "#!/bin/sh",
            "# Waits for Jinxy to quit, swaps the bundle, puts the old one back",
            "# if the new one does not land, then reopens whichever survived.",
            "",
            "for _ in $(seq 1 50); do",
            "    pgrep -x JinxyMac >/dev/null 2>&1 || break",
            "    sleep 0.2",
            "done",
            "",
            $"rm -rf \"{backup}\"",
            $"mv \"{bundle}\" \"{backup}\" || exit 1",
            "",
            $"if /usr/bin/ditto \"{staged}\" \"{bundle}\"; then",
            $"    rm -rf \"{backup}\"",
            "else",
            $"    rm -rf \"{bundle}\"",
            $"    mv \"{backup}\" \"{bundle}\"",
            "fi",
            "",
            $"/usr/bin/open \"{bundle}\"",
            $"rm -rf \"{work}\" 2>/dev/null");

        File.WriteAllText(path, script);

        return path;
    }

    /// <summary>The .app this code is running from, or empty if it is not in one.</summary>
    private static string BundlePath()
    {
        // Contents/MacOS/<arch>/ — three levels up from the binary's folder.
        string here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        for (DirectoryInfo? at = new(here); at != null; at = at.Parent)
        {
            if (at.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return at.FullName;
        }

        return "";
    }

    private static bool Run(string command, string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            foreach (string argument in arguments) info.ArgumentList.Add(argument);

            using Process? process = Process.Start(info);
            if (process == null) return false;

            process.WaitForExit(120_000);

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
