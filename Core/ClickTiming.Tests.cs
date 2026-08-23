using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The arithmetic behind the clicker page. Worth pinning down: this is what
/// decides whether a press is long enough for the game to see, and it went
/// three revisions without anyone able to check it outside a running window.
/// </summary>
public class ClickTimingTests
{
    // Tolerance for millisecond figures that come out of a division.
    private const double Epsilon = 0.001;

    [Fact]
    public void WithoutHitFix_PassesTheSlidersThrough()
    {
        ClickTiming t = ClickTimings.Resolve(cps: 100, duty: 0.5, hitFix: false);

        Assert.Equal(10.0, t.PeriodMs, Epsilon);
        Assert.Equal(5.0, t.DownMs, Epsilon);
        Assert.Equal(100.0, t.Cps, Epsilon);
        Assert.Equal(50.0, t.DutyPercent, Epsilon);
    }

    /// <summary>
    /// The configuration that prompted all of this: both sliders near maximum,
    /// HitFix on. Every CPS above 20 and every duty above 50% lands here.
    /// </summary>
    /// <summary>
    /// The pinned rate: both floors binding, 25ms held and 25ms clear.
    /// </summary>
    private const double CeilingCps = 1000.0 / 30.0;

    [Fact]
    public void HitFix_CollapsesHighSettingsToTheCeiling()
    {
        ClickTiming t = ClickTimings.Resolve(cps: 101.94117647058825, duty: 0.99, hitFix: true);

        Assert.Equal(15.0, t.DownMs, Epsilon);
        Assert.Equal(30.0, t.PeriodMs, Epsilon);
        Assert.Equal(CeilingCps, t.Cps, Epsilon);
        Assert.Equal(50.0, t.DutyPercent, Epsilon);
    }

    /// <summary>
    /// The profile measured off a competing clicker that lands the extra hit:
    /// 31.8 clicks/sec, 15.6ms press, 49.6% duty. The floors have to let this
    /// through untouched or the app cannot reproduce what demonstrably works.
    /// </summary>
    [Fact]
    public void TheMeasuredCompetitorProfile_PassesThroughUnclamped()
    {
        ClickTiming t = ClickTimings.Resolve(cps: 32, duty: 0.5, hitFix: true);

        Assert.Equal(15.625, t.DownMs, Epsilon);
        Assert.Equal(31.25, t.PeriodMs, Epsilon);
        Assert.Equal(32.0, t.Cps, Epsilon);
        Assert.False(ClickTimings.IsClamped(cps: 32, duty: 0.5, hitFix: true));
    }

    /// <summary>
    /// Once the press itself is short enough to hit the floor, both floors bind
    /// and the rate is pinned no matter how high the slider goes. At 99% duty
    /// that starts just under 67 CPS.
    /// </summary>
    [Theory]
    [InlineData(150.0)]
    [InlineData(101.94117647058825)]
    [InlineData(70.0)]
    public void HitFix_GivesTheSameOutputForEveryRateAboveTheCap(double cps)
    {
        ClickTiming t = ClickTimings.Resolve(cps, duty: 0.99, hitFix: true);

        Assert.Equal(CeilingCps, t.Cps, Epsilon);
    }

    /// <summary>
    /// Between the asked rate and the pinned one the output still moves, just
    /// not to where the slider says. At 21 CPS the press already clears the
    /// floor, so only the gap floor applies.
    /// </summary>
    [Fact]
    public void HitFix_BetweenTheFloors_LandsBelowBothTheSliderAndTheCap()
    {
        ClickTiming t = ClickTimings.Resolve(cps: 21, duty: 0.99, hitFix: true);

        Assert.Equal(47.143, t.DownMs, 0.01);
        Assert.Equal(62.143, t.PeriodMs, 0.01);
        Assert.Equal(16.092, t.Cps, 0.01);
    }

    /// <summary>
    /// The ceiling the floors impose on a sane duty cycle — the fastest setting
    /// the app delivers untouched.
    /// </summary>
    [Fact]
    public void ThirtyThreePerSecond_AtHalfDuty_IsTheFastestUnclampedSetting()
    {
        Assert.False(ClickTimings.IsClamped(cps: 33, duty: 0.5, hitFix: true));

        // Anything past it is rewritten rather than delivered, and pinned.
        Assert.True(ClickTimings.IsClamped(cps: 34, duty: 0.5, hitFix: true));
        Assert.Equal(CeilingCps, ClickTimings.Resolve(cps: 34, duty: 0.5, hitFix: true).Cps, Epsilon);
    }

    /// <summary>
    /// Every rate a player would actually set has to be delivered untouched, so
    /// that moving the floors can never shift the sliders underneath them.
    /// </summary>
    [Theory]
    [InlineData(10.0)]
    [InlineData(12.0)]
    [InlineData(15.0)]
    [InlineData(20.0)]
    [InlineData(25.0)]
    [InlineData(32.0)]
    public void RatesUpToTheCeiling_AtHalfDuty_AreUntouchedByTheFloors(double cps)
    {
        ClickTiming t = ClickTimings.Resolve(cps, duty: 0.5, hitFix: true);

        Assert.Equal(cps, t.Cps, Epsilon);
        Assert.Equal(50.0, t.DutyPercent, Epsilon);
        Assert.False(ClickTimings.IsClamped(cps, duty: 0.5, hitFix: true));
    }

