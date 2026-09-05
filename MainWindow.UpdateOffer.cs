using System;
using System.Threading.Tasks;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// Turns a launch-time update into something the user cannot miss.
/// </summary>
/// <remarks>
/// <see cref="MainWindow.axaml.cs"/>'s constructor already calls
/// <c>CheckForUpdate(announce: false)</c> when the launch check is on, and
/// that method already does everything the Settings page's manual "Check
/// now" button needs: it stores the result in <c>_update</c> and lights up
/// the Settings page's own <c>UpdateBox</c>. Neither of those helps someone
/// who is looking at the Clicker page, which is the whole reason this file
/// exists — the constructor is changed to call <see cref="OfferUpdateAtLaunch"/>
/// instead, which does that same check and then, if it found something,
/// raises a <see cref="UpdateOfferWindow"/> as well. <c>CheckForUpdate</c>
/// and <c>InstallUpdateButton</c>'s Settings-page wiring are untouched —
/// this rides on top of them rather than replacing anything.
/// </remarks>
public partial class MainWindow
{
    /// <summary>
    /// Runs the launch-time check, then offers whatever it finds in a
    /// window the user cannot be looking away from — unlike the Settings
    /// page's box, this one is not tucked behind a page they may never
    /// visit.
    /// </summary>
    private async Task OfferUpdateAtLaunch()
    {
        // Reuses CheckForUpdate's own announce:false path rather than
        // calling Updater.CheckAsync a second time — that keeps the
        // Settings page's UpdateBox and UpdateStatusText in the same state
        // they would already be in today, so opening Settings later shows
        // exactly what this window is about to offer.
        await CheckForUpdate(announce: false);

        if (_update is not { } update) return;

        await ShowUpdateOffer(update);
    }

    /// <summary>
    /// Puts the offer on screen, waiting for the main window to actually be
    /// up first.
    /// </summary>
    /// <remarks>
    /// The launch check is a network round trip, so in practice this window
    /// is always shown well after <c>Show()</c> has already happened — but
    /// "in practice" is not a guarantee, and a window created before its
    /// owner exists is a crash waiting for a fast enough network. The guard
    /// costs nothing on the ordinary path: <see cref="Avalonia.Controls.TopLevel.IsVisible"/>
    /// is already true by the time this is reached, so <c>Opened</c> is
    /// never subscribed to at all.
    /// </remarks>
    private async Task ShowUpdateOffer(Available update)
    {
        if (!IsVisible)
        {
            var opened = new TaskCompletionSource();
            void OnOpened(object? sender, EventArgs args) => opened.TrySetResult();

            Opened += OnOpened;
            try
            {
                await opened.Task;
            }
            finally
            {
                Opened -= OnOpened;
            }
        }

        var offer = new UpdateOfferWindow(update, RunInstall);
        offer.Show(this);
    }

    /// <summary>
    /// Downloads and installs <paramref name="update"/>, reporting progress
    /// through the given callbacks, then closes this window so the swap
    /// script that is waiting for the process to exit can replace the
    /// bundle.
    /// </summary>
    /// <remarks>
    /// Shared by the Settings page's <c>InstallUpdate</c> (which supplies
    /// its own <c>UpdateBox</c> controls as the callbacks) and by
    /// <see cref="UpdateOfferWindow"/> (which supplies its own) — this is
    /// the one place that calls <see cref="Updater.InstallAsync"/> and
    /// decides what happens next, so the two entry points cannot drift into
    /// installing differently.
    /// </remarks>
    /// <param name="onHeadline">Where the running status text goes.</param>
    /// <param name="onProgress">The download fraction, 0 to 1.</param>
    /// <param name="onFailure">
    /// Called instead of finishing, with a message to show, if the install
    /// did not succeed. Never called on success — the window is closed
    /// instead, and there is nothing left standing to tell.
    /// </param>
    private async Task RunInstall(
        Available update,
        Action<string> onHeadline,
        Action<double> onProgress,
        Action<string> onFailure)
    {
        onHeadline($"Downloading {update.Version}…");

        var progress = new Progress<double>(fraction => onProgress(Math.Clamp(fraction, 0, 1)));

        string? failure = await Updater.InstallAsync(update, progress);

        if (failure == null)
        {
            // The swap script is waiting for this process to go away before
            // it touches the bundle, so closing is the last step of the
            // install rather than a courtesy.
            onHeadline("Installing. Jinxy will reopen by itself.");
            Close();
            return;
        }

        onFailure(failure);
    }
}
