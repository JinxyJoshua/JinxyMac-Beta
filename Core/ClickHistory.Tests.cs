using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The running totals behind the History page.
/// </summary>
/// <remarks>
/// Most methods here work on an in-memory instance, since Load and Save touch
/// a real file in the user's application data and a test that wrote there
/// carelessly would be editing the numbers it is meant to be checking. The few
/// tests that do need the real file (at the bottom) back it up first and
/// restore it in a finally block, the same isolation pattern
/// <c>AppSettings.Tests.cs</c> and <c>KeyMacro.Tests.cs</c> use for theirs.
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

    /// <summary>
    /// A hand-edited history.json with <c>"Days":null</c> deserializes Days
    /// to null despite its own field initialiser — System.Text.Json
    /// overwrites an initialiser whenever the JSON carries an explicit null.
    /// Unlike Load()/Save(), Add() has no try/catch of its own: it runs on
    /// every click, on the UI thread, so an unguarded null here used to be an
    /// unhandled NullReferenceException while someone was clicking, not a
    /// quiet fallback. The Days property setter now refuses to store a null,
    /// so this must behave exactly like a fresh ClickHistory.
    /// </summary>
    [Fact]
    public void AddDoesNotThrowWhenDaysDeserializedAsNull()
    {
        var history = System.Text.Json.JsonSerializer.Deserialize<ClickHistory>("""{"Days":null}""")!;

        history.Add(Monday, seconds: 10, clicks: 100);

        HistoryDay day = Assert.Single(history.Days);
        Assert.Equal("2026-08-17", day.Date);
    }

    /// <summary>Assigning null directly is refused the same way deserializing one is.</summary>
    [Fact]
    public void TheDaysPropertyRefusesANullAssignment()
    {
        var history = new ClickHistory();

        history.Days = null!;

        Assert.NotNull(history.Days);
        Assert.Empty(history.Days);
    }

    // ---- reading and writing the real file ----
    //
    // Load()/Save() read and write a fixed path (SettingsPath.For
    // ("history.json")), same as AppSettings and MacroStore — see those
    // Tests.cs files for the backup/restore isolation this reuses. Every
    // other test above works on an in-memory instance for the reason the
    // class remarks give; these three specifically need the real file.

    private static string HistoryFilePath => SettingsPath.For("history.json");

    /// <summary>
    /// A zero-length file — what a kill mid-write used to leave behind
    /// before Save() went through SettingsPath.WriteAtomic — must load as a
    /// fresh, empty history, the same as a missing file, rather than crash.
    /// </summary>
    [Fact]
    public void AZeroLengthFileLoadsAsFreshRatherThanCrashing()
    {
        WithHistoryFile("", () =>
        {
            ClickHistory loaded = ClickHistory.Load();

            Assert.Empty(loaded.Days);
            Assert.Equal(0, loaded.TotalClicks);
        });
    }

    /// <summary>
    /// Save() must leave the previous history file exactly as it was when
    /// the write fails partway, the same contract pinned directly at the
    /// shared helper in SettingsPath.Tests.cs and exercised here through the
    /// real caller.
    /// </summary>
    [Fact]
    public void AFailedSaveLeavesThePreviousHistoryFileIntact()
    {
        WithHistoryFile(
            """{"TotalSeconds":10,"TotalClicks":100,"Days":[{"Date":"2026-08-17","Seconds":10,"Clicks":100}]}""",
            () =>
            {
                string temp = HistoryFilePath + ".tmp";
                Directory.CreateDirectory(temp);

                try
                {
                    var history = new ClickHistory();
                    history.Add(Tuesday, seconds: 999, clicks: 9999);
                    history.Save();

                    string raw = File.ReadAllText(HistoryFilePath);
                    Assert.Contains("2026-08-17", raw);
                    Assert.DoesNotContain("9999", raw);
                }
                finally
                {
                    if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
                }
            });
    }

    /// <summary>
    /// Backs up whatever is at the real history.json and restores it
    /// afterwards — the isolation pattern <c>AppSettings.Tests.cs</c> and
    /// <c>KeyMacro.Tests.cs</c> use for their own fixed-path files.
    /// </summary>
    private static void WithHistoryFile(string? json, Action assertion)
    {
        string path = HistoryFilePath;
        bool existed = File.Exists(path);
        string? original = existed ? File.ReadAllText(path) : null;

        try
        {
            if (json != null) File.WriteAllText(path, json);
            assertion();
        }
        finally
        {
            if (existed) File.WriteAllText(path, original!);
            else if (File.Exists(path)) File.Delete(path);
        }
    }
}
