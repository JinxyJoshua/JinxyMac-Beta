using JinxyMac.Engine;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Records what the loop actually sent, so the pairing can be checked without
/// a keyboard.
/// </summary>
internal sealed class FakeKeyEngine : IKeyEngine
{
    private readonly object _gate = new();
    private readonly List<(int Code, bool Down)> _events = new();

    public bool IsAvailable => true;
    public string? Unavailable => null;

    public IReadOnlyList<(int Code, bool Down)> Events
    {
        get { lock (_gate) return _events.ToList(); }
    }

    public void KeyDown(int code)
    {
        lock (_gate) _events.Add((code, true));
    }

    public void KeyUp(int code)
    {
        lock (_gate) _events.Add((code, false));
    }
}

/// <summary>
/// The macro runner's send loop, and the pairing guarantee that matters most:
/// a key that goes down must come back up, even when a stop lands mid-press.
/// </summary>
/// <remarks>
/// Mirrors <see cref="ClickerTests"/>'s shape for the mouse button — a fake
/// engine recording events, and the same walk-the-log style of assertion —
/// because a key left held down in a game is the keyboard equivalent of the
/// stuck-mouse-button bug that suite exists to catch.
/// </remarks>
public class MacroRunnerTests
{
    [Fact]
    public void StartedMacroSendsItsKey()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("Macro", new[] { 0x52 }, "R", 30);
        runner.Start(macro);

        Thread.Sleep(300);
        runner.Stop("Macro");
        Thread.Sleep(150);