    /// <summary>
    /// Below the floors HitFix must not touch anything, or it would be raising
    /// the rate rather than capping it.
    /// </summary>
    [Fact]
    public void HitFix_LeavesSettingsThatAlreadyClearTheFloors()
    {
        ClickTiming t = ClickTimings.Resolve(cps: 10, duty: 0.5, hitFix: true);

        Assert.Equal(50.0, t.DownMs, Epsilon);
        Assert.Equal(100.0, t.PeriodMs, Epsilon);
        Assert.Equal(10.0, t.Cps, Epsilon);
    }

    /// <summary>
    /// A high duty cycle still distorts the rate at low CPS, which is why the
    /// duty slider matters even well under the cap.
    /// </summary>
    [Fact]
    public void HitFix_StretchesThePeriodWhenDutyLeavesNoGap()
    {
        ClickTiming t = ClickTimings.Resolve(cps: 10, duty: 0.99, hitFix: true);

        Assert.Equal(99.0, t.DownMs, Epsilon);
        // 99ms held needs a gap after it, so the cycle grows past the 100ms asked.
        Assert.Equal(114.0, t.PeriodMs, Epsilon);
        Assert.True(t.Cps < 10.0);
    }

    /// <summary>
    /// Zero is "armed but idle". Dividing by it produces Infinity, which cast to
    /// a tick count wraps to a 1ms delay — the opposite of not clicking.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void NonPositiveRate_ProducesNoTiming(double cps)
    {
        ClickTiming t = ClickTimings.Resolve(cps, duty: 0.5, hitFix: true);

        Assert.Equal(0.0, t.PeriodMs);
        Assert.Equal(0.0, t.DownMs);
        Assert.Equal(0.0, t.Cps);
        Assert.Equal(0.0, t.DutyPercent);
    }

    [Theory]
    [InlineData(1.5, 10.0)]   // over 100% cannot hold longer than the cycle
    [InlineData(-0.5, 0.0)]   // under zero cannot hold negative time
    public void DutyIsClampedToTheCycle(double duty, double expectedDownMs)
    {
        ClickTiming t = ClickTimings.Resolve(cps: 100, duty, hitFix: false);

        Assert.Equal(expectedDownMs, t.DownMs, Epsilon);
    }

    /// <summary>
    /// The building hotkey's fixed rate: 35 clicks a second at 1% duty, which is
    /// a 0.29ms tap inside a 28.6ms cycle.
    /// </summary>
    [Fact]
    public void TheBuildingRate_IsAShortTapInALongCycle()
    {
        ClickTiming t = ClickTimings.Resolve(cps: 35, duty: 0.01, hitFix: false);

        Assert.Equal(28.571, t.PeriodMs, 0.01);
        Assert.Equal(0.286, t.DownMs, 0.01);
        Assert.Equal(35.0, t.Cps, 0.01);
    }

    /// <summary>
    /// Why building bypasses HitFix rather than just turning the sliders down.
    /// The floors would drag a 1% duty cycle past 50% and a 35/s rate down to
    /// 33/s — turning the tap into a held button, which is the opposite of what
    /// placing blocks needs.
    /// </summary>
    [Fact]
    public void HitFixWouldDestroyTheBuildingRate()
    {
        ClickTiming clamped = ClickTimings.Resolve(cps: 35, duty: 0.01, hitFix: true);

        Assert.Equal(15.0, clamped.DownMs, Epsilon);
        Assert.Equal(30.0, clamped.PeriodMs, Epsilon);
        Assert.Equal(50.0, clamped.DutyPercent, Epsilon);
        Assert.True(ClickTimings.IsClamped(cps: 35, duty: 0.01, hitFix: true));
    }

    [Fact]
    public void IsClamped_IsTrueWhenHitFixRewritesTheSliders()
    {
        Assert.True(ClickTimings.IsClamped(cps: 101.94117647058825, duty: 0.99, hitFix: true));
    }

    [Fact]
    public void IsClamped_IsFalseWhenTheSettingsAlreadyClearTheFloors()
    {
        Assert.False(ClickTimings.IsClamped(cps: 10, duty: 0.5, hitFix: true));
    }

    [Fact]
    public void IsClamped_IsFalseWithHitFixOff()
    {
        Assert.False(ClickTimings.IsClamped(cps: 150, duty: 0.99, hitFix: false));
    }

    [Fact]
    public void IsClamped_IsFalseWhenIdle()
    {
        Assert.False(ClickTimings.IsClamped(cps: 0, duty: 0.99, hitFix: true));
    }
}
