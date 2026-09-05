using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JinxyMac.Engine;

/// <summary>
/// Keystrokes on macOS, through Quartz.
/// </summary>
/// <remarks>
/// Simpler than the mouse side, because macOS has no equivalent of Windows'
/// split between a virtual key and a scan code. On Windows a key event has to
/// carry both — games routinely read the scan code and ignore an event
/// carrying only the virtual key. A CGKeyCode already is the hardware code,
/// so there is nothing second to carry and nothing to translate.
///
/// Posted to the HID tap and from the shared source, for the same reasons the
/// clicks are: lowest point injection is possible from, and an interval that
/// does not blind macOS to the real keyboard.
///
/// Needs Accessibility permission like everything else here. Without it macOS
/// accepts the call and throws the event away.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacKeyEngine : IKeyEngine
{
    public bool IsAvailable => OperatingSystem.IsMacOS() && IsTrusted();

    public string? Unavailable
    {
        get
        {
            if (!OperatingSystem.IsMacOS()) return "Not running on macOS.";

            return IsTrusted()
                ? null
                : "Accessibility permission is not granted, so macOS is discarding "
                  + "every keystroke. Grant it under System Settings, Privacy & Security, "
                  + "Accessibility, then restart JinxyMac.";
        }
    }

    public void KeyDown(int code) => Post(code, down: true);

    public void KeyUp(int code) => Post(code, down: false);

    private static void Post(int code, bool down)
    {
        if (code <= 0 || code > ushort.MaxValue) return;

        IntPtr key = IntPtr.Zero;

        try
        {
            key = CGEventCreateKeyboardEvent(MacEventSource.Handle, (ushort)code, down);
            if (key == IntPtr.Zero) return;

            CGEventPost(HidEventTap, key);
        }
        catch
        {
            // A failed key must never take a macro thread — or the app — down.
        }
        finally
        {
            if (key != IntPtr.Zero) CFRelease(key);
        }
    }

    private static bool IsTrusted()
    {
        try
        {
            return AXIsProcessTrusted();
        }
        catch
        {
            return false;
        }
    }

    private const uint HidEventTap = 0;

    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreateKeyboardEvent(
        IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(ApplicationServices)]
    private static extern void CGEventPost(uint tap, IntPtr theEvent);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr reference);
}
