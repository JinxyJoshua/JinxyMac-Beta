using System.Runtime.InteropServices;

namespace JinxyMac.Engine;

/// <summary>
/// Keeps macOS from slowing Jinxy down while the game is in front, which is
/// every moment Jinxy is actually being used.
/// </summary>
/// <remarks>
/// The Mac half of the Windows app's hit-registration fix, and the defence
/// that was cut on 2026-09-04 (see that day's design doc). Two separate things
/// throttle an app whose window is covered, and both apply to Jinxy:
///
/// <b>App Nap.</b> An app macOS judges inactive has its timers coalesced and
/// its CPU priority lowered. Jinxy's window is always behind the game, so it
/// always looks inactive. The click loop sleeps between presses, and a sleep
/// that wakes late is a click that goes out late or not at all.
///
/// <b>Thread QoS.</b> On Apple silicon, threads at a low quality-of-service
/// class are put on the efficiency cores at a reduced clock, and macOS lowers
/// the class of a background app's threads over a session. That is the shape of
/// "fine at first, worse later".
///
/// <c>ThreadPriority.AboveNormal</c>, which the Windows code relies on, is no
/// defence against either. The Mac's answers are an activity assertion for the
/// first and an explicit QoS class for the second.
///
/// Every call here is best effort and a no-op off macOS. A refusal leaves the
/// app working exactly as it did before, just without the protection.
///
/// None of this has been run on a Mac. It is checked on a PC as far as a PC can
/// check it: the flags, and that nothing throws.
/// </remarks>
public static class MacScheduling
{
    /// <summary>
    /// What Jinxy tells macOS it is doing: user-initiated, latency-critical
    /// work, which exempts it from App Nap and asks for precise timers.
    /// </summary>
    /// <remarks>
    /// <c>NSActivityUserInitiatedAllowingIdleSystemSleep | NSActivityLatencyCritical</c>.
    /// The "allowing idle system sleep" variant, not plain
    /// <c>NSActivityUserInitiated</c>: this is held for as long as Jinxy is
    /// open, and the plain form would stop the Mac going to sleep the whole
    /// time an idle Jinxy sat in the background.
    /// </remarks>
    public const ulong ActivityOptions = 0x00EFFFFFUL | 0xFF00000000UL;

    /// <summary><c>QOS_CLASS_USER_INTERACTIVE</c>.</summary>
    public const uint UserInteractiveQos = 0x21;

    /// <summary>
    /// The activity token. Kept for the life of the process: the assertion ends
    /// when this object is released, so letting go of it would switch App Nap
    /// straight back on.
    /// </summary>
    private static IntPtr _activity;

    private static readonly object Gate = new();

    /// <summary>
    /// Opts the whole app out of App Nap, for as long as it runs. Safe to call
    /// more than once.
    /// </summary>
    /// <returns>True if macOS accepted the activity.</returns>
    /// <remarks>
    /// At launch rather than when clicking starts, for the reason the Windows
    /// app found: macOS decides an app is idle from how it behaves over time,
    /// so the opt-out should be in place before that judgement is made. It also
    /// covers the macro and switcher threads, which run without the clicker.
    /// </remarks>
    public static bool KeepAppAwake()
    {
        if (!OperatingSystem.IsMacOS()) return false;

        lock (Gate)
        {
            if (_activity != IntPtr.Zero) return true;

            try
            {
                IntPtr processInfo = Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
                if (processInfo == IntPtr.Zero) return false;

                IntPtr reason = SendString(
                    objc_getClass("NSString"),
                    sel_registerName("stringWithUTF8String:"),
                    "Sending clicks and keys on a schedule while a game is in front");

                IntPtr activity = SendActivity(
                    processInfo,
                    sel_registerName("beginActivityWithOptions:reason:"),
                    ActivityOptions,
                    reason);

                if (activity == IntPtr.Zero) return false;

                // Returned autoreleased. Without this retain the next drain of
                // the main thread's pool would free it, and freeing it ends the
                // activity — App Nap back on within a frame, silently.
                Send(activity, sel_registerName("retain"));

                _activity = activity;
                return true;
            }
            catch
            {
                // Missing runtime or selector. Not worth a crash.
                return false;
            }
        }
    }

    /// <summary>
    /// Puts the calling thread in the user-interactive QoS class, which keeps it
    /// on the performance cores. Call it first thing on a timing thread.
    /// </summary>
    /// <returns>True if macOS accepted the class.</returns>
    /// <remarks>
    /// Per thread, not per process: it only affects the thread that calls it,
    /// so each loop calls it at the top of its own thread.
    ///
    /// macOS refuses this with EPERM on a thread whose scheduling policy was set
    /// explicitly, which is why the Mac threads no longer get a
    /// <c>ThreadPriority</c> — see <see cref="SetsWindowsThreadPriority"/>.
    /// </remarks>
    public static bool MakeCurrentThreadInteractive()
    {
        if (!OperatingSystem.IsMacOS()) return false;

        try
        {
            return pthread_set_qos_class_self_np(UserInteractiveQos, 0) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a thread should be given a <c>ThreadPriority</c> at all.
    /// </summary>
    /// <remarks>
    /// On Windows, yes: AboveNormal is what keeps the click thread ahead of the
    /// game. On macOS, no. The QoS class above is the mechanism that matters,
    /// and a priority set through .NET can pin the thread's scheduling policy,
    /// which makes macOS refuse the QoS change. (confidence: moderate — it
    /// depends on how the .NET runtime implements the setter on macOS, which
    /// could not be checked from a PC. Leaving it unset costs nothing either way.)
    /// </remarks>
    public static bool SetsWindowsThreadPriority => !OperatingSystem.IsMacOS();

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendString(
        IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendActivity(
        IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int pthread_set_qos_class_self_np(uint qosClass, int relativePriority);
}
