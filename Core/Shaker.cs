using JinxyMac.Engine;

namespace JinxyMac.Core;

/// <summary>Pixels of travel allowed in each direction.</summary>
public readonly record struct ShakeRange(double Left, double Right, double Up, double Down)
{
    public bool IsZero => Left <= 0 && Right <= 0 && Up <= 0 && Down <= 0;
}

/// <summary>
/// Small random camera movement while the clicker runs.
/// </summary>
/// <remarks>
/// Shares the clicker's input gate, and that is the whole design. A mouse move
/// landing between a press and its release turns the click into a drag, and a
/// drag is not a click as far as a game is concerned. On the Windows build,
/// before the two were locked together, roughly half of every click was being
/// lost that way — the shake made the clicker worse rather than adding aim.
///
/// The wait for the gate is bounded. A low rate on a high duty cycle can hold
/// the button for most of a second, and stalling the shake outright would be
/// worse than dropping one step of it.
/// </remarks>
public sealed class Shaker : IDisposable
{
    private readonly IClickEngine _engine;
    private readonly object _inputGate;

    private CancellationTokenSource? _cts;

    // A record struct cannot be volatile, and a torn read here would be a
    // wrong-sized jump. The lock is uncontended in practice: one writer on the
    // UI thread, one reader per shake step.
    private readonly object _rangeGate = new();
    private ShakeRange _rangeValue = new(7, 9, 6, 5);
    private volatile int _movesPerSecond = 50;

    public Shaker(IClickEngine engine, object inputGate)
    {
        _engine = engine;
        _inputGate = inputGate;
    }

    private ShakeRange Range
    {
        get { lock (_rangeGate) return _rangeValue; }
        set { lock (_rangeGate) _rangeValue = value; }
    }

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public void Apply(ShakeRange range, double movesPerSecond)
    {
        Range = range;
        _movesPerSecond = (int)Math.Clamp(movesPerSecond, MinSpeed, MaxSpeed);
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        new Thread(() => Loop(token))
        {
            IsBackground = true,
            Name = "ShakeEngine"
        }.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private void Loop(CancellationToken token)
    {
        int offsetX = 0, offsetY = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                ShakeRange range = Range;

                if (range.IsZero)
                {
                    Thread.Sleep(25);
                    continue;
                }

                int targetX = Offset(-range.Left, range.Right);
                int targetY = Offset(-range.Up, range.Down);

                int dx = targetX - offsetX;
                int dy = targetY - offsetY;

                // The offsets only advance if the move actually went out, or
                // this thread's idea of where the pointer is drifts from
                // reality and the return-to-origin ends up wrong.
                if ((dx != 0 || dy != 0) && Move(dx, dy))
                {
                    offsetX = targetX;
                    offsetY = targetY;
                }

                // Jittered around the chosen rate rather than exactly on it. A
                // perfectly fixed interval is both unlike a hand and a clean
                // signature; a quarter either side keeps the average honest.
                int interval = Math.Max(1, 1000 / Math.Clamp(_movesPerSecond, MinSpeed, MaxSpeed));
                int spread = Math.Max(1, interval / 4);

                Thread.Sleep(Random.Shared.Next(Math.Max(1, interval - spread), interval + spread + 1));
            }
        }
        catch
        {
            // A failed move must never take the clicker down with it.
        }
        finally
        {
            // Undo the outstanding displacement so the crosshair ends where it
            // began, rather than wherever the last random step left it.
            if (offsetX != 0 || offsetY != 0) Move(-offsetX, -offsetY);
        }
    }

    /// <summary>
    /// Moves, but never between a press and its release.
    /// </summary>
    /// <returns>False if the click stream was busy and the step was skipped.</returns>
    private bool Move(int dx, int dy)
    {
        if (!Monitor.TryEnter(_inputGate, GateWaitMs)) return false;

        try
        {
            _engine.MoveBy(dx, dy);
            return true;
        }
        finally
        {
            Monitor.Exit(_inputGate);
        }
    }

    private static int Offset(double min, double max) =>
        (int)Math.Round(min + Random.Shared.NextDouble() * (max - min));

    public void Dispose() => Stop();

    public const int MinSpeed = 5;
    public const int MaxSpeed = 50;

    /// <summary>Longest to wait for a press to finish before skipping a step.</summary>
    private const int GateWaitMs = 120;
}
