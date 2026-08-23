using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JinxyMac.Engine;

/// <summary>
/// Mouse input on macOS, through Quartz.
/// </summary>
/// <remarks>
/// The one part of this application that cannot be checked from Windows, so it
/// is kept small and does nothing clever.
///
/// Events go to the HID tap, the lowest point injection is possible from and
/// the one hardest for a game to tell apart from a real mouse. Existing Mac
/// autoclickers that work with Roblox use the same route, which is the evidence
/// this approach rests on.
///
/// Everything needs Accessibility permission. Without it macOS accepts the
/// calls and throws the events away — no error, no exception, nothing happens.
/// That silence is why the permission is checked up front and reported rather
/// than discovered.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacClickEngine : IClickEngine
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
                  + "every click. Grant it under System Settings, Privacy & Security, "
                  + "Accessibility, then restart JinxyMac.";
        }
    }

    public void MouseDown() => Post(EventLeftMouseDown);

    public void MouseUp() => Post(EventLeftMouseUp);

    public void MoveBy(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;

        CursorPoint at = Location();

        // Quartz has no relative move: a mouse-moved event carries an absolute
        // position, so the delta is applied to wherever the pointer is now.
        var to = new CursorPoint { X = at.X + dx, Y = at.Y + dy };

        IntPtr move = IntPtr.Zero;

        try
        {
            move = CGEventCreateMouseEvent(IntPtr.Zero, EventMouseMoved, to, MouseButtonLeft);
            if (move == IntPtr.Zero) return;

            // Carrying the delta as well as the destination matters for games
            // that read movement rather than position, which is most of them
            // once the camera owns the mouse.
            CGEventSetIntegerValueField(move, EventFieldDeltaX, dx);
            CGEventSetIntegerValueField(move, EventFieldDeltaY, dy);

            CGEventPost(HidEventTap, move);
        }
        catch
        {
            // A failed move must never take the clicker down with it.
        }
        finally
        {
            if (move != IntPtr.Zero) CFRelease(move);
        }
    }

    /// <summary>Posts a button event at wherever the pointer already is.</summary>
    private static void Post(uint type)
    {
        IntPtr click = IntPtr.Zero;

        try
        {
            click = CGEventCreateMouseEvent(IntPtr.Zero, type, Location(), MouseButtonLeft);
            if (click == IntPtr.Zero) return;

            CGEventPost(HidEventTap, click);
        }
        catch
        {
            // Same reasoning as MoveBy: never bring the loop down.
        }
        finally
        {
            if (click != IntPtr.Zero) CFRelease(click);
        }
    }

    private static CursorPoint Location()
    {
        IntPtr snapshot = IntPtr.Zero;

        try
        {
            snapshot = CGEventCreate(IntPtr.Zero);
            return snapshot == IntPtr.Zero ? default : CGEventGetLocation(snapshot);
        }
        catch
        {
            return default;
        }
        finally
        {
            if (snapshot != IntPtr.Zero) CFRelease(snapshot);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public double X;
        public double Y;
    }

    private const uint EventMouseMoved = 5;
    private const uint EventLeftMouseDown = 1;
    private const uint EventLeftMouseUp = 2;
    private const uint MouseButtonLeft = 0;
    private const uint HidEventTap = 0;

    private const int EventFieldDeltaX = 4;
    private const int EventFieldDeltaY = 5;

    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreateMouseEvent(
        IntPtr source, uint type, CursorPoint position, uint button);

    [DllImport(ApplicationServices)]
    private static extern void CGEventPost(uint tap, IntPtr theEvent);

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(ApplicationServices)]
    private static extern CursorPoint CGEventGetLocation(IntPtr theEvent);

    [DllImport(ApplicationServices)]
    private static extern void CGEventSetIntegerValueField(IntPtr theEvent, int field, long value);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr reference);
}
