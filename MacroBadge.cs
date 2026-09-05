using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace JinxyMac;

/// <summary>
/// A badge in the corner of the screen for as long as a macro or the auto
/// switcher is running.
/// </summary>
/// <remarks>
/// A macro holds keys down in a game that binds those same keys to its own
/// actions, and a macro left running on F while you are trying to play is
/// indistinguishable from the game misbehaving. The badge names the keys it
/// is pressing, so the answer to "why is my F doing that" is on screen
/// rather than three tabs deep in an app that is behind the game.
///
/// Unlike the capture toast, this deliberately does not fade. A macro can run
/// for a long time — an entire session — and the badge has to stay up for
/// all of it; fading it out after a few seconds would defeat the one thing
/// it exists to do.
///
/// It never takes focus (<see cref="Window.ShowActivated"/> is false, and it
/// is shown with <see cref="Window.Show()"/> rather than any method that
/// activates) and is always on top (<see cref="Window.Topmost"/>), so it
/// cannot steal a click or pull the game out of the foreground by appearing.
///
/// <b>Click-through is not implemented, and this is a real limitation, not an
/// oversight.</b> The Windows original sets <c>WS_EX_TRANSPARENT</c> so the
/// badge cannot absorb a click meant for the game underneath it.
/// Avalonia 11.3's public surface — <see cref="Window"/>, and the
/// <c>IWindowImpl</c>/<c>ITopLevelImpl</c> interfaces a platform backend
/// implements — has no cross-platform equivalent (checked directly, by
/// reflecting over both interfaces' full member lists: nothing there sets a
/// window transparent to input). <see cref="InputElement.IsHitTestVisible"/>,
/// set false below, is the closest thing on offer, but it only turns off
/// this window's own hit-testing of its own content on the way *in* — it
/// cannot make the desktop compositor deliver a click to whatever sits
/// behind this window instead of to this window. The macOS mechanism that
/// would actually do it, <c>NSWindow.ignoresMouseEvents</c>, is reachable
/// only through Objective-C runtime interop, which this project has
/// deliberately avoided everywhere else — so this badge ships without it
/// rather than adding the one Obj-C call the rest of the app has done
/// without. It sits small and out of the way in a corner, but a click that
/// lands on it will not reach the game.
///
/// Streamer mode — hiding the badge from OBS/Discord capture while leaving it
/// visible on the monitor, via Windows' <c>SetWindowDisplayAffinity</c> — has
/// no macOS equivalent and is out of scope for this port. This badge is
/// always visible to whatever is capturing the screen, the same as it is to
/// the person at the keyboard.
/// </remarks>
public sealed class MacroBadge : Window
{
    private readonly TextBlock _label;

    public MacroBadge()
    {
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        // Transparent everywhere the content border does not paint, so the
        // badge reads as a small rounded chip rather than a stray rectangle
        // sitting over the game.
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        // Turns off this window's own hit-testing of its content. It does
        // not achieve OS-level click-through — see the class remarks — but
        // there is no reason for this control to swallow the one click it
        // is able to see.
        IsHitTestVisible = false;

        _label = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = this.FindResource("TextBright") as IBrush
        };

        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = this.FindResource("Accent") as IBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(_label);

        // The same card treatment the rest of the window uses (see
        // Border.card in MainWindow.axaml), so this reads as part of
        // JinxyMac rather than a system tooltip dropped on top of it.
        Content = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = this.FindResource("Panel") as IBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = this.FindResource("Divider") as IBrush,
            Padding = new Thickness(14, 9, 16, 9),
            Child = row
        };
    }

    /// <summary>
    /// Shows the badge naming what is running and how many presses have
    /// actually landed, or hides it once nothing is.
    /// </summary>
    /// <param name="lines">
    /// One entry per running macro — see
    /// <see cref="Core.MacroRunner.BadgeLines"/>, which is what builds these
    /// and already picks keys over names, labelling the auto switcher rather
    /// than leaking its reserved internal name.
    /// </param>
    /// <param name="sent">
    /// <see cref="Core.MacroRunner.Sent"/> — the running total of key
    /// presses this runner has actually delivered. Shown so that a number
    /// which stops climbing is the visible difference between "suppressed
    /// because this app's own window is focused" and "not working at all" —
    /// see that property's own remarks.
    /// </param>
    public void Update(IReadOnlyList<string> lines, long sent)
    {
        if (lines.Count == 0)
        {
            if (IsVisible) Hide();
            return;
        }

        string what = string.Join("   ", lines);
        string heading = lines.Count == 1 ? "MACRO ON" : "MACROS ON";

        _label.Text = $"{heading}   {what}    ·    {sent:N0} sent";

        if (!IsVisible) Show();

        PlaceTopLeft();
    }

    private void PlaceTopLeft()
    {
        const int margin = 24;

        Screen? screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null) return;

        Position = new PixelPoint(screen.WorkingArea.X + margin, screen.WorkingArea.Y + margin);
    }
}
