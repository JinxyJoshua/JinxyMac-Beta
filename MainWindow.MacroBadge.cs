using System;
using System.Collections.Generic;
using Avalonia.Threading;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// Wires the on-screen macro badge to <see cref="MacroRunner.Changed"/>.
/// </summary>
/// <remarks>
/// The badge itself (<see cref="MacroBadge"/>) lives at the repo root, not
/// under <c>Core/</c> — it is an Avalonia <c>Window</c>, and <c>Core/</c>
/// must stay free of Avalonia (see <c>Core/CorePurity.Tests.cs</c>). This
/// file is the same kind of thin page-glue <c>MainWindow.Macros.cs</c> and
/// <c>MainWindow.Switcher.cs</c> already are for their own features: the
/// engine is built and tested elsewhere, and this is what connects it to a
/// window.
///
/// Unlike every other macro-related repaint in this app, this one needs no
/// call site of its own at every place a macro starts or stops — that was
/// exactly the gap <see cref="MacroRunner.Changed"/> was added to close (see
/// its own remarks: raised by <c>Start</c>, <c>Stop</c> and <c>StopAll</c>,
/// with no subscriber until now). Subscribing once here is what makes every
/// existing start/stop path — a macro's card switch, its toggle hotkey, the
/// switcher's checkbox, "Stop all", deleting a macro, closing the app — keep
/// the badge honest without any of those call sites needing to know the
/// badge exists.
/// </remarks>
public partial class MainWindow
{
    /// <summary>The badge, or null until the first macro or switcher run ever starts it.</summary>
    private MacroBadge? _macroBadge;

    /// <summary>
    /// Repaints the badge on a clock while it is up, so its sent count keeps
    /// moving between starts and stops rather than freezing at whatever it
    /// read on the last one.
    /// </summary>
    private readonly DispatcherTimer _macroBadgeTicker = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private void WireMacroBadge()
    {
        _macroBadgeTicker.Tick += (_, _) => RefreshMacroBadge();

        // MacroRunner.Changed's own remarks warn it is raised on whichever
        // thread made the change, including the hotkey poll thread — so this
        // posts to the UI thread rather than touching the badge (or
        // _macroBadgeTicker, a DispatcherTimer, which is itself not
        // thread-safe to start/stop off its owning thread) directly.
        _macros.Changed += () => Dispatcher.UIThread.Post(RefreshMacroBadge);
    }

    /// <summary>
    /// Puts the on-screen badge in step with what is actually running.
    /// </summary>
    /// <remarks>
    /// Reads <see cref="MacroRunner.BadgeLines"/> and <see cref="MacroRunner.Sent"/>
    /// straight from the runner rather than this window's own
    /// <c>_macroList</c> — the auto switcher is never in that list (see
    /// <see cref="SwitcherMacro"/>), and <c>BadgeLines</c>' own remarks are
    /// why only the runner has a current answer for it.
    ///
    /// The badge is created the first time this actually has something to
    /// show, not in the constructor — the same rule <c>EnsureMacrosBuilt</c>
    /// and <c>EnsureKitWheelBuilt</c> already follow for their own on-demand
    /// UI: someone who never runs a macro never gets a second window made on
    /// their behalf.
    /// </remarks>
    private void RefreshMacroBadge()
    {
        IReadOnlyList<string> lines = _macros.BadgeLines();

        if (lines.Count == 0)
        {
            _macroBadge?.Update(lines, _macros.Sent);
            _macroBadgeTicker.Stop();
            return;
        }

        _macroBadge ??= new MacroBadge();
        _macroBadge.Update(lines, _macros.Sent);
        _macroBadgeTicker.Start();
    }
}
