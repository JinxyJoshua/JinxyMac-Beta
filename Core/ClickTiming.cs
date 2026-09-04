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
    /// The shipped values, and the ones used unless a config says otherwise.
    /// </summary>
    /// <remarks>
    /// Kept separate from the values actually read so the defaults survive as
    /// facts: a remote config that goes missing, or arrives with nonsense in
    /// it, falls back to exactly the numbers this build was tested with.
    /// </remarks>
    public const double DefaultHitFixMinDownMs = 15.0;
    public const double DefaultHitFixMinUpMs = 15.0;

    /// <summary>
    /// What the timing actually uses.
    /// </summary>
    /// <remarks>
    /// Properties rather than constants because these are exactly the kind of
    /// number that turns out to be slightly wrong on hardware nobody could
    /// test — which, on macOS, is all of it.
    ///
    /// Bounded at the point the config is read, never here — this only ever
    /// sees a value the app already agreed to accept.
    /// </remarks>
    public static double HitFixMinDownMs => RemoteConfig.Current.HitFixMinDownMs;

    public static double HitFixMinUpMs => RemoteConfig.Current.HitFixMinUpMs;

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
            // The floors only ever need to keep the press and the gap from
            // collapsing to nothing, which is what actually broke at a 99% duty
            // cycle.
            //
            // Not because a client samples input once a frame — that model was
            // disproved. A competing clicker holds for 15.6ms, under a frame at
            // 60fps, and its presses register; this app's own measured profile
            // has a gap of a third of a frame and wins anyway. Roblox takes
            // mouse events off the message queue, where a short press is queued
            // and read like any other.
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
