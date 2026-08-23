using Xunit;

namespace JinxyMac.Capture.Tests;

/// <summary>
/// Reading ffmpeg's device list, and building the arguments that capture from
/// what it found.
/// </summary>
/// <remarks>
/// This is the part of the Mac recorder that can be checked from a Windows
/// machine. The capture itself cannot — it needs a screen, a permission dialog
/// and an actual Mac — but the parse of avfoundation's output and the shape of
/// the command line are just text, and text is testable anywhere.
///
/// The sample below is real ffmpeg output, not invented. Getting the format
/// wrong here would mean shipping a picker that lists nothing on a machine that
/// has two screens, which is exactly the class of bug that cannot be found
/// without a Mac.
/// </remarks>
public class CaptureBackendTests
{
    private const string DeviceList = """
        [AVFoundation indev @ 0x7f8] AVFoundation video devices:
        [AVFoundation indev @ 0x7f8] [0] FaceTime HD Camera
        [AVFoundation indev @ 0x7f8] [1] Capture screen 0
        [AVFoundation indev @ 0x7f8] [2] Capture screen 1
        [AVFoundation indev @ 0x7f8] AVFoundation audio devices:
        [AVFoundation indev @ 0x7f8] [0] Built-in Microphone
        """;

    [Fact]
    public void ReadsTheScreensOutOfTheDeviceList()
    {
        List<CaptureDevice> screens = CaptureBackend.ParseScreens(DeviceList);

        Assert.Equal(2, screens.Count);
        Assert.Equal(1, screens[0].Index);
        Assert.Equal("Capture screen 0", screens[0].Name);
        Assert.Equal(2, screens[1].Index);
    }

    [Fact]
    public void LeavesTheCameraOutOfIt()
    {
        List<CaptureDevice> screens = CaptureBackend.ParseScreens(DeviceList);

        Assert.DoesNotContain(screens, s => s.Name.Contains("FaceTime"));
    }

    /// <summary>
    /// The audio list restarts its numbering at zero. Reading past the boundary
    /// would offer the built-in microphone as index 0 — a device that exists,
    /// so the failure would be a black recording rather than an error.
    /// </summary>
    [Fact]
    public void StopsAtTheAudioDevices()
    {
        List<CaptureDevice> screens = CaptureBackend.ParseScreens(DeviceList);

        Assert.DoesNotContain(screens, s => s.Name.Contains("Microphone"));
        Assert.All(screens, s => Assert.True(s.Index > 0));
    }

    [Fact]
    public void FindsNothingInEmptyOutput()
    {
        Assert.Empty(CaptureBackend.ParseScreens(""));
        Assert.Empty(CaptureBackend.ParseScreens("ffmpeg version 7.1\nbuilt with clang"));
    }

    /// <summary>
    /// A machine with no camera starts the list at the screen, so index 0 is a
    /// legitimate screen there. Nothing may assume screens start at 1.
    /// </summary>
    [Fact]
    public void AcceptsAScreenAtIndexZero()
    {
        List<CaptureDevice> screens = CaptureBackend.ParseScreens("""
            [AVFoundation indev @ 0x1] AVFoundation video devices:
            [AVFoundation indev @ 0x1] [0] Capture screen 0
            [AVFoundation indev @ 0x1] AVFoundation audio devices:
            """);

        CaptureDevice only = Assert.Single(screens);
        Assert.Equal(0, only.Index);
    }

    [Fact]
    public void CapturesTheCursorAndTheClicks()
    {

        string args = CaptureBackend.MacInputArgs(new CaptureDevice(1, "Capture screen 0"), 60, withFramerate: true);

        Assert.Contains("-capture_cursor 1", args);
        Assert.Contains("-capture_mouse_clicks 1", args);
    }

    [Fact]
    public void AddressesTheChosenScreenByItsIndex()
    {

        string args = CaptureBackend.MacInputArgs(new CaptureDevice(2, "Capture screen 1"), 60, withFramerate: true);

        Assert.Contains("-i \"2:none\"", args);
    }

    /// <summary>
    /// The retry path. A screen that rejects the requested rate is recorded at
    /// its own, which means the second attempt must not carry -framerate.
    /// </summary>
    [Fact]
    public void DropsTheFramerateWhenAskedTo()
    {

        var screen = new CaptureDevice(1, "Capture screen 0");

        Assert.Contains("-framerate 120", CaptureBackend.MacInputArgs(screen, 120, withFramerate: true));
        Assert.DoesNotContain("-framerate", CaptureBackend.MacInputArgs(screen, 120, withFramerate: false));
    }

    /// <summary>
    /// The fix for a minute of capture arriving as a one-second clip. Without
    /// wall-clock stamps ffmpeg believes the requested rate, so fifty frames
    /// gathered over a minute are written as a second of sixty-fps video.
    /// </summary>
    [Fact]
    public void StampsFramesWithTheClock()
    {
        string args = CaptureBackend.MacInputArgs(new CaptureDevice(1, "Capture screen 0"), 60, withFramerate: true);

        Assert.Contains("-use_wallclock_as_timestamps 1", args);
    }

    /// <summary>
    /// The other half: honest timestamps alone give a sparse variable-rate file.
    /// CFR holds the last frame so a still screen records at its real length.
    /// </summary>
    [Fact]
    public void ForcesAConstantOutputRate()
    {
        string args = CaptureBackend.OutputArgs(60);

        Assert.Contains("-fps_mode cfr", args);
        Assert.Contains("-r 60", args);
    }

    /// <summary>
    /// Input options have to precede -i or ffmpeg applies them to the output,
    /// where they mean something else or nothing at all.
    /// </summary>
    [Fact]
    public void PutsTheInputOptionsBeforeTheInput()
    {
        string args = CaptureBackend.MacInputArgs(new CaptureDevice(1, "Capture screen 0"), 60, withFramerate: true);

        int input = args.IndexOf("-i ", StringComparison.Ordinal);

        Assert.True(input > 0);
        Assert.True(args.IndexOf("-framerate", StringComparison.Ordinal) < input);
    }
}
