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
