using JinxyMac.Engine;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Records what the loop actually sent, so the pairing can be checked without
/// a mouse.
/// </summary>
internal sealed class FakeClickEngine : IClickEngine
{
    private readonly object _gate = new();
    private readonly List<(ClickButton Button, bool Down)> _events = new();

    public bool IsAvailable => true;
    public string? Unavailable => null;

    public IReadOnlyList<(ClickButton Button, bool Down)> Events
    {
        get { lock (_gate) return _events.ToList(); }
    }

    public void MouseDown(ClickButton button)
    {
        lock (_gate) _events.Add((button, true));
    }

    public void MouseUp(ClickButton button)
    {
        lock (_gate) _events.Add((button, false));
    }

    public void MoveBy(int dx, int dy) { }
}

/// <summary>
/// The click loop's pairing guarantee.
/// </summary>
/// <remarks>
/// A press and its release are sent separately. If a stop, or a change of
/// selected button, lands between them and the release names a different
/// button, the pressed one stays down across the entire desktop with nothing
/// to release it. On macOS that is a stuck context menu.
/// </remarks>
public class ClickerTests
{
    private static void RunBriefly(Clicker clicker)
    {
        clicker.Start();
        Thread.Sleep(400);
        clicker.Stop();
        Thread.Sleep(200);
    }

    [Fact]
    public void EveryPressIsReleased()
    {
        var engine = new FakeClickEngine();
        using var clicker = new Clicker(engine);

        clicker.Apply(new ClickSettings(20, 0.5, HitFix: true, Spin: false));
        RunBriefly(clicker);

        var events = engine.Events;

        Assert.NotEmpty(events);
        Assert.Equal(events.Count(e => e.Down), events.Count(e => !e.Down));
    }

