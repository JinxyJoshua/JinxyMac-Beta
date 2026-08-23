using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JinxyMac.Engine;

/// <summary>What a permission is currently worth.</summary>
public enum Permission
{
    /// <summary>Not a thing on this platform, so nothing to grant.</summary>
    NotNeeded,
    Granted,
    Denied
}

/// <summary>
/// The two macOS permissions this app cannot work without.
/// </summary>
/// <remarks>
/// Both fail silently, which is the whole reason this file exists. Without
/// Accessibility, macOS accepts every synthetic click and discards it — the app
/// looks like it is running and nothing happens in the game. Without Screen
/// Recording, avfoundation hands back black frames or an I/O error rather than
/// saying what is wrong.
///
/// Neither failure is distinguishable from a bug unless the app checks and says
/// so, and a user who does not know the permission exists has no way to guess.
/// </remarks>
public static class MacPermissions
{
    public static Permission Accessibility()
    {
        if (!OperatingSystem.IsMacOS()) return Permission.NotNeeded;

        try
        {
            return AXIsProcessTrusted() ? Permission.Granted : Permission.Denied;
        }
        catch
        {
            return Permission.Denied;
        }
    }

    /// <remarks>
    /// Preflight rather than request: this runs on every visit to the Settings
    /// page, and the requesting call puts a system dialog on screen. Asking is
    /// its own button.
    /// </remarks>
    public static Permission ScreenRecording()
    {
        if (!OperatingSystem.IsMacOS()) return Permission.NotNeeded;

        try
        {
            return CGPreflightScreenCaptureAccess() ? Permission.Granted : Permission.Denied;
        }
        catch
        {
            // Older than macOS 10.15, where the permission does not exist and
            // capture simply works.
            return Permission.NotNeeded;
        }
    }

    /// <summary>
    /// Puts macOS's own permission dialog on screen.
    /// </summary>
    /// <remarks>
    /// Only ever from a button the user pressed. It also only prompts once per
    /// app per install — after a refusal macOS says nothing and the user has to
    /// go to System Settings, which is why the panel names the exact pane.
    /// </remarks>
    public static void RequestScreenRecording()
    {
        if (!OperatingSystem.IsMacOS()) return;

        try { CGRequestScreenCaptureAccess(); } catch { /* not available */ }
    }

    /// <summary>Where to turn it on by hand, when the prompt will not come back.</summary>
    public static string Where(string pane) =>
        $"System Settings > Privacy & Security > {pane}";

    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [SupportedOSPlatform("macos")]
    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    [SupportedOSPlatform("macos")]
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightScreenCaptureAccess();

    [SupportedOSPlatform("macos")]
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRequestScreenCaptureAccess();
}
