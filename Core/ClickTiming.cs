using System;

namespace JinxyMac.Core;

/// <summary>
/// The press length and cycle length actually sent to the mouse, after HitFix.
/// </summary>
public readonly record struct ClickTiming(double DownMs, double PeriodMs)
{
    /// <summary>Clicks per second this timing delivers.</summary>
    public double Cps => PeriodMs > 0 ? 1000.0 / PeriodMs : 0.0;

    /// <summary>Share of each cycle the button is held, 0–100.</summary>
    public double DutyPercent => PeriodMs > 0 ? DownMs / PeriodMs * 100.0 : 0.0;
}

/// <summary>
/// Turns the two sliders plus the HitFix toggle into the timing the click loop
/// runs on.
/// </summary>
/// <remarks>
/// Split out of the loop so the readout on the clicker page and the thread
/// sending the clicks compute the same answer from the same code. They used to
/// disagree in the way that matters most: with HitFix on and the sliders high,
/// every setting above 20 CPS produced identical output, and nothing in the UI
/// said so.
/// </remarks>
public static class ClickTimings
{
    /// <summary>
    /// Shortest the button is held, and the shortest gap after it, while HitFix
    /// is on. A frame and a half at 60 fps, so a read lands inside a press
    /// wherever the frame boundary happens to fall.
    /// </summary>
    /// <remarks>
    /// 25ms came from assuming a client samples input once a frame, so a press
    /// had to span a frame boundary (16.7ms at 60fps) to be seen at all.
    ///
    /// That assumption was wrong. A low-level mouse hook was used to capture a
    /// competing clicker that reliably lands one more hit per ten seconds in the
    /// same arena: it holds the button for 15.6ms — under a frame — at 31.8
    /// clicks per second, and its presses register. Roblox takes mouse events
    /// off the Windows message queue, where a short press is queued and read
    /// like any other, not sampled per frame.
    ///
    /// So the floors only ever need to keep press and gap from collapsing to
    /// nothing, which is what actually broke things at a 99% duty cycle. 15ms
    /// matches a profile measured working and lifts the ceiling to ~33/s.
    ///
    /// The earlier 25 → 20 → 17 → 25 wandering was all model, no measurement.
    /// This number has an implementation behind it.
    /// </remarks>
    public const double HitFixMinDownMs = 15.0;

    public const double HitFixMinUpMs = 15.0;

    /// <summary>
    /// Resolves the timing for a rate and duty cycle.
    /// </summary>
    /// <param name="cps">Clicks per second requested by the slider.</param>
    /// <param name="duty">Share of the cycle held, 0–1.</param>
    /// <param name="hitFix">Whether the minimum press and gap are enforced.</param>
    public static ClickTiming Resolve(double cps, double duty, bool hitFix)
    {
        if (cps <= 0) return new ClickTiming(0, 0);

        double period = 1000.0 / cps;
        double downMs = period * Math.Clamp(duty, 0.0, 1.0);

        if (hitFix)
        {
            // A client reads input once a frame. At 60 fps that is every ~17ms,
            // so a press shorter than a frame can begin and end between two
            // reads and never be seen.
            //
            // Both edges need a read inside them, so the gap after the press
            // gets a floor too. A press with no observed release is a held
            // button, not a click.
            downMs = Math.Max(downMs, HitFixMinDownMs);

            // Raising the period is what makes the floors reachable, and it
            // lowers the delivered rate below the slider. That is the honest
            // outcome: the surplus was never landing anyway.
            period = Math.Max(period, downMs + HitFixMinUpMs);
        }

        return new ClickTiming(downMs, period);
    }

    /// <summary>
    /// Whether HitFix is changing the requested timing rather than passing it
    /// through. True means the sliders are not what is being sent.
    /// </summary>
    public static bool IsClamped(double cps, double duty, bool hitFix)
    {
        if (!hitFix || cps <= 0) return false;

        ClickTiming asked = Resolve(cps, duty, hitFix: false);
        ClickTiming sent = Resolve(cps, duty, hitFix: true);

        // Tolerance well under a millisecond: this only needs to catch a real
        // floor being applied, not floating point noise from the division.
        return Math.Abs(sent.DownMs - asked.DownMs) > 0.01
               || Math.Abs(sent.PeriodMs - asked.PeriodMs) > 0.01;
    }
}