    [Fact]
    public void ThePressAndItsReleaseNameTheSameButton()
    {
        var engine = new FakeClickEngine();
        using var clicker = new Clicker(engine);

        clicker.Apply(new ClickSettings(20, 0.5, true, false, ClickButton.Right));
        RunBriefly(clicker);

        var events = engine.Events;

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal(ClickButton.Right, e.Button));
    }

    /// <summary>
    /// The case that leaves a button stuck: the selector moves while a press is
    /// open. The release must name what was pressed, not what is selected now.
    /// </summary>
    [Fact]
    public void ChangingTheButtonMidRun_StillReleasesWhatWasPressed()
    {
        var engine = new FakeClickEngine();
        using var clicker = new Clicker(engine);

        clicker.Apply(new ClickSettings(8, 0.9, true, false, ClickButton.Right));
        clicker.Start();
        Thread.Sleep(150);

        clicker.Apply(new ClickSettings(8, 0.9, true, false, ClickButton.Middle));
        Thread.Sleep(400);
        clicker.Stop();
        Thread.Sleep(200);

        // Walk the log: every down must be followed by an up of the same button
        // before any other down.
        ClickButton? held = null;

        foreach ((ClickButton button, bool down) in engine.Events)
        {
            if (down)
            {
                Assert.Null(held);
                held = button;
            }
            else
            {
                Assert.Equal(held, button);
                held = null;
            }
        }

        Assert.Null(held);
    }

    /// <summary>
    /// Covers the `finally` rescue path in Loop: when the loop exits while a
    /// press is still open, the release it sends there must name the button
    /// that was actually pressed, not whatever is selected at that moment. A
    /// rescue that re-read current settings instead of the recorded press
    /// would leave a button held down across the entire desktop.
    /// </summary>
    [Fact]
    public void StoppingMidPress_RescuePathReleasesWhatWasPressed()
    {
        var engine = new FakeClickEngine();
        using var clicker = new Clicker(engine);

        // 8 CPS / 90% duty resolves, via HitFix, to roughly a 112ms press and
        // a 15ms gap -- long enough that a press is reliably still open well
        // after it starts.
        clicker.Apply(new ClickSettings(8, 0.9, true, false, ClickButton.Right));
        clicker.Start();

        // Wait until the first press is definitely open, rather than assuming
        // a fixed delay covers thread start-up.
        for (int i = 0; i < 500 && engine.Events.Count == 0; i++)
            Thread.Sleep(2);

        var opened = engine.Events;
        Assert.NotEmpty(opened);
        Assert.True(opened[0].Down);
        Assert.Equal(ClickButton.Right, opened[0].Button);

        // Give the press a moment to be solidly open -- comfortably inside
        // its ~112ms length, with generous margin either side.
        Thread.Sleep(30);

        // Switch the selection to a different button without waiting for the
        // open Right press to close, then stop immediately. This forces the
        // loop out through the `finally` rescue path while Right is still
        // held, with Middle now selected.
        clicker.Apply(new ClickSettings(8, 0.9, true, false, ClickButton.Middle));
        clicker.Stop();
        Thread.Sleep(300);

        var events = engine.Events;
        Assert.NotEmpty(events);

        // The last event in the log is the rescue release. It must name what
        // was pressed, never what is selected now.
        var last = events[^1];
        Assert.False(last.Down);
        Assert.Equal(ClickButton.Right, last.Button);
        Assert.DoesNotContain(events, e => e.Button == ClickButton.Middle);

        // And the whole log stays balanced: every down matched by an up of
        // the same button before the next down.
        ClickButton? held = null;

        foreach ((ClickButton button, bool down) in events)
        {
            if (down)
            {
                Assert.Null(held);
                held = button;
            }
            else
            {
                Assert.Equal(held, button);
                held = null;
            }
        }

        Assert.Null(held);
    }

    /// <summary>
    /// The restart race: Stop() cancels and detaches without joining, so the
    /// old thread can still be mid-press when Start() spins up a new one. If
    /// the rescue release in Loop's `finally` were not gated the same as the
    /// ordinary release, the old thread's release could land between the new
    /// thread's gated MouseDown and MouseUp — turning that click into a drag.
    /// </summary>
    /// <remarks>
    /// This is a deterministic regression test, not a probabilistic one: it
    /// does not rely on hitting a narrow timing window by chance. Start()
    /// joins the thread the previous Stop() left running (bounded, but the
    /// token is already cancelled by then so the join is normally immediate)
    /// before a new thread can exist, so there is structurally never more
    /// than one Loop thread alive per Clicker. That makes the invariant below
    /// — no down ever follows another down without an intervening up of the
    /// same button — true on every run, not just probably true, which is why
    /// this is worth asserting rather than only exercising.
    /// </remarks>
    [Fact]
    public void StopThenImmediateRestart_NeverOverlapsMouseState()
    {
        var engine = new FakeClickEngine();
        using var clicker = new Clicker(engine);

        // 8 CPS / 90% duty resolves, via HitFix, to a long press relative to
        // the gap, so stopping soon after start reliably lands mid-press.
        clicker.Apply(new ClickSettings(8, 0.9, true, false, ClickButton.Right));
        clicker.Start();

        for (int i = 0; i < 500 && engine.Events.Count == 0; i++)
            Thread.Sleep(2);

        Assert.NotEmpty(engine.Events);
        Thread.Sleep(30); // comfortably inside the open press

        // Stop and restart back-to-back, with nothing in between -- exactly
        // the window finding 1 describes.
        clicker.Stop();
        clicker.Start();

        Thread.Sleep(400);
        clicker.Stop();
        Thread.Sleep(200);

        var events = engine.Events;
        Assert.NotEmpty(events);

        ClickButton? held = null;

        foreach ((ClickButton button, bool down) in events)
        {
            if (down)
            {
                Assert.Null(held);
                held = button;
            }
            else
            {
                Assert.Equal(held, button);
                held = null;
            }
        }

        Assert.Null(held);
    }
}
