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
}
