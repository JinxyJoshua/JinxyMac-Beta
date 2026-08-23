using System.Text.RegularExpressions;

namespace JinxyMac.Capture;

/// <summary>One thing ffmpeg can capture from.</summary>
/// <param name="Index">avfoundation's device index, which is what -i takes.</param>
public sealed record CaptureDevice(int Index, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Chooses how the screen is captured and encoded on this machine.
/// </summary>
/// <remarks>
/// The Windows build's answer was ddagrab plus whichever vendor encoder the
/// card had — measured at 2.64s of CPU against libx264's 14.91s over the same
/// eight second capture. None of that transfers. macOS has no Desktop
/// Duplication and no vendor split: screens are avfoundation devices like
/// cameras are, and every Mac Apple has shipped in over a decade has the same
/// hardware encoder behind h264_videotoolbox.
///
/// So the shape is the same and the contents are not. The encoder is still
/// confirmed by actually encoding a frame rather than trusted because ffmpeg
/// lists it — being compiled in says nothing about the hardware being there —
/// and everything still degrades to libx264 rather than failing.
/// </remarks>
public static class CaptureBackend
{
    private static string? _encoderArgs;
    private static string? _encoderName;

    /// <summary>Encoder chosen for this machine, for display. Null until probed.</summary>
    public static string? EncoderName => _encoderName;

    public static string EncoderArgs(string ffmpeg)
    {
        if (_encoderArgs != null) return _encoderArgs;

        string? candidate = OperatingSystem.IsMacOS() ? "h264_videotoolbox" : null;

        if (candidate != null && CanEncode(ffmpeg, candidate))
        {
            _encoderName = candidate;

            // Bitrate rather than CRF. VideoToolbox ignores CRF outright — it
            // is a rate-controlled hardware encoder — and 8 Mbit is ample for a
            // desktop capture at 1080p.
            _encoderArgs = $"-c:v {candidate} -b:v 8M -pix_fmt yuv420p";
        }
        else
        {
            _encoderName = "libx264";
            _encoderArgs = "-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p";
        }

        return _encoderArgs;
    }

    /// <summary>
    /// Whether the encoder actually initialises, tested by encoding one frame of
    /// a generated source. Cheap — a few hundred milliseconds, once per run.
    /// </summary>
    /// <remarks>
    /// Uses no capture device on purpose, so it costs nothing in permissions and
    /// answers only the question asked: can this machine encode H.264 in
    /// hardware. A Mac in a state where VideoToolbox will not initialise is
    /// unusual but the fallback is one line, so it is not worth assuming.
    /// </remarks>
    private static bool CanEncode(string ffmpeg, string encoder)
    {
        (int exitCode, _) = Ffmpeg.Run(ffmpeg,
            "-hide_banner -loglevel error -f lavfi -i color=black:s=256x256 "
            + $"-frames:v 1 -c:v {encoder} -f null -",
            timeoutMs: 8000);

        return exitCode == 0;
    }

    /// <summary>
    /// The screens avfoundation will capture from.
    /// </summary>
    /// <remarks>
    /// ffmpeg prints the list to stderr and then exits non-zero, because listing
    /// devices is technically a failed capture. That is expected — the exit code
    /// is ignored and the text is what matters.
    ///
    /// Only the video half is read. The list continues into audio devices with
    /// indices that start over at zero, and mixing the two would offer a
    /// microphone as a screen.
    /// </remarks>
    public static List<CaptureDevice> Screens(string ffmpeg)
    {
        var screens = new List<CaptureDevice>();

        if (!OperatingSystem.IsMacOS()) return screens;

        (_, string output) = Ffmpeg.Run(ffmpeg,
            "-hide_banner -f avfoundation -list_devices true -i \"\"",
            timeoutMs: 8000);

        return ParseScreens(output);
    }

    /// <summary>Split out from the process call so the parsing can be tested.</summary>
    internal static List<CaptureDevice> ParseScreens(string output)
    {
        var screens = new List<CaptureDevice>();
        bool inVideo = false;

        foreach (string line in output.Split('\n'))
        {
            if (line.Contains("AVFoundation video devices", StringComparison.OrdinalIgnoreCase))
            {
                inVideo = true;
                continue;
            }

            if (line.Contains("AVFoundation audio devices", StringComparison.OrdinalIgnoreCase))
            {
                inVideo = false;
                continue;
            }

            if (!inVideo) continue;

            Match match = DeviceLine.Match(line);
            if (!match.Success) continue;

            string name = match.Groups[2].Value.Trim();

            // Cameras are on the same list. Someone recording their gameplay
            // does not want the FaceTime camera offered as a capture source.
            if (!name.Contains("screen", StringComparison.OrdinalIgnoreCase)) continue;

            screens.Add(new CaptureDevice(int.Parse(match.Groups[1].Value), name));
        }

        return screens;
    }

    /// <summary>
    /// Input arguments for a capture, up to and including the source.
    /// </summary>
    /// <remarks>
    /// The cursor is drawn in because a clip of an autoclicker with no pointer
    /// in it is missing the thing being demonstrated. Clicks are highlighted for
    /// the same reason.
    ///
    /// The audio half of the -i pair is "none" rather than omitted. avfoundation
    /// reads "1" as a video device and "1:0" as video plus audio; leaving the
    /// colon off entirely works, but being explicit means a future audio option
    /// changes one token instead of the parse.
    /// </remarks>
    public static string InputArgs(CaptureDevice? screen, int framesPerSecond, bool withFramerate = true)
    {
        // Windows is for exercising the recorder on the machine this is being
        // written on. It is not what ships.
        return OperatingSystem.IsMacOS()
            ? MacInputArgs(screen, framesPerSecond, withFramerate)
            : $"-f gdigrab -framerate {framesPerSecond} -i desktop";
    }

    /// <summary>
    /// Output arguments that force a real-time, constant-rate file.
    /// </summary>
    /// <remarks>
    /// The other half of the wall-clock fix. Honest timestamps alone give a
    /// variable-rate file whose frames are correctly spaced but sparse; players
    /// and every upload target handle that badly, and editing it is worse.
    ///
    /// CFR tells ffmpeg to hold the last frame until the next one arrives, so
    /// a still screen produces a still video of the right length rather than a
    /// short one. It costs nothing to encode — a duplicated frame compresses to
    /// almost nothing.
    /// </remarks>
    public static string OutputArgs(int framesPerSecond) =>
        $"-fps_mode cfr -r {framesPerSecond}";

    /// <summary>
    /// Split from the platform check so the arguments can be tested anywhere.
    /// </summary>
    /// <remarks>
    /// The point of the seam: a test that skipped itself off a Mac would leave
    /// the one thing checkable about the Mac capture path unchecked on the only
    /// machine available to check it.
    /// </remarks>
    internal static string MacInputArgs(CaptureDevice? screen, int framesPerSecond, bool withFramerate)
    {
        string rate = withFramerate ? $"-framerate {framesPerSecond} " : "";

        // Stamp frames with the clock, not with whatever the device claims.
        //
        // avfoundation screen capture does not hand over a steady stream — it
        // emits a frame when the screen changes, and a still screen produces
        // almost none. The requested framerate is a request, and ffmpeg
        // otherwise believes it: capture a minute at sixty and hand it fifty
        // frames, and it writes a file that says sixty frames per second and
        // lasts one second. Every frame is there; the clip is just played a
        // minute too fast.
        //
        // Wall-clock timestamps make the durations real, and CFR on the output
        // fills the gaps so the result is a video rather than a slideshow with
        // honest metadata.
        string clock = "-use_wallclock_as_timestamps 1 ";

        // Index 1 rather than 0 when nothing is chosen: on a Mac with a camera
        // — which is most of them — 0 is the camera and 1 is the first screen.
        int index = screen?.Index ?? 1;

        return "-f avfoundation -capture_cursor 1 -capture_mouse_clicks 1 "
               + clock
               + rate
               + $"-i \"{index}:none\"";
    }

    private static readonly Regex DeviceLine =
        new(@"\[(\d+)\]\s+(.+)$", RegexOptions.Compiled);
}
