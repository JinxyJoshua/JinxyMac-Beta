using JinxyMac.Core;
using JinxyMac.Engine;

namespace JinxyMac;

/// <summary>
/// The Settings page's diagnostics card: what macOS thinks this app is.
/// </summary>
/// <remarks>
/// Exists because the Mac problems cannot be reproduced where the code is
/// written. "It says BLOCKED" has cost several rounds of asking people to run
/// commands in Terminal and relay the output; this is the same facts, on screen,
/// with a button that copies them.
/// </remarks>
public partial class MainWindow
{
    private void WireDiagnostics()
    {
        CopyDiagnosticsButton.Click += async (_, _) =>
        {
            if (Clipboard == null) return;

            await Clipboard.SetTextAsync(DiagnosticsText.Text ?? "");
            CopyDiagnosticsButton.Content = "Copied";
        };
    }

    /// <summary>
    /// Rebuilt each time the page is opened, so it never shows a stale answer
    /// about a permission the user has just changed.
    /// </summary>
    private void RefreshDiagnostics()
    {
        CopyDiagnosticsButton.Content = "Copy";

        DiagnosticsText.Text = MacDiagnostics.Report(
            Updater.Version,
            Describe(MacPermissions.Accessibility()),
            Describe(MacPermissions.ScreenRecording()));
    }

    private static string Describe(Permission permission) => permission switch
    {
        Permission.Granted => "granted",
        Permission.Denied => "NOT granted",
        _ => "not needed on this platform"
    };
}
