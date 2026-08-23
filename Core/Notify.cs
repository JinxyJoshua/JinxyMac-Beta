using System.Diagnostics;

namespace JinxyMac.Core;

/// <summary>
/// A system notification, for things that finish while the window is not being
/// looked at.
/// </summary>
/// <remarks>
/// Instant replay is the reason this exists. You press a key mid-game, the app
/// writes a clip, and nothing tells you it worked — the window is behind
/// Roblox and its status line may as well not be there. Same for an upload that
/// completes a minute after you started it.
///
/// osascript rather than UNUserNotificationCenter, and that is a deliberate
/// trade. The framework route needs a signed bundle with a notification
/// entitlement, which this build does not have and cannot get from here.
/// osascript is a process spawn — expensive by the standards of a click loop,
/// irrelevant by the standards of something that fires once when a clip saves.
/// </remarks>
public static class Notify
{
    /// <summary>Whether notifications will actually appear on this platform.</summary>
    public static bool Available => OperatingSystem.IsMacOS();

    /// <summary>
    /// Shows a notification, or does nothing where they are unavailable.
    /// </summary>
    /// <remarks>
    /// Never throws. A missing notification is not worth interrupting whatever
    /// just succeeded, and this is only ever called after something worked.
    /// </remarks>
    public static void Send(string title, string message)
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            // AppleScript string literals take double quotes and backslash
            // escapes. Anything unescaped here — a filename with a quote in it —
            // would end the string early and turn the rest into script.
            string script =
                $"display notification \"{Escape(message)}\" with title \"{Escape(title)}\"";

            var info = new ProcessStartInfo("/usr/bin/osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            info.ArgumentList.Add("-e");
            info.ArgumentList.Add(script);

            Process.Start(info);
        }
        catch
        {
            // No notification is a smaller problem than a crash on the way out
            // of a successful save.
        }
    }

    /// <summary>Makes a string safe to sit inside an AppleScript literal.</summary>
    internal static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
}
