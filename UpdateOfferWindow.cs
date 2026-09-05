using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// The launch-time update prompt: names what is available and what
/// installing it costs, then waits for a choice.
/// </summary>
/// <remarks>
/// Built entirely in code, the way <see cref="MacroBadge"/> is — a
/// two-button dialog does not earn a XAML file of its own, and
/// <c>this.FindResource(...)</c> is the same trick <c>MacroBadge</c> uses to
/// pick up <c>MainWindow.axaml</c>'s palette without declaring any resources
/// of its own.
///
/// <para>
/// This is the only place a launch-time update is ever offered. The
/// Settings page keeps its own copy of this box (<c>UpdateBox</c> in
/// <c>MainWindow.axaml</c>, wired in <c>MainWindow.axaml.cs</c>'s
/// <c>WireUpdates</c>) as the manual "Check now" path, where the person is
/// already looking at it — this window exists because the launch check
/// finds updates while the person is very possibly on the Clicker page
/// instead, where that box is invisible. See <c>MainWindow.UpdateOffer.cs</c>
/// for how the two are kept from stepping on each other.
/// </para>
/// </remarks>
public sealed class UpdateOfferWindow : Window
{
    private readonly Available _update;
    private readonly Func<Available, Action<string>, Action<double>, Action<string>, Task> _install;

    private readonly TextBlock _headline;
    private readonly TextBlock _notes;
    private readonly ProgressBar _progress;
    private readonly Button _installButton;
    private readonly Button _laterButton;

    /// <param name="update">What the feed reported.</param>
    /// <param name="install">
    /// Runs the actual download-and-swap — <c>MainWindow.RunInstall</c>,
    /// passed in rather than called directly so this window does not need
    /// to know about <see cref="Updater.InstallAsync"/> or that finishing
    /// means closing the main window, only that it reports a headline, a
    /// fraction, and — if something goes wrong — a failure message instead
    /// of ever completing.
    /// </param>
    public UpdateOfferWindow(
        Available update,
        Func<Available, Action<string>, Action<double>, Action<string>, Task> install)
    {
        _update = update;
        _install = install;

        Title = "Update available";
        CanResize = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 440;
        Background = this.FindResource("Backdrop") as IBrush;
        Foreground = this.FindResource("Text") as IBrush;

        var eyebrow = new TextBlock
        {
            Text = "UPDATE AVAILABLE",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = this.FindResource("TextMuted") as IBrush
        };

        _headline = new TextBlock
        {
            Text = UpdateOffer.Headline(update),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = this.FindResource("TextBright") as IBrush,
            Margin = new Thickness(0, 6, 0, 0)
        };

        _notes = new TextBlock
        {
            Text = UpdateOffer.Notes(update.Notes),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = this.FindResource("TextMuted") as IBrush,
            Margin = new Thickness(0, 10, 0, 0),
            IsVisible = update.Notes.Trim().Length > 0
        };

        // The one line the offer must never omit — see Updater's own
        // remarks for why an unsigned app cannot update silently.
        var warning = new TextBlock
        {
            Text = UpdateOffer.AccessibilityWarning,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = this.FindResource("Accent") as IBrush,
            Margin = new Thickness(0, 14, 0, 0)
        };

        _progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Height = 4,
            Margin = new Thickness(0, 16, 0, 0),
            Foreground = this.FindResource("Accent") as IBrush,
            IsVisible = false
        };

        _installButton = new Button
        {
            Content = "Install and restart",
            Padding = new Thickness(16, 7),
            FontWeight = FontWeight.SemiBold,
            Background = this.FindResource("Accent") as IBrush,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _installButton.Click += async (_, _) => await OnInstall();

        _laterButton = new Button
        {
            Content = "Not now",
            Padding = new Thickness(14, 6),
            FontSize = 11
        };
        _laterButton.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 18, 0, 0),
            Children = { _installButton, _laterButton }
        };

        var body = new StackPanel
        {
            Children = { eyebrow, _headline, _notes, warning, _progress, buttons }
        };

        // The same card treatment the rest of the window uses (see
        // Border.card in MainWindow.axaml) — this reads as part of JinxyMac
        // rather than a system dialog dropped on top of it.
        Content = new Border
        {
            Background = this.FindResource("Panel") as IBrush,
            BorderBrush = this.FindResource("Divider") as IBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24),
            Child = body
        };
    }

    /// <summary>
    /// Runs the install, disabling both buttons for as long as it can still
    /// be cancelled by nothing in particular happening — there is no cancel
    /// button once this starts, because a half-downloaded update sitting in
    /// temp is not a state worth building a path back out of.
    /// </summary>
    private async Task OnInstall()
    {
        _installButton.IsEnabled = false;
        _laterButton.IsEnabled = false;
        _progress.IsVisible = true;
        _progress.Value = 0;

        await _install(
            _update,
            text => _headline.Text = text,
            fraction => _progress.Value = fraction,
            failure =>
            {
                _progress.IsVisible = false;
                _headline.Text = failure;
                _installButton.IsEnabled = true;
                _laterButton.IsEnabled = true;
            });

        // No else branch for success: the callback's contract (see
        // MainWindow.RunInstall) is that success closes the main window,
        // which takes this one with it — there is nothing left here to
        // update.
    }
}
