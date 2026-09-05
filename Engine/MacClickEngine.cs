using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JinxyMac.Core;

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

    public void MouseDown(ClickButton button) => Post(button, down: true, pressure: 1.0);

    public void MouseUp(ClickButton button) => Post(button, down: false, pressure: 0.0);

    /// <summary>
    /// The Quartz event type and button number for each button.
    /// </summary>
    /// <remarks>
    /// Left and right have their own event types; everything else is "other"
    /// and carries its number in the event. Kept as one table so the down and
    /// the up cannot come from different places.
    /// </remarks>
    private static (uint Down, uint Up, uint Number) Codes(ClickButton button) => button switch
    {
        ClickButton.Right => (EventRightMouseDown, EventRightMouseUp, MouseButtonRight),
        ClickButton.Middle => (EventOtherMouseDown, EventOtherMouseUp, MouseButtonCenter),
        _ => (EventLeftMouseDown, EventLeftMouseUp, MouseButtonLeft)
    };

    /// <summary>
    /// The event source every event is posted from, built once.
    /// </summary>
    /// <remarks>
    /// Created rather than passing null, for one reason that matters more than
    /// the rest: a source has a local events suppression interval, and it
    /// defaults to a quarter of a second. For that long after each synthetic
    /// event macOS ignores the real mouse and keyboard.
    ///
    /// A quarter second is nothing when a script clicks once. This posts an
    /// event every fifty milliseconds at twenty CPS, so the window never
    /// closes and the machine stops seeing its own user — including the hotkey
    /// meant to stop the clicker. Setting the interval to zero is the whole
    /// point of owning the source.
    /// </remarks>
    private static readonly IntPtr Source = CreateSource();

    private static IntPtr CreateSource()
    {
        try
        {
            IntPtr source = CGEventSourceCreate(SourceStateHidSystem);

            // Never released: it lives as long as the process, and there is
            // nowhere sensible to free it that is not process exit.
            if (source != IntPtr.Zero) CGEventSourceSetLocalEventsSuppressionInterval(source, 0.0);

            return source;
        }
        catch
        {
            // Null is a valid source argument — it just means the default one,
            // suppression interval and all. Worse, but still clicking.
            return IntPtr.Zero;
        }
    }

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
            move = CGEventCreateMouseEvent(Source, EventMouseMoved, to, MouseButtonLeft);
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
    /// <remarks>
    /// Click state is the field that decides whether this is a click at all.
    /// A mouse-down created without it carries a state of zero, which means
    /// "not part of a click sequence" — and while AppKit will forward that to a
    /// button anyway, anything reading events itself treats it as noise. A game
    /// is exactly that, which is why the clicks landed everywhere except the
    /// one place they were wanted.
    ///
    /// Pressure goes with it. A real button reports full pressure while held
    /// and none once released, and the pair is what a strict reader checks.
    /// </remarks>
    private static void Post(ClickButton button, bool down, double pressure)
    {
        (uint downType, uint upType, uint number) = Codes(button);

        IntPtr click = IntPtr.Zero;

        try
        {
            click = CGEventCreateMouseEvent(Source, down ? downType : upType, Location(), number);
            if (click == IntPtr.Zero) return;

            CGEventSetIntegerValueField(click, EventFieldClickState, 1);
            CGEventSetDoubleValueField(click, EventFieldPressure, pressure);

            CGEventPost(HidEventTap, click);
        }
        catch
        {
            // Never bring the loop down.
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
    private const uint EventRightMouseDown = 3;
    private const uint EventRightMouseUp = 4;
    private const uint EventOtherMouseDown = 25;
    private const uint EventOtherMouseUp = 26;
    private const uint MouseButtonLeft = 0;
    private const uint MouseButtonRight = 1;
    private const uint MouseButtonCenter = 2;
    private const uint HidEventTap = 0;

    private const int EventFieldClickState = 1;
    private const int EventFieldPressure = 2;
    private const int EventFieldDeltaX = 4;
    private const int EventFieldDeltaY = 5;

    /// <summary>Hardware state, the same source the real mouse reports through.</summary>
    private const uint SourceStateHidSystem = 1;

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
    private static extern void CGEventSetDoubleValueField(IntPtr theEvent, int field, double value);

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventSourceCreate(uint stateId);

    [DllImport(ApplicationServices)]
    private static extern void CGEventSourceSetLocalEventsSuppressionInterval(IntPtr source, double seconds);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr reference);
}
