namespace JinxyMac.Core;

/// <summary>How what is being delivered compares to what was asked for.</summary>
public enum OutputState
{
    /// <summary>Not clicking, so there is nothing to measure yet.</summary>
    Idle,

    /// <summary>Delivering materially less than the setting asks for.</summary>
    Shortfall,

    /// <summary>
    /// Short of the setting because HitFix's floors are holding it back.
    /// </summary>
    /// <remarks>
    /// Separate from a plain shortfall because the remedy is different, and
    /// pointing at the slider here would be wrong advice: HitFix caps every
    /// setting at the same rate, so lowering the slider changes nothing until
    /// it drops under that cap.
    /// </remarks>
    ClampedByHitFix,

    /// <summary>Delivering the setting, but the setting is past the point of use.</summary>
    OverDriven,

    /// <summary>Delivering the setting, inside a rate the server keeps up with.</summary>
    Matching
}

/// <summary>
/// The difference between the rate someone set and the rate that leaves the app.
/// </summary>
/// <remarks>
/// The sliders are a request. HitFix's floors, the duty cycle and the operating
/// system's scheduler all sit between that request and what the game receives,
/// and the gap is routinely large — a 193 CPS setting delivering 33 /s is the
/// ordinary case rather than a fault.
///
/// Left unsaid, the bigger number reads as the better setting, which is exactly
/// backwards: a run measured at 33.3 CPS landed 34 hits where a 193 CPS run
/// landed 33. This is the arithmetic behind saying so.
/// </remarks>
public static class ClickOutput
{
    /// <summary>
    /// Past this, more clicks stop becoming more hits.
    /// </summary>
    /// <remarks>
    /// Set from this app's own measurements, not from advice found online.
    ///
    /// Two runs, both recorded here: a profile delivering 33.3 clicks a second
    /// landed 34 hits, and one delivering about 193 landed 33. The slower one
    /// won. A personal best of 34 was then repeated at that same 33.3.
    ///
    /// A brief detour set this to 15, on the strength of community posts
    /// recommending 8 to 12 — those turned out to be about Minecraft's Bedwars,
    /// a different game with a different server. Following them would have told
    /// every user to abandon the only setting measured to work here. Kept as a
    /// note because the mistake is easy to repeat: this game's numbers have to
    /// come from this game.
    ///
    /// A little above 33.3 rather than exactly on it, so the configuration that
    /// produced the best result does not sit on the wrong side of its own
    /// warning.
    /// </remarks>
    public const double DiminishingReturnsCps = 36.0;

    /// <summary>The delivered rate measured to produce the best result.</summary>
    public const double MeasuredBestCps = 33.3;

    /// <summary>
    /// How far short delivery must fall before it is worth mentioning.
    /// </summary>
    /// <remarks>
    /// Measurement is a difference of click counts over a wall-clock second, so
    /// it jitters by a click either way at any rate. A tolerance stops the panel
    /// flickering between verdicts while nothing has actually changed.
    /// </remarks>
    public const double MismatchTolerance = 0.15;

    /// <param name="hitFixClamping">
    /// Whether HitFix's floors are what is holding the rate down, rather than
    /// the setting simply being unreachable for some other reason.
    /// </param>
    public static OutputState Classify(
        bool running, double setCps, double deliveredCps, bool hitFixClamping = false)
    {
        if (!running) return OutputState.Idle;

        // A nonsense reading is not evidence of a shortfall. NaN slips through
        // any comparison it is put in, so it is refused up front.
        if (double.IsNaN(deliveredCps) || double.IsNaN(setCps)) return OutputState.Idle;

        if (setCps > 0 && deliveredCps < setCps * (1.0 - MismatchTolerance))
            return hitFixClamping ? OutputState.ClampedByHitFix : OutputState.Shortfall;

        return setCps > DiminishingReturnsCps ? OutputState.OverDriven : OutputState.Matching;
    }

    /// <summary>The sentence shown under the delivered rate.</summary>
    public static string Verdict(OutputState state, double setCps, double deliveredCps) => state switch
    {
        OutputState.Idle =>
            "Start clicking to measure what the game actually receives.",

        OutputState.Shortfall =>
            $"Your {setCps:0.0} setting is really sending {deliveredCps:0.0}. "
            + "Lowering the slider until these match costs you nothing and makes the rate honest.",

        OutputState.ClampedByHitFix =>
            $"HitFix is holding this to {deliveredCps:0.0}, not the {setCps:0.0} you set — and that "
            + $"is the rate measured to land the most hits. Every setting above about "
            + $"{MeasuredBestCps:0} produces exactly this, so the slider above it changes nothing "
            + "but the number on it.",

        OutputState.OverDriven =>
            $"Every click is landing, but {setCps:0.0} is past the point where more clicks became "
            + $"more hits. Measured here: {MeasuredBestCps:0} a second landed 34, about 193 landed 33.",

        _ => "Matching the setting, inside the range the server turns into hits."
    };

    /// <summary>
    /// Whether the verdict is worth colouring. A line that is always lit stops
    /// being read, so only the states asking for a change are accented.
    /// </summary>
    public static bool IsWarning(OutputState state) =>
        state is OutputState.Shortfall or OutputState.OverDriven or OutputState.ClampedByHitFix;
}
