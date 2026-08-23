using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The running totals behind the History page.
/// </summary>
/// <remarks>
/// Every method here works on an in-memory instance. Load and Save touch a real
/// file in the user's application data, and a test that wrote there would be
/// editing the numbers it is meant to be checking.
/// </remarks>
public class ClickHistoryTests
{
    private static readonly DateTime Monday = new(2026, 8, 17, 14, 0, 0);
    private static readonly DateTime Tuesday = new(2026, 8, 18, 9, 30, 0);

    [Fact]
    public void FoldsClicksIntoTheDayTheyHappened()
    {
        var history = new ClickHistory();

        history.Add(Monday, seconds: 10, clicks: 100);
        history.Add(Monday.AddHours(4), seconds: 5, clicks: 40);

        HistoryDay day = Assert.Single(history.Days);

        Assert.Equal("2026-08-17", day.Date);
        Assert.Equal(15, day.Seconds);
        Assert.Equal(140, day.Clicks);
    }

    [Fact]
    public void KeepsTheDaysApart()
    {
        var history = new ClickHistory();

        history.Add(Monday, 10, 100);
        history.Add(Tuesday, 10, 200);

        Assert.Equal(2, history.Days.Count);
        Assert.Equal(300, history.TotalClicks);
        Assert.Equal(20, history.TotalSeconds);
    }

    [Fact]
    public void ShowsTheNewestDayFirst()
    {
        var history = new ClickHistory();

        history.Add(Monday, 10, 100);
        history.Add(Tuesday, 10, 100);

        Assert.Equal("2026-08-18", history.RecentDays()[0].Date);
    }

    /// <summary>
    /// Nothing happened is not the same as a zero-length session, and a row of
    /// zeroes on the page would read as the latter.
    /// </summary>
    [Fact]
    public void IgnoresAnEmptySlice()
    {
        var history = new ClickHistory();

        history.Add(Monday, seconds: 0, clicks: 0);

        Assert.Empty(history.Days);
        Assert.Equal(0, history.TotalClicks);
    }

    /// <summary>
    /// Trimming drops the per-day breakdown, never the lifetime totals. Someone
    /// with a year of use should not watch their click count fall.
    /// </summary>
    [Fact]
    public void TrimsOldDaysWithoutLosingTheTotals()
    {
        var history = new ClickHistory();

        for (int day = 0; day < 90; day++)
            history.Add(new DateTime(2026, 1, 1).AddDays(day), seconds: 1, clicks: 10);

        Assert.Equal(60, history.Days.Count);
        Assert.Equal(900, history.TotalClicks);
        Assert.Equal(90, history.TotalSeconds);

        // The 60 kept are the newest, not the first 60 recorded.
        Assert.Equal("2026-03-31", history.RecentDays()[0].Date);
    }

    [Fact]
    public void AveragesOverCountedTimeRatherThanWallClock()
    {
        var history = new ClickHistory();

        history.Add(Monday, seconds: 20, clicks: 300);

        Assert.Equal("15.0 /s", history.AverageRateText);
    }

    [Fact]
    public void SaysNothingRatherThanZeroBeforeAnyClicking()
    {
        Assert.Equal("—", new ClickHistory().AverageRateText);
    }

    [Fact]
    public void ResetClearsEverything()
    {
        var history = new ClickHistory();

        history.Add(Monday, 10, 100);
        history.Reset();

        Assert.Empty(history.Days);
        Assert.Equal(0, history.TotalClicks);
        Assert.Equal(0, history.TotalSeconds);
    }

    [Theory]
    [InlineData(0.4, "0s")]
    [InlineData(9, "9s")]
    [InlineData(75, "1m 15s")]
    [InlineData(3725, "1h 02m 05s")]
    public void FormatsDurationsAtTheRightScale(double seconds, string expected)
    {
        Assert.Equal(expected, ClickHistory.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }
}
