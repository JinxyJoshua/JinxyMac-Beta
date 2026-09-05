using System.Diagnostics;
using System.Globalization;

namespace JinxyMac.Capture;

/// <summary>
/// A rolling buffer of the last N seconds, so a hotkey can save something that
/// has already happened.
/// </summary>
/// <remarks>
/// ffmpeg writes fixed-length segments and wraps around a fixed count, so disk
/// use is bounded. Saving picks the newest segments and concatenates them.
///
/// Segments are MPEG-TS, not MP4, and that is the load-bearing decision. An MP4
/// only becomes readable once its index is written at the end, so the segment
/// currently being written would be unusable — losing exactly the last second
/// before the hotkey, which is the second that matters. TS has no such index
/// and a partially written file still decodes.
///
/// Nothing here is Windows-specific; the platform difference is entirely inside
/// CaptureBackend.InputArgs, which already answers for both.
/// </remarks>
public sealed class ReplayBuffer : IDisposable
{
    /// <summary>One second per segment: the finest granularity worth the file churn.</summary>
    private const int SegmentSeconds = 1;

    private Process? _process;
    private string? _bufferDirectory;

    // Guards Start's own re-entrancy against concurrent callers — a hotkey
    // thread and the UI both reach this, and everything between the IsRunning
    // check and the _process assignment below (file cleanup, spawning
    // ffmpeg) is real work with no lock of its own. Two threads that both pass
    // the check before either assigns _process would each start ffmpeg, and
    // the second would silently orphan the first exactly like the recorder's
    // own bug.
    private readonly ReentryGuard _starting = new();

    public bool IsRunning => _process is { HasExited: false };

    public int CapacitySeconds { get; private set; }

    public void Start(int capacitySeconds, int framesPerSecond, CaptureDevice? screen = null)
    {
        if (!_starting.TryEnter()) return;

        try
        {
            if (IsRunning) return;

            string ffmpeg = Ffmpeg.Find()
                ?? throw new FileNotFoundException($"ffmpeg was not found. {Ffmpeg.InstallHint}");

            CapacitySeconds = Math.Max(10, capacitySeconds);

            _bufferDirectory = Path.Combine(Path.GetTempPath(), "JinxyMac", "replay");
            Directory.CreateDirectory(_bufferDirectory);

            foreach (string stale in Directory.GetFiles(_bufferDirectory, "buf*.ts"))
            {
                try { File.Delete(stale); } catch { /* in use by a previous run */ }
            }

            string pattern = Path.Combine(_bufferDirectory, "buf%04d.ts");

            var info = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            // -g forces a keyframe every segment, so each one decodes on its own
            // and the pieces can be joined without re-encoding.
            //
            // The buffer runs for the whole session, so its cost matters more
            // than the recorder's — this is the one that would sit on two cores
            // all game, which is why the hardware encoder is not optional here.
            //
            // ArgumentList throughout, for the same reason as the recorder:
            // the segment pattern lives under a fixed temp path today, but
            // nothing here should depend on that staying true.
            info.ArgumentList.Add("-y");

            foreach (string token in ArgumentTokens.Split(CaptureBackend.InputArgs(screen, framesPerSecond)))
                info.ArgumentList.Add(token);

            foreach (string token in ArgumentTokens.Split(CaptureBackend.EncoderArgs(ffmpeg)))
                info.ArgumentList.Add(token);

            foreach (string token in ArgumentTokens.Split(CaptureBackend.OutputArgs(framesPerSecond)))
                info.ArgumentList.Add(token);

            info.ArgumentList.Add("-g");
            info.ArgumentList.Add(framesPerSecond.ToString(CultureInfo.InvariantCulture));
            info.ArgumentList.Add("-f");
            info.ArgumentList.Add("segment");
            info.ArgumentList.Add("-segment_time");
            info.ArgumentList.Add(SegmentSeconds.ToString(CultureInfo.InvariantCulture));
            info.ArgumentList.Add("-segment_format");
            info.ArgumentList.Add("mpegts");
            info.ArgumentList.Add("-segment_wrap");
            info.ArgumentList.Add((CapacitySeconds / SegmentSeconds + 1).ToString(CultureInfo.InvariantCulture));
            info.ArgumentList.Add("-reset_timestamps");
            info.ArgumentList.Add("1");
            info.ArgumentList.Add(pattern);

            _process = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg would not start.");

            // Undrained pipes fill and stall the process partway through.
            _process.ErrorDataReceived += (_, _) => { };
            _process.OutputDataReceived += (_, _) => { };
            _process.BeginErrorReadLine();
            _process.BeginOutputReadLine();
        }
        finally
        {
            _starting.Exit();
        }
    }

    /// <summary>
    /// Writes the most recent <paramref name="seconds"/> to an MP4.
    /// </summary>
    /// <returns>The saved path, or null if the buffer held nothing usable.</returns>
    public async Task<string?> SaveLastAsync(int seconds, string outputDirectory)
    {
        if (_bufferDirectory == null) return null;

        string? ffmpeg = Ffmpeg.Find();
        if (ffmpeg == null) return null;

        // Newest first by write time — with wrapping filenames the timestamp is
        // the only thing that says which segment is which.
        FileInfo[] newest = new DirectoryInfo(_bufferDirectory)
            .GetFiles("buf*.ts")
            .Where(f => f.Length > 0)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(Math.Max(1, seconds / SegmentSeconds) + 1)
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToArray();

        if (newest.Length == 0) return null;

        Directory.CreateDirectory(outputDirectory);

        string output = Path.Combine(outputDirectory, $"replay-{DateTime.Now:yyyy-MM-dd-HHmmss}.mp4");
        string joined = string.Join("|", newest.Select(f => f.FullName));

        var info = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        // ArgumentList, not an interpolated string: outputDirectory is the
        // user's clip folder, and a folder name holding a double quote would
        // otherwise corrupt this command line — see ScreenRecorder.Launch for
        // the full story. Stream copy: no re-encode, so saving is near-instant
        // and costs nothing beyond the read. -t trims the leading overshoot
        // from the extra segment.
        info.ArgumentList.Add("-y");
        info.ArgumentList.Add("-i");
        info.ArgumentList.Add($"concat:{joined}");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("copy");
        info.ArgumentList.Add("-t");
        info.ArgumentList.Add(seconds.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add("-movflags");
        info.ArgumentList.Add("+faststart");
        info.ArgumentList.Add(output);

        using Process? process = Process.Start(info);
        if (process == null) return null;

        process.ErrorDataReceived += (_, _) => { };
        process.OutputDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        await process.WaitForExitAsync().ConfigureAwait(false);

        return File.Exists(output) && new FileInfo(output).Length > 0 ? output : null;
    }

    public void Stop()
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

                if (!process.WaitForExit(5000)) process.Kill(entireProcessTree: true);
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

    public void Dispose() => Stop();
}
