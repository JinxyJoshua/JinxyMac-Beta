using System.Diagnostics;
using System.Text;

namespace JinxyMac.Capture;

/// <summary>
/// Screen capture, backed by ffmpeg's avfoundation input.
/// </summary>
/// <remarks>
/// Kept behind Start, StopAsync and IsRecording, the same surface the Windows
/// build uses, so the page above it does not know which platform it is on.
///
/// Two things here exist because this cannot be tested on a Mac from here.
///
/// The first is the framerate retry. avfoundation validates the requested rate
/// against what the device reports, and screen devices on some macOS versions
/// refuse anything but their own — the capture dies immediately with "Selected
/// framerate is not supported". Rather than guess which versions, the recorder
/// notices the instant death and retries once without the request, taking
/// whatever rate the screen offers. A clip at the wrong framerate beats no clip.
///
/// The second is that stderr is kept. When a capture fails the reason is in
/// there — most often Screen Recording permission, which macOS does not
/// otherwise announce — and a recorder that says "it did not work" when the
/// machine told it exactly why is the same mistake the uploader made.
/// </remarks>
public sealed class ScreenRecorder : IDisposable
{
    private Process? _process;
    private readonly StringBuilder _log = new();

    // Guards StartAsync's own re-entrancy. _process is not assigned until
    // after the settle delay below, so IsRecording alone cannot stop a second
    // call arriving during that gap — see StartAsync.
    private readonly ReentryGuard _starting = new();

    public bool IsRecording => _process is { HasExited: false };

    public string? OutputPath { get; private set; }

    public DateTime StartedUtc { get; private set; }

    /// <summary>What ffmpeg said, when something went wrong.</summary>
    public string LastError { get; private set; } = "";

