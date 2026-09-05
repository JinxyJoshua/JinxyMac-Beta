using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JinxyMac.Engine;

/// <summary>
/// The one Quartz event source every synthetic event in this app is posted
/// from.
/// </summary>
/// <remarks>
/// Shared rather than one per engine, for the reason the clicker found the
/// hard way: a source has a local events suppression interval, and it
/// defaults to a quarter of a second. For that long after each synthetic
/// event macOS ignores the real mouse and keyboard.
///
/// A quarter second is nothing when a script clicks once. The clicker posts
/// every fifty milliseconds at twenty CPS, so the window never closes and the
/// machine stops seeing its own user — including the hotkey meant to stop it.
/// Setting the interval to zero is the whole point of owning the source.
///
/// A second source created for the keyboard would carry the default interval
/// and reintroduce exactly that, for every key a macro sends — and macros are
/// meant to run while the clicker runs, so the two would compound.
/// </remarks>
[SupportedOSPlatform("macos")]
public static class MacEventSource
{
    /// <summary>
    /// The source, or <see cref="IntPtr.Zero"/> if it could not be made.
    /// </summary>
    /// <remarks>
    /// Zero is a valid argument to every function that takes it — it just
    /// means the default source, suppression interval and all. Worse, but
    /// still working, which is the right failure for this.
    ///
    /// Never released: it lives as long as the process, and there is nowhere
    /// sensible to free it that is not process exit.
    /// </remarks>
    public static IntPtr Handle { get; } = Create();

    private static IntPtr Create()
    {
        try
        {
            IntPtr source = CGEventSourceCreate(SourceStateHidSystem);

            if (source != IntPtr.Zero) CGEventSourceSetLocalEventsSuppressionInterval(source, 0.0);

            return source;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>Hardware state, the same source the real mouse reports through.</summary>
    private const uint SourceStateHidSystem = 1;

    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventSourceCreate(uint stateId);

    [DllImport(ApplicationServices)]
    private static extern void CGEventSourceSetLocalEventsSuppressionInterval(IntPtr source, double seconds);
}
