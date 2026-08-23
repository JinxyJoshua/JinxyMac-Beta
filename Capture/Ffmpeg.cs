using System.Diagnostics;

namespace JinxyMac.Capture;

/// <summary>
/// Finding ffmpeg, which is not in the same place on the two platforms — and on
/// macOS is very often not there at all.
/// </summary>
/// <remarks>
/// The Windows build ships ffmpeg.exe beside the app and is done with it. That
/// is not straightforwardly available here: a Mac binary pulled from a download
/// page cannot be built or signed from this machine, and an unsigned helper
/// inside a bundle is exactly the thing Gatekeeper refuses to run.
///
/// So a bundled copy is still preferred if someone puts one there, and
/// otherwise the usual Homebrew locations are checked. When none of them hold
/// anything, the recorder says so with the command to fix it rather than
/// failing with a path that means nothing to the person reading it.
/// </remarks>
public static class Ffmpeg
{
    private static string? _cached;
    private static bool _searched;

    /// <summary>The install line shown when nothing is found.</summary>
    public static string InstallHint => OperatingSystem.IsMacOS()
        ? "brew install ffmpeg"
        : "Put ffmpeg.exe in an 'ffmpeg' folder next to the app.";

    /// <summary>Homebrew's own binary, if it is installed.</summary>
    /// <remarks>
    /// Two fixed paths rather than PATH. Homebrew lives at one of exactly two
    /// prefixes — /opt/homebrew on Apple silicon, /usr/local on Intel — and a
    /// GUI app launched from Finder inherits a PATH that usually contains
    /// neither.
    /// </remarks>
    public static string? Homebrew()
    {
        if (!OperatingSystem.IsMacOS()) return null;

        foreach (string candidate in new[] { "/opt/homebrew/bin/brew", "/usr/local/bin/brew" })
        {
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // Unreadable is not installed, for this purpose.
            }
        }

        return null;
    }

    /// <summary>
    /// Opens Terminal with the install command running in it.
    /// </summary>
    /// <remarks>
    /// In Terminal, visibly, rather than silently from inside the app — and
    /// that is the whole design, not a shortcut.
    ///
    /// Installing ffmpeg takes minutes, prints a great deal, and occasionally
    /// asks something. Run hidden behind a spinner it would look frozen, and a
    /// prompt nobody can see is a hang. Run in Terminal the user watches the
    /// same output they would have got typing it, can stop it, and is left with
    /// a window that explains itself if it fails.
    ///
    /// It also keeps this honest: the app never installs anything the user did
    /// not watch it install.
    /// </remarks>
    /// <returns>False if Terminal could not be opened at all.</returns>
    public static bool OpenInstaller()
    {
        if (!OperatingSystem.IsMacOS()) return false;

        try
        {
            const string script =
                "tell application \"Terminal\"\n"
                + "  activate\n"
                + "  do script \"brew install ffmpeg\"\n"
                + "end tell";

            var info = new ProcessStartInfo("/usr/bin/osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            info.ArgumentList.Add("-e");
            info.ArgumentList.Add(script);

            return Process.Start(info) != null;
        }
        catch
        {
            return false;
        }
    }

    public static string? Find()
    {
        if (_searched) return _cached;

        _searched = true;
        _cached = Search();

        return _cached;
    }

    private static string? Search()
    {
        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string here = AppContext.BaseDirectory;

        var candidates = new List<string>
        {
            Path.Combine(here, "ffmpeg", exe),
            Path.Combine(here, exe)
        };

        if (OperatingSystem.IsMacOS())
        {
            // Apple silicon and Intel Homebrew respectively, then MacPorts.
            candidates.Add("/opt/homebrew/bin/ffmpeg");
            candidates.Add("/usr/local/bin/ffmpeg");
            candidates.Add("/opt/local/bin/ffmpeg");
        }

        foreach (string candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // An unreadable path is simply not the one.
            }
        }

        // PATH last. A GUI app launched from Finder inherits a much shorter PATH
        // than a terminal does, which is exactly why the fixed locations above
        // are checked first rather than relying on this.
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path == null) return null;

        foreach (string directory in path.Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // Malformed PATH entry.
            }
        }

        return null;
    }

    /// <summary>
    /// Runs ffmpeg to completion and hands back everything it said.
    /// </summary>
    /// <remarks>
    /// ffmpeg writes almost everything to stderr, including the device list,
    /// so both streams are read and joined. Bounded, because a probe that hangs
    /// must not hang the window that is waiting on it.
    /// </remarks>
    public static (int ExitCode, string Output) Run(string ffmpeg, string arguments, int timeoutMs)
    {
        try
        {
            var info = new ProcessStartInfo(ffmpeg, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using Process? process = Process.Start(info);
            if (process == null) return (-1, "");

            // Read before waiting. A full pipe blocks the child forever, and the
            // device list is long enough to fill one.
            Task<string> error = process.StandardError.ReadToEndAsync();
            Task<string> output = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (-1, "");
            }

            return (process.ExitCode, error.Result + output.Result);
        }
        catch
        {
            return (-1, "");
        }
    }
}
