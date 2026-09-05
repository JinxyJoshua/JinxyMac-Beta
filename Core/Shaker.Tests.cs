using JinxyMac.Engine;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Records every MoveBy the loop actually sent, so the net displacement can
/// be checked without a mouse.
/// </summary>
internal sealed class FakeShakeEngine : IClickEngine
{
    private readonly object _gate = new();
    private readonly List<(int Dx, int Dy)> _moves = new();

    public bool IsAvailable => true;
    public string? Unavailable => null;

    public IReadOnlyList<(int Dx, int Dy)> Moves
    {
        get { lock (_gate) return _moves.ToList(); }
    }

    /// <summary>Sum of every dx and dy sent so far -- zero means "back at origin".</summary>
    public (int X, int Y) NetOffset
    {
        get
        {
            lock (_gate)
            {
                int x = 0, y = 0;
                foreach ((int dx, int dy) in _moves) { x += dx; y += dy; }
                return (x, y);
            }
        }
    }

    public void MouseDown(ClickButton button) { }

    public void MouseUp(ClickButton button) { }

    public void MoveBy(int dx, int dy)
    {
        lock (_gate) _moves.Add((dx, dy));
    }
}

/// <summary>
/// The shaker's return-to-origin guarantee, and the bookkeeping a fast
/// restart must not corrupt.
/// </summary>
public class ShakerTests
{
    private static void WaitForOrigin(FakeShakeEngine engine)
    {
        for (int i = 0; i < 500; i++)
        {
            (int x, int y) = engine.NetOffset;
            if (x == 0 && y == 0) return;
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// Covers the dropped return in Loop's `finally`: Move can time out on the
    /// gate (Monitor.TryEnter with a 120ms bound) exactly when the gate is
    /// busy the longest -- a low CPS with a high duty cycle can hold it for
    /// close to a second. A `finally` that only tries once would leave the
    /// crosshair displaced. This holds the gate itself, from another thread,
    /// well past that 120ms bound, then confirms the origin is still restored
    /// once the gate frees up -- proving the fix retries rather than only
    /// exercising the happy path.
    /// </summary>
    [Fact]
    public void StoppingWhileGateIsHeld_StillReturnsToOrigin()
    {
        var engine = new FakeShakeEngine();
        var gate = new object();
        using var shaker = new Shaker(engine, gate);

        shaker.Apply(new ShakeRange(7, 9, 6, 5), 50);
        shaker.Start();

        // Let it accumulate a real, non-zero offset before stopping.
        Thread.Sleep(150);
        Assert.NotEqual((0, 0), engine.NetOffset);

        // Hold the gate the way a long click hold would -- well past
        // Shaker's 120ms GateWaitMs -- while Stop() runs, so the `finally`
        // undo's first attempt (and likely its first few retries) must fail.
        lock (gate)
        {
            shaker.Stop();
            Thread.Sleep(250);
        }

        // Once the gate is free, the retry must eventually land.
        WaitForOrigin(engine);
        Assert.Equal((0, 0), engine.NetOffset);
    }

    /// <summary>
    /// The restart race: Stop() cancels and detaches without joining, so a
    /// fast Stop()-then-Start() can leave two Loop threads alive whose
    /// origin bookkeeping (offsetX/offsetY, both local to Loop) disagrees --
    /// the old thread's `finally` undo subtracts an offset the new thread's
    /// fresh, zeroed locals never re-created.
    /// </summary>
    /// <remarks>
    /// Deterministic, not probabilistic, for the same reason as the
    /// equivalent Clicker test: Start() joins the outgoing thread before a
    /// new one can exist, so there is structurally never more than one Loop
    /// thread alive per Shaker. The net offset returning to exactly zero
    /// after two restarts is therefore guaranteed, not merely likely.
    /// </remarks>
    [Fact]
    public void StopThenImmediateRestart_OriginBookkeepingStaysConsistent()
    {
        var engine = new FakeShakeEngine();
        var gate = new object();
        using var shaker = new Shaker(engine, gate);

        shaker.Apply(new ShakeRange(7, 9, 6, 5), 50);
        shaker.Start();
        Thread.Sleep(150);

        // Back-to-back, with nothing in between -- exactly the window
        // finding 3 describes.
        shaker.Stop();
        shaker.Start();

        Thread.Sleep(150);
        shaker.Stop();

        WaitForOrigin(engine);
        Assert.Equal((0, 0), engine.NetOffset);
    }

    /// <summary>
    /// A second, stronger check on the same restart race as
    /// <see cref="StopThenImmediateRestart_OriginBookkeepingStaysConsistent"/>.
    /// </summary>
    /// <remarks>
    /// The final net offset returning to zero is necessary but not
    /// sufficient: each Loop call's offsetX/offsetY is local, every MoveBy
    /// delta is additive, and each thread's own `finally` always undoes
    /// exactly its own contribution — so the final sum lands back on zero
    /// whether or not two threads ever overlapped. That invariant alone
    /// cannot tell a clean restart from two racing threads.
    ///
    /// What two overlapping threads actually do is transiently push the
    /// cursor further than one shaker's configured range should ever allow:
    /// a new thread's Loop starts believing the cursor sits at its own
    /// fresh (0, 0), and if the old thread has not yet undone its own
    /// displacement, the two contributions stack. Tracking the cumulative
    /// offset implied by every MoveBy call, in order, and asserting it never
    /// exceeds the configured box catches exactly that stacking. Confirmed
    /// against this fix: with Start()'s join temporarily removed, this
    /// failed on roughly half of a handful of runs (seen: maxAbsX 14 against
    /// a bound of 10, maxAbsY 8 against a bound of 7); with the join in
    /// place it has not failed once.
    /// </remarks>
    [Fact]
    public void StopThenImmediateRestart_NeverExceedsConfiguredRange()
    {
        var engine = new FakeShakeEngine();
        var gate = new object();
        using var shaker = new Shaker(engine, gate);

        var range = new ShakeRange(7, 9, 6, 5);
        shaker.Apply(range, 50);
        shaker.Start();
        Thread.Sleep(150);

        // Back-to-back, with nothing in between -- exactly the window
        // finding 3 describes.
        shaker.Stop();
        shaker.Start();

        Thread.Sleep(150);
        shaker.Stop();
        Thread.Sleep(300);

        int x = 0, y = 0, maxAbsX = 0, maxAbsY = 0;

        foreach ((int dx, int dy) in engine.Moves)
        {
            x += dx;
            y += dy;
            maxAbsX = Math.Max(maxAbsX, Math.Abs(x));
            maxAbsY = Math.Max(maxAbsY, Math.Abs(y));
        }

        // +1 for rounding slack in Offset's Math.Round -- a single shaker
        // never legitimately exceeds its configured box by more than that.
        Assert.True(maxAbsX <= Math.Max(range.Left, range.Right) + 1, $"maxAbsX={maxAbsX}");
        Assert.True(maxAbsY <= Math.Max(range.Up, range.Down) + 1, $"maxAbsY={maxAbsY}");
    }
}
