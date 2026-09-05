using System.Diagnostics;
using JinxyMac.Engine;

namespace JinxyMac.Core;

/// <summary>Everything the click loop reads, as one snapshot.</summary>
/// <remarks>
/// A record passed by reference rather than fields the loop reaches for. UI
/// controls have thread affinity, and the loop must never touch them.
/// </remarks>
public sealed record ClickSettings(
    double Cps, double Duty, bool HitFix, bool Spin, ClickButton Button = ClickButton.Left)
{
    public ClickTiming Timing => ClickTimings.Resolve(Cps, Duty, HitFix);
}

/// <summary>
/// The click loop.
/// </summary>
/// <remarks>
/// Carries across the two corrections that mattered most in the Windows app,
/// because both were found by measurement and neither is obvious:
///
/// A stall must not discard the clicks it cost. Resetting the schedule to now
/// after falling behind measured 27.1 clicks a second against a 32/sec setting;
/// allowing a bounded catch-up brought it to 31.0 with the same stalls.
///
/// The press and its release are one unit. Anything injected between them turns
/// the click into a drag, and a drag is not a click as far as a game is
/// concerned — with shake running, roughly half of every click was being lost
/// that way before the two were locked together.
/// </remarks>
public sealed class Clicker : IDisposable
{
    private readonly IClickEngine _engine;
    private readonly object _inputGate = new();

    private CancellationTokenSource? _cts;
    private long _clicks;
    private volatile ClickSettings _settings = new(10, 0.5, HitFix: true, Spin: false);

    public Clicker(IClickEngine engine) => _engine = engine;

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    /// <summary>Clicks actually delivered, which is not the same as the slider.</summary>
    public long ClickCount => Interlocked.Read(ref _clicks);

    /// <summary>Held while the button is down, so shake cannot split a click.</summary>
    public object InputGate => _inputGate;

    public void Apply(ClickSettings settings) => _settings = settings;

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        new Thread(() => Loop(token))
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "ClickEngine"
        }.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private void Loop(CancellationToken token)
    {
        // The button actually pressed, which is not necessarily the one
        // selected now. A release naming a different button leaves the pressed
        // one down across the whole desktop with nothing to release it.
        ClickButton? held = null;
        long freq = Stopwatch.Frequency;
        long deadline = Stopwatch.GetTimestamp();

        try
        {
            while (!token.IsCancellationRequested)
            {
                ClickSettings s = _settings;

                if (s.Cps < MinimumCps)
                {
                    Thread.Sleep(50);
                    deadline = Stopwatch.GetTimestamp();
                    continue;
                }

                ClickTiming timing = s.Timing;
                double period = timing.PeriodMs;
                double downMs = timing.DownMs;

                // Bounded catch-up rather than a reset. The bound is what stops
                // a long stall becoming one long burst.
                long now = Stopwatch.GetTimestamp();
                long maxLag = (long)(period * CatchUpPeriods * freq / 1000.0);
                if (deadline < now - maxLag) deadline = now - maxLag;

                bool cancelled;

                lock (_inputGate)
                {
                    ClickButton pressing = s.Button;

                    _engine.MouseDown(pressing);
                    held = pressing;
                    deadline += (long)(downMs * freq / 1000.0);

                    cancelled = !WaitUntil(deadline, s.Spin, token);

                    if (!cancelled)
                    {
                        _engine.MouseUp(pressing);
                        held = null;
                        Interlocked.Increment(ref _clicks);
                    }
                }

                if (cancelled) break;

                // Outside the lock: the gap is when shake is free to move, and
                // it is most of the cycle.
                deadline += (long)((period - downMs) * freq / 1000.0);
                if (!WaitUntil(deadline, s.Spin, token)) break;
            }
        }
        catch
        {
            // A failure here must not leave the button held across the desktop.
        }
        finally
        {
            // The one that was pressed, whatever is selected now.
            //
            // This release only runs because a send already threw, and the
            // usual reason — Accessibility permission revoked mid-run — is
            // still true here, so the release is likely to throw the same
            // way. Guarded because that second throw has nowhere left to go
            // but out of this background thread, which kills the process —
            // exactly what the catch above exists to prevent.
            if (held is ClickButton stuck)
            {
                try { _engine.MouseUp(stuck); }
                catch
                {
                    // See above: the release can fail for the same reason
                    // the send did, and must not be allowed to take the
                    // process down with it.
                }
            }
        }
    }

    /// <summary>
    /// Waits until an absolute timestamp, so time spent sending input cannot
    /// compound into drift.
    /// </summary>
    private static bool WaitUntil(long target, bool spin, CancellationToken token)
    {
        long freq = Stopwatch.Frequency;

        while (true)
        {
            if (token.IsCancellationRequested) return false;

            long remaining = target - Stopwatch.GetTimestamp();
            if (remaining <= 0) return true;

            double ms = remaining * 1000.0 / freq;

            if (!spin)
            {
                int whole = (int)ms;
                if (whole <= 0) return true;

                // Sliced: at a low rate a single hold runs to seconds, and an
                // uninterruptible sleep there would keep the button physically
                // down that long after a stop.
                Thread.Sleep(Math.Min(whole, CoarseSleepSliceMs));
                continue;
            }

            if (ms > 2.0) Thread.Sleep(1);
            else Thread.SpinWait(40);
        }
    }

    public void Dispose() => Stop();

    /// <summary>Below this the slider means "armed but not clicking".</summary>
    public const double MinimumCps = 0.5;

    /// <summary>
    /// The building hotkey's fixed rate. Not adjustable by design: it is a
    /// specific slow, long press that places blocks reliably, and a slider next
    /// to it would only be a way to break it.
    /// </summary>
    public const double BuildCps = 35.0;

    /// <summary>Duty cycle for the building rate, as a fraction.</summary>
    public const double BuildDuty = 0.01;

    private const double CatchUpPeriods = 4.0;
    private const int CoarseSleepSliceMs = 20;
}