    /// <summary>Where clips go, per platform convention.</summary>
    /// <remarks>
    /// The home directory is asked for twice, because the first answer can be
    /// empty. GetFolderPath reads HOME, and a process launched in an unusual
    /// way may not have it — at which point Path.Combine happily returns a
    /// relative path and clips land wherever the working directory happens to
    /// be, which for a Finder-launched app is the root of the disk.
    /// </remarks>
    public static string DefaultFolder
    {
        get
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? "";

            if (OperatingSystem.IsMacOS())
            {
                return home.Length > 0
                    ? Path.Combine(home, "Movies", "Jinxy Clips")
                    : "/tmp/Jinxy Clips";
            }

            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            if (videos.Length == 0) videos = home;

            return Path.Combine(videos, "Jinxy Clips");
        }
    }

    /// <remarks>
    /// The re-entrancy guard is entered synchronously, before anything else —
    /// including the <c>IsRecording</c> check, which is not enough on its own.
    /// <c>_process</c> is not assigned until after the settle delay in
    /// <see cref="Launch"/>, so a second call arriving during that roughly
    /// one-second gap would see <c>IsRecording</c> as false too, and start a
    /// second ffmpeg that the first's <see cref="StopAsync"/> would never know
    /// about. Guarding only the caller's button does not close this: a hotkey
    /// bound straight to this method reaches it exactly the same way.
    /// </remarks>
    public async Task<string> StartAsync(string outputDirectory, int framesPerSecond,
                                         CaptureDevice? screen = null)
    {
        if (!_starting.TryEnter()) throw new InvalidOperationException("Already recording.");

        try
        {
            if (IsRecording) throw new InvalidOperationException("Already recording.");

            string ffmpeg = Ffmpeg.Find()
                ?? throw new FileNotFoundException($"ffmpeg was not found. {Ffmpeg.InstallHint}");

            Directory.CreateDirectory(outputDirectory);

            string path = Path.Combine(outputDirectory,
                $"clip-{DateTime.Now:yyyy-MM-dd-HHmmss}.mp4");

            if (await Launch(ffmpeg, path, screen, framesPerSecond, withFramerate: true))
                return path;

            // Only the framerate is worth a second attempt. A permission failure
            // will fail again identically, and retrying it just hides the message.
            if (!LastError.Contains("framerate", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Describe());

            if (await Launch(ffmpeg, path, screen, framesPerSecond, withFramerate: false))
                return path;

            throw new InvalidOperationException(Describe());
        }
        finally
        {
            _starting.Exit();
        }
    }

    /// <returns>False if ffmpeg died in the first moment, which means it never
    /// started capturing at all.</returns>
    private async Task<bool> Launch(string ffmpeg, string path, CaptureDevice? screen,
                                    int framesPerSecond, bool withFramerate)
    {
        _log.Clear();

        var info = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        // ArgumentList, not an interpolated string: the clip folder is
        // whatever the user typed or picked, and macOS folder names can
        // contain a double quote. Building one quoted string out of it would
        // let a folder like Jinxy "Clips" corrupt the command line; adding it
        // as its own ArgumentList entry needs no escaping at all, because no
        // shell ever parses it — it goes to the child process as one atomic
        // argument regardless of what characters it holds.
        info.ArgumentList.Add("-y");

        foreach (string token in ArgumentTokens.Split(CaptureBackend.InputArgs(screen, framesPerSecond, withFramerate)))
            info.ArgumentList.Add(token);

        foreach (string token in ArgumentTokens.Split(CaptureBackend.EncoderArgs(ffmpeg)))
            info.ArgumentList.Add(token);

        foreach (string token in ArgumentTokens.Split(CaptureBackend.OutputArgs(framesPerSecond)))
            info.ArgumentList.Add(token);

        info.ArgumentList.Add("-movflags");
        info.ArgumentList.Add("+faststart");
        info.ArgumentList.Add(path);

        Process process = Process.Start(info)
            ?? throw new InvalidOperationException("ffmpeg would not start.");

        // ffmpeg writes continuously to stderr. Left undrained the pipe fills
        // and the process blocks partway through the recording. Only the head is
        // kept — the rest is a progress counter, and the errors come first.
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null && _log.Length < 4000) _log.AppendLine(e.Data);
        };
        process.OutputDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        // Long enough for a rejected device or a denied permission to have made
        // itself known, short enough not to be felt as a delay.
        await Task.Delay(SettleMs).ConfigureAwait(false);

        if (process.HasExited)
        {
            LastError = _log.ToString();
            process.Dispose();
            return false;
        }

        _process = process;
        OutputPath = path;
        StartedUtc = DateTime.UtcNow;
        LastError = "";

        return true;
    }

    /// <summary>
    /// The one line of ffmpeg's output that says what actually went wrong.
    /// </summary>
    /// <remarks>
    /// ffmpeg's stderr opens with a build banner and its configure flags. Handing
    /// that to someone as an error message tells them nothing; the last few lines
    /// are where the reason is.
    /// </remarks>
    private string Describe()
    {
        string[] lines = _log.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("configuration:", StringComparison.Ordinal)
                        && !line.StartsWith("lib", StringComparison.Ordinal)
                        && !line.StartsWith("ffmpeg version", StringComparison.Ordinal)
                        && !line.StartsWith("built with", StringComparison.Ordinal))
            .ToArray();

        string tail = lines.Length == 0
            ? "ffmpeg stopped without saying why."
            : string.Join("\n", lines.TakeLast(3));

        // The one failure macOS will not explain itself. Permission is granted
        // per app, and a fresh build in a new location is a new app as far as
        // the system is concerned, so this comes up more than once.
        if (OperatingSystem.IsMacOS()
            && tail.Contains("Input/output error", StringComparison.OrdinalIgnoreCase))
        {
            tail += "\n\nThis is usually Screen Recording permission. "
                  + "System Settings > Privacy & Security > Screen Recording.";
        }

        return tail;
    }

    /// <summary>
    /// Asks ffmpeg to stop rather than killing it.
    /// </summary>
    /// <remarks>
    /// This matters more than it looks. An MP4's index is written when encoding
    /// finishes; a killed process leaves a file with no moov atom, which no
    /// player will open. Sending "q" lets ffmpeg finalise properly.
    /// </remarks>
    public async Task<string?> StopAsync()
    {
        Process? process = _process;
        string? path = OutputPath;

        if (process == null) return null;

        try
        {
            if (!process.HasExited)
            {
                // ConfigureAwait(false) throughout: shutdown has to block on
                // this, and a continuation posted back to the UI thread it is
                // blocking would deadlock the app on exit.
                await process.StandardInput.WriteLineAsync("q").ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Last resort. The file is likely unplayable, which is why
                    // the graceful path is tried first.
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // Already gone.
        }
        finally
        {
            process.Dispose();
            _process = null;
        }

        return path != null && File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
    }

    /// <summary>Blocking stop for shutdown, where there is nothing to await on.</summary>
    public void StopBlocking()
    {
        Process? process = _process;
        _process = null;

        if (process == null) return;

        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.WriteLine("q");
                process.StandardInput.Flush();

                if (!process.WaitForExit(10_000)) process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already gone.
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose() => StopBlocking();

    /// <summary>How long to let a capture prove it started.</summary>
    private const int SettleMs = 1200;
}