        var events = engine.Events;

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal(0x52, e.Code));
    }

    [Fact]
    public void TwoKeyMacroAlternatesBetweenThem()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("Switcher", new[] { 0x31, 0x32 }, "1, 2", 30);
        runner.Start(macro);

        Thread.Sleep(400);
        runner.Stop("Switcher");
        Thread.Sleep(150);

        List<int> downs = engine.Events.Where(e => e.Down).Select(e => e.Code).ToList();

        Assert.True(downs.Count >= 2, $"only {downs.Count} presses landed; nothing to alternate");
        Assert.All(downs, code => Assert.Contains(code, macro.Keys));

        for (int i = 1; i < downs.Count; i++)
            Assert.NotEqual(downs[i - 1], downs[i]);
    }

    [Fact]
    public void StopEndsItAndEveryKeyThatWentDownCameBackUp()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("Macro", new[] { 0x52 }, "R", 25);
        runner.Start(macro);

        Thread.Sleep(300);
        runner.Stop("Macro");
        Thread.Sleep(150);

        Assert.False(runner.IsRunning("Macro"));

        var events = engine.Events;

        Assert.NotEmpty(events);
        Assert.Equal(events.Count(e => e.Down), events.Count(e => !e.Down));
    }

    [Fact]
    public void StopAllEndsSeveralRunningMacros()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var first = new KeyMacro("First", new[] { 0x31 }, "1", 25);
        var second = new KeyMacro("Second", new[] { 0x32 }, "2", 25);
        var third = new KeyMacro("Third", new[] { 0x33 }, "3", 25);

        runner.Start(first);
        runner.Start(second);
        runner.Start(third);

        Assert.Equal(3, runner.RunningCount);

        Thread.Sleep(300);
        runner.StopAll();
        Thread.Sleep(150);

        Assert.Equal(0, runner.RunningCount);
        Assert.False(runner.IsRunning("First"));
        Assert.False(runner.IsRunning("Second"));
        Assert.False(runner.IsRunning("Third"));

        var events = engine.Events;

        Assert.NotEmpty(events);
        Assert.Equal(events.Count(e => e.Down), events.Count(e => !e.Down));
    }

    [Fact]
    public void DisabledMacroRefusesToStart()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("Off", new[] { 0x52 }, "R", 25, enabled: false);
        runner.Start(macro);

        Thread.Sleep(100);

        Assert.False(runner.IsRunning("Off"));
        Assert.Equal(0, runner.RunningCount);
        Assert.Empty(engine.Events);
    }

    [Fact]
    public void UnusableMacroWithNoKeysRefusesToStart()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("NoKeys", Array.Empty<int>(), "", 25);
        Assert.False(macro.IsUsable);

        runner.Start(macro);
        Thread.Sleep(100);

        Assert.False(runner.IsRunning("NoKeys"));
        Assert.Equal(0, runner.RunningCount);
        Assert.Empty(engine.Events);
    }

    [Fact]
    public void UnusableMacroWithBlankNameRefusesToStart()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("   ", new[] { 0x52 }, "R", 25);
        Assert.False(macro.IsUsable);

        runner.Start(macro);
        Thread.Sleep(100);

        Assert.False(runner.IsRunning("   "));
        Assert.Equal(0, runner.RunningCount);
        Assert.Empty(engine.Events);
    }

    /// <summary>
    /// The same walk that catches the stuck mouse button, adapted for a key: a
    /// stop that lands while a press is open must not leave that key down.
    /// SendGated always sends its own release regardless of what the wait in
    /// the middle observed, so this is a regression guard on that pairing
    /// rather than a demonstration that it can fail today.
    /// </summary>
    [Fact]
    public void StoppingMidPressStillReleasesTheKey()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("Macro", new[] { 0x52 }, "R", 200);
        runner.Start(macro);

        // Wait until the first press is definitely open, rather than assuming
        // a fixed delay covers thread start-up.
        for (int i = 0; i < 500 && engine.Events.Count == 0; i++)
            Thread.Sleep(2);

        var opened = engine.Events;
        Assert.NotEmpty(opened);
        Assert.True(opened[0].Down);
        Assert.Equal(0x52, opened[0].Code);

        // Stop as close to the open press as we can land. The invariant below
        // must hold whether this lands inside the hold or after it.
        runner.Stop("Macro");
        Thread.Sleep(250);

        var events = engine.Events;
        Assert.NotEmpty(events);

        // Walk the log: every down must be followed by an up of the same code
        // before any other down, and nothing is left open at the end.
        int? held = null;

        foreach ((int code, bool down) in events)
        {
            if (down)
            {
                Assert.Null(held);
                held = code;
            }
            else
            {
                Assert.Equal(held, code);
                held = null;
            }
        }

        Assert.Null(held);
    }

    /// <summary>
    /// The collision guard <c>MainWindow.axaml.cs</c>'s <c>Fire</c> needs:
    /// a running macro's own output must be discoverable, so a hotkey watcher
    /// that observes this app's own synthetic key events (as
    /// <c>MacHotkeyWatcher.Poll</c> does on macOS) can tell them apart from a
    /// person's keypress.
    /// </summary>
    [Fact]
    public void RunningKeysReportsARunningMacrosOutput()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("Switcher", new[] { 0x31, 0x32 }, "1, 2", 25);
        runner.Start(macro);

        Assert.Contains(0x31, runner.RunningKeys());
        Assert.Contains(0x32, runner.RunningKeys());

        runner.Stop("Switcher");
        Thread.Sleep(100);
    }

    [Fact]
    public void RunningKeysDropsAStoppedMacrosOutput()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("Macro", new[] { 0x52 }, "R", 25);
        runner.Start(macro);
        Assert.Contains(0x52, runner.RunningKeys());

        runner.Stop("Macro");
        Thread.Sleep(100);

        Assert.DoesNotContain(0x52, runner.RunningKeys());
    }

    [Fact]
    public void RunningKeysNeverReportsADisabledMacrosOutput()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("Off", new[] { 0x52 }, "R", 25, enabled: false);
        runner.Start(macro);

        Assert.Empty(runner.RunningKeys());
    }

    /// <summary>
    /// The line the on-screen badge shows for an ordinary running macro: its
    /// keys, not the name it was saved under.
    /// </summary>
    [Fact]
    public void BadgeLinesShowsAnOrdinaryMacrosKeysNotItsName()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("Sword Spam", new[] { 0x52 }, "R", 25);
        runner.Start(macro);

        Assert.Equal(new[] { "R" }, runner.BadgeLines());

        runner.Stop("Sword Spam");
    }

    /// <summary>
    /// The auto switcher runs under <see cref="SwitcherMacro.Name"/> — a
    /// reserved, leading-space internal name nobody should ever see on
    /// screen. <see cref="MacroRunner.BadgeLines"/> must label it rather than
    /// leak that name or show its keys with no label at all.
    /// </summary>
    [Fact]
    public void BadgeLinesLabelsTheAutoSwitcherRatherThanItsInternalName()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var switcher = new KeyMacro(SwitcherMacro.Name, new[] { 0x31, 0x32 }, "1, 2", 25);
        runner.Start(switcher);

        string line = Assert.Single(runner.BadgeLines());
        Assert.DoesNotContain(SwitcherMacro.Name, line);
        Assert.Contains("1, 2", line);

        runner.Stop(SwitcherMacro.Name);
    }

    [Fact]
    public void BadgeLinesOmitsAStoppedMacro()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var macro = new KeyMacro("Macro", new[] { 0x52 }, "R", 25);
        runner.Start(macro);
        Assert.NotEmpty(runner.BadgeLines());

        runner.Stop("Macro");

        Assert.Empty(runner.BadgeLines());
    }

    [Fact]
    public void BadgeLinesListsEveryRunningMacro()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        var first = new KeyMacro("First", new[] { 0x31 }, "1", 25);
        var second = new KeyMacro("Second", new[] { 0x32 }, "2", 25);

        runner.Start(first);
        runner.Start(second);

        Assert.Equal(2, runner.BadgeLines().Count);
        Assert.Contains("1", runner.BadgeLines());
        Assert.Contains("2", runner.BadgeLines());

        runner.StopAll();
    }

    /// <summary>
    /// <see cref="MacroRunner.Suppressed"/> is what stops a macro typing into
    /// this app's own window (<c>MainWindow</c> wires it to a cached
    /// <c>IsActive</c>). This is the guarantee that matters: nothing sent
    /// while it reads true, sending resumes once it reads false, and — the
    /// subtle part — the cycle keeps advancing the whole time it was
    /// suppressed, so a two-key macro does not resume stuck resending the key
    /// it was on when suppression began.
    /// </summary>
    /// <remarks>
    /// Suppression is lifted by a call count on <c>Suppressed</c> itself
    /// rather than after a fixed sleep, so which key resumes first is
    /// deterministic instead of a race against the dwell timer: the callback
    /// is invoked exactly once per loop cycle (see <c>Loop</c>), so returning
    /// true for the first three calls and false after guarantees three
    /// suppressed cycles have elapsed — an odd number, which lands a two-key
    /// macro's cursor back on index 1 by the time it resumes. If the cycle
    /// did not advance while suppressed, resuming would instead resend index
    /// 0 (0x31) — the key it was on when suppression began.
    /// </remarks>
    [Fact]
    public void SuppressedMacroSendsNothingThenResumesWithoutStickingOnTheStartingKey()
    {
        var engine = new FakeKeyEngine();
        using var runner = new MacroRunner(engine);

        long calls = 0;
        runner.Suppressed = () => Interlocked.Increment(ref calls) <= 3;

        var macro = new KeyMacro("Switcher", new[] { 0x31, 0x32 }, "1, 2", 30);
        runner.Start(macro);

        // Wait for the third suppressed cycle to be evaluated, but no
        // further — the fourth call is the one that lifts suppression and
        // sends, so checking immediately after the third keeps the empty
        // assertion below out of a race with it.
        for (int i = 0; i < 500 && Interlocked.Read(ref calls) < 3; i++)
            Thread.Sleep(2);

        Assert.Empty(engine.Events);

        // Wait for the resumed send. Generous: the dwell after the third
        // suppressed cycle must fully elapse before the fourth call fires.
        for (int i = 0; i < 500 && engine.Events.Count == 0; i++)
            Thread.Sleep(2);

        runner.Stop("Switcher");
        Thread.Sleep(150);

        var events = engine.Events;
        Assert.NotEmpty(events);

        // The first key to actually land is the second slot, not the first —
        // proof the cursor moved on while nothing was sent, rather than
        // sitting on index 0 for the whole suppressed stretch.
        Assert.True(events[0].Down);
        Assert.Equal(0x32, events[0].Code);
    }

    // ---- guarding a rebind capture against a macro's own output ----
    //
    // Both platform hotkey watchers capture a rebind by scanning raw key
    // state with no notion of what a macro is, so a running macro's own
    // synthetic key would be captured as the new binding. CaptureBlockedReason
    // is the refusal MainWindow.axaml.cs's Bind and MainWindow.Macros.cs's
    // BindMacroHotkey both check before ever calling IHotkeyWatcher.CaptureNext.

    [Fact]
    public void CaptureBlockedReasonAllowsCapturingWhenNothingIsRunning()
    {
        Assert.Null(MacroRunner.CaptureBlockedReason(0));
    }

    [Fact]
    public void CaptureBlockedReasonRefusesWhenOneMacroIsRunning()
    {
        string? reason = MacroRunner.CaptureBlockedReason(1);

        Assert.NotNull(reason);
        Assert.Contains("macro", reason);
    }

    [Fact]
    public void CaptureBlockedReasonCountsHowManyAreRunning()
    {
        string? reason = MacroRunner.CaptureBlockedReason(3);

        Assert.NotNull(reason);
        Assert.Contains("3", reason);
    }

    [Fact]
    public void CaptureBlockedReasonAllowsANegativeCountTheSameAsZero()
    {
        Assert.Null(MacroRunner.CaptureBlockedReason(-1));
    }
}
