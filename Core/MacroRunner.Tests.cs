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
}
