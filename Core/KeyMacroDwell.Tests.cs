using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The dwell arithmetic that decides whether a swapped-to weapon actually
/// fires.
/// </summary>
/// <remarks>
/// The whole point of this map is hit count, so a rotation that draws a
/// crossbow and swaps away before it shoots is worse than no rotation. The
/// minimum has to come out of the numbers rather than out of somebody trying
/// values until it looks right.
/// </remarks>
public class KeyMacroDwellTests
{
    /// <summary>
    /// Equip time plus two clicks. Two rather than one because the first can
    /// land on the same instant the equip finishes and be swallowed.
    /// </summary>
    [Fact]
    public void LeavesRoomForTheEquipAndTwoClicks()
    {
        // 10 clicks a second is a 100ms period, so 250 + 200.
        Assert.Equal(450, KeyMacro.MinimumDwellMs(clickPeriodMs: 100, equipMs: 250));
    }

    /// <summary>
    /// The measured case. A 130ms crossbow dip at 15 clicks a second fires
    /// reliably by hand, so the computed floor must not exceed it — an earlier
    /// 250ms equip guess put the floor at 383 and would have forced the
    /// rotation slower than the hand it copies.
    /// </summary>
    [Fact]
    public void DoesNotOutrunWhatAHandDoes()
    {
        int floor = KeyMacro.MinimumDwellMs(clickPeriodMs: 66, equipMs: KeyMacro.DefaultEquipMs);

        Assert.True(floor <= 200, $"floor was {floor}ms, longer than a measured 130-200ms dip");
    }

    /// <summary>
    /// A faster clicker needs less dwell, because its two clicks arrive sooner.
    /// This is why the figure is derived from the clicker rather than typed in
    /// once and left to rot when the CPS changes.
    /// </summary>
    [Fact]
    public void ShrinksAsTheClickerGetsFaster()
    {
        int slow = KeyMacro.MinimumDwellMs(clickPeriodMs: 100, equipMs: 250);
        int fast = KeyMacro.MinimumDwellMs(clickPeriodMs: 12, equipMs: 250);

        Assert.True(fast < slow);
        Assert.Equal(274, fast);
    }

    [Fact]
    public void GrowsWithASlowerEquip()
    {
        Assert.True(KeyMacro.MinimumDwellMs(100, equipMs: 600)
                  > KeyMacro.MinimumDwellMs(100, equipMs: 250));
    }

    /// <summary>Rounded up. Half a click short is a shot that does not happen.</summary>
    [Fact]
    public void RoundsUpRatherThanDown()
    {
        Assert.Equal(251, KeyMacro.MinimumDwellMs(clickPeriodMs: 0.4, equipMs: 250, clicks: 2));
    }

    /// <summary>
    /// A zero or nonsense period must not produce a zero dwell, which would
    /// swap instantly and fire nothing at all.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void SurvivesAnImpossibleClickRate(double period)
    {
        Assert.True(KeyMacro.MinimumDwellMs(period, equipMs: 250) >= 250);
    }

    [Fact]
    public void StaysInsideTheAllowedRange()
    {
        Assert.InRange(KeyMacro.MinimumDwellMs(100, equipMs: 999_999),
            KeyMacro.MinIntervalMs, KeyMacro.MaxIntervalMs);
    }

    // ---- per-key dwell ----

    /// <summary>
    /// The reason this exists. An even rotation keeps the crossbow in hand half
    /// the time and redraws it on every return, which is exactly the complaint.
    /// </summary>
    [Fact]
    public void EachKeyCanHoldForADifferentTime()
    {
        var macro = new KeyMacro("S", new[] { 0x33, 0x31 }, "3, 1", 700, new[] { 700, 40 });

        Assert.Equal(700, macro.DwellFor(0));
        Assert.Equal(40, macro.DwellFor(1));
    }

    [Fact]
    public void FallsBackToTheSingleIntervalWhenNoHoldsAreGiven()
    {
        var macro = new KeyMacro("R", new[] { 0x52 }, "R", 120);

        Assert.Equal(120, macro.DwellFor(0));
        Assert.Equal(120, macro.DwellFor(5));
    }

    [Fact]
    public void ClampsEveryHoldIntoRange()
    {
        var macro = new KeyMacro("S", new[] { 0x33, 0x31 }, "3, 1", 700, new[] { 0, 999_999 });

        Assert.Equal(KeyMacro.MinIntervalMs, macro.DwellFor(0));
        Assert.Equal(KeyMacro.MaxIntervalMs, macro.DwellFor(1));
    }
}
