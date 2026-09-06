using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
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
    public const string Version = "1.2.3";

    /// <summary>
    /// The repository this build updates from, and reads its config from.
    /// </summary>
    /// <remarks>
    /// Named once. Two places spelling out the same repository is two places
    /// to get it wrong, and the failure is silent — an updater pointed at the
    /// wrong repo simply never finds a release.
    /// </remarks>
    public const string Owner = "JinxyJoshua";
    public const string Repo = "JinxyMac-Beta";

    private const string Feed =
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    private static readonly HttpClient Client = CreateClient();

    /// <summary>How long the launch-time check may take before it is dropped.</summary>
    /// <remarks>
    /// The shared client's own timeout is fifteen minutes, which is right for
    /// a hundred-megabyte download and badly wrong for a question. Nobody
    /// asked for this check on launch, so it must never be what someone
    /// notices about opening the app — and a stalled network without this
    /// deadline does exactly that: the check hangs, and the offer window
    /// finally appears minutes later, over whatever they moved on to. Given
    /// up on rather than left hanging, matching the eight seconds the Windows
    /// build allows its own launch check.
    /// </remarks>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);

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
        // Linked, not a bare CancelAfter: a caller's own token still has to
        // cancel this, and the deadline is an extra reason to stop rather than
        // a replacement for theirs.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(CheckTimeout);

        try
        {
            string json = await Client.GetStringAsync(Feed, deadline.Token).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            string version = tag.TrimStart('v').Replace("-beta", "");

            if (!IsNewer(version, Version)) return null;

            // Two downloads since 1.2.3, one per architecture. The match is
            // required, not preferred: there is no safe fallback, because the
            // two mismatches are not equally survivable. An Intel build on
            // Apple silicon runs under Rosetta; an Apple silicon build on an
            // Intel Mac does not run at all, and offering one would replace a
            // working app with a bundle that cannot open. Finding nothing that
            // matches means no update, which is the outcome to prefer.
            var assets = root.GetProperty("assets").EnumerateArray()
                .Where(a => (a.GetProperty("name").GetString() ?? "")
                    .EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                .Where(a => IsForThisMac(a.GetProperty("name").GetString() ?? ""));

            foreach (JsonElement asset in assets)
            {
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";

                // A reply is JSON from an API call that host-pinning already
                // protects, but the address inside it is what actually gets
                // downloaded and unpacked over the app bundle — worth checking
                // on its own rather than trusting the document that carried it.
                if (!IsTrustedAssetUrl(url)) continue;

                return new Available(
                    version,
                    root.GetProperty("body").GetString() ?? "",
                    url,
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
    /// Whether a release asset is the build for the architecture this process
    /// is running as.
    /// </summary>
    /// <remarks>
    /// The Intel tarball is the one that says so in its name; the Apple
    /// silicon build keeps the plain name it has always had, so that a client
    /// shipped before this method existed — every 1.0.8 and 1.2.2 install —
    /// still finds something it can use in the first asset it looks at.
    ///
    /// That last point is why the Intel asset is <c>JinxyMac-mac_intel.tar.gz</c>
    /// and not <c>-intel</c>: GitHub lists a release's assets alphabetically,
    /// <c>'-'</c> sorts before <c>'.'</c>, and an older client takes the first
    /// tarball it sees. Named with a hyphen, the Intel build would be handed
    /// to every Apple silicon user still on an old build. An underscore sorts
    /// after the dot and puts them back in the right order.
    ///
    /// Matching on the name rather than on anything inside the file because
    /// this runs against a JSON listing, before a single byte is downloaded.
    ///
    /// A process running under Rosetta reports itself as x64, and taking it at
    /// its word is right: it should be offered the x64 build it is already
    /// running, not moved to a different architecture by an update.
    /// </remarks>
    internal static bool IsForThisMac(string assetName)
    {
        bool intel = assetName.Contains("intel", StringComparison.OrdinalIgnoreCase);

        return RuntimeInformation.ProcessArchitecture == Architecture.X64 ? intel : !intel;
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
    /// Whether a release asset address is one GitHub itself would have handed
    /// back.
    /// </summary>
    /// <remarks>
    /// Pinned the same way <see cref="KitArt.IsWikiImage"/> pins the wiki's —
    /// the JSON that carries this address comes from GitHub's API, but the
    /// address itself is just a string in that document until something
    /// checks it, and this is the one that gets downloaded and unpacked over
    /// the running app. github.com is what the API hands back; the
    /// githubusercontent.com suffix covers the CDN host a real download
    /// redirects to.
    /// </remarks>
    internal static bool IsTrustedAssetUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Downloads and installs an update, then relaunches.
    /// </summary>
    /// <remarks>
    /// The swap is done by a detached shell script rather than in process, for
    /// the obvious reason: this code lives inside the bundle being replaced.
    ///
    /// The new bundle is fully copied into place next to the old one — on the
    /// same volume — before either is touched, so the swap itself is two
    /// renames rather than a copy. Renaming is atomic; a copy of a
    /// several-hundred-megabyte bundle is not, and the live path must never
    /// sit empty for however long that copy takes. The script puts the old
    /// bundle back if the second rename fails. A half-finished update is the
    /// one outcome worse than no update, because the person it happens to has
    /// no working copy left to report it from.
    /// </remarks>
    /// <returns>An error to show, or null if the relaunch is under way.</returns>
    public static async Task<string?> InstallAsync(Available update, IProgress<double>? progress = null)
    {
        if (!OperatingSystem.IsMacOS()) return "Updating is a macOS path.";

        // Checked here too, not just where the feed is parsed: this is the
        // call that actually downloads and unpacks it over the running app,
        // and it should not have to trust that its caller checked first.
        if (!IsTrustedAssetUrl(update.Url))
            return "The update address was not a GitHub release asset.";

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
            //
            // ArgumentList, not the Arguments string: $TMPDIR (where the
            // script lives) can contain a space, and a raw arguments string
            // would split on it and hand /bin/sh two words instead of a path.
            var swap = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            swap.ArgumentList.Add(script);
            Process.Start(swap);

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
    /// ditto rather than cp or mv to stage the copy: it is the tool that
    /// preserves an app bundle's permissions and extended attributes, and a
    /// bundle copied with anything else can arrive without its executable bit.
    ///
    /// The copy lands at <c>bundle.new</c>, a sibling of the live bundle, not
    /// on top of it. That is what makes the actual swap two renames instead of
    /// a copy: <c>mv</c> between two paths in the same directory is a same-
    /// volume rename, and a rename is atomic — the live path is never briefly
    /// missing the way it would be if <c>ditto</c> wrote straight over it.
    /// Only once the full copy has landed does anything at the live path move.
    ///
    /// Every path here is attacker-shaped as far as the shell is concerned —
    /// it is wherever the user chose to put the app — so each one is wrapped
    /// with <see cref="ShellQuote"/> rather than bare double quotes, which stop
    /// nothing: a folder named with a backtick or a "$(...)" would otherwise
    /// run as a command inside this script.
    /// </remarks>
    private static string WriteSwapScript(string work, string staged, string bundle)
    {
        string path = Path.Combine(work, "swap.sh");
        string fresh = bundle + ".new";
        string backup = bundle + ".old";

        string script = string.Join('\n',
            "#!/bin/sh",
            "# Waits for Jinxy to quit, copies the new bundle in next to the old",
            "# one, then swaps both with a pair of same-volume renames so the",
            "# live path is never missing longer than those two renames take.",
            "# Puts the old bundle back if the second rename fails, then",
            "# reopens whichever survived.",
            "",
            "for _ in $(seq 1 50); do",
            "    pgrep -x JinxyMac >/dev/null 2>&1 || break",
            "    sleep 0.2",
            "done",
            "",
            "# Still running after ten seconds: swapping now would replace the",
            "# directory a live process is executing from. Leave it alone.",
            "if pgrep -x JinxyMac >/dev/null 2>&1; then",
            "    exit 1",
            "fi",
            "",
            $"rm -rf {ShellQuote(fresh)} {ShellQuote(backup)}",
            "",
            "# Fully staged beside the live bundle, on its volume, before",
            "# anything at the live path is touched. A truncated or interrupted",
            "# copy fails here, where the original is still whole.",
            $"/usr/bin/ditto {ShellQuote(staged)} {ShellQuote(fresh)} || {{ rm -rf {ShellQuote(fresh)}; exit 1; }}",
            "",
            $"mv {ShellQuote(bundle)} {ShellQuote(backup)} || {{ rm -rf {ShellQuote(fresh)}; exit 1; }}",
            "",
            $"if mv {ShellQuote(fresh)} {ShellQuote(bundle)}; then",
            $"    rm -rf {ShellQuote(backup)}",
            "else",
            $"    mv {ShellQuote(backup)} {ShellQuote(bundle)}",
            $"    rm -rf {ShellQuote(fresh)}",
            "fi",
            "",
            $"/usr/bin/open {ShellQuote(bundle)}",
            $"rm -rf {ShellQuote(work)} 2>/dev/null");

        File.WriteAllText(path, script);

        return path;
    }

    /// <summary>
    /// Wraps a path in single quotes so <c>/bin/sh</c> treats it as one
    /// literal argument.
    /// </summary>
    /// <remarks>
    /// Single quotes, not double: double quotes still let <c>$</c>, backticks
    /// and backslashes through, and every path this script embeds is a
    /// filesystem path the user chose, not a literal this code wrote. Single
    /// quotes suppress all of that — the only character they cannot contain is
    /// another single quote, handled the standard POSIX way: close the quote,
    /// emit an escaped one, reopen it.
    /// </remarks>
    internal static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";

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

    /// <summary>
    /// Runs a command to completion, the same shape as <c>Ffmpeg.Run</c> in
    /// the Capture namespace.
    /// </summary>
    /// <remarks>
    /// Both streams are drained before the wait, not after: left unread, a
    /// child that writes enough of either — <c>tar</c> on a corrupt archive,
    /// or one with extended-attribute warnings, easily does — fills the pipe
    /// and blocks forever, which is a hang <c>WaitForExit</c> never gets the
    /// chance to time out on. A timeout that does land kills the process tree
    /// rather than falling through: reading <c>ExitCode</c> on a process that
    /// has not exited throws, and letting that reach the catch below would
    /// report a plain failure while leaving <c>tar</c> running.
    /// </remarks>
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

            Task<string> error = process.StandardError.ReadToEndAsync();
            Task<string> output = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(120_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return false;
            }

            // Forced to complete before the process is disposed, exactly as
            // in Ffmpeg.Run — the pipes closed when the process exited above,
            // so both are already done or a moment from it.
            _ = error.Result;
            _ = output.Result;

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
