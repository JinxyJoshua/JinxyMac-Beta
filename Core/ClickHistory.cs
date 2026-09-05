using System.Globalization;
using System.Text.Json;

namespace JinxyMac.Core;

/// <summary>One day's totals. Date is stored as yyyy-MM-dd so the file stays readable.</summary>
public sealed class HistoryDay
{
    public string Date { get; set; } = "";
    public double Seconds { get; set; }
    public long Clicks { get; set; }

    public string DateText =>
        DateTime.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime parsed)
            ? parsed.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture)
            : Date;

    public string DurationText => ClickHistory.FormatDuration(TimeSpan.FromSeconds(Seconds));
    public string ClicksText => Clicks.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Average delivered rate over the counted time, not over the day.</summary>
    public string RateText => Seconds > 0.5 ? $"{Clicks / Seconds:0.0} /s avg" : "";
}

/// <summary>
/// Lifetime clicking totals, broken down by day.
/// </summary>
/// <remarks>
/// "Time clicking" means time the engine spent actually delivering clicks. It
/// excludes the app merely being open.
///
/// Carried over from the Windows build unchanged apart from the namespace. The
/// file format matches, which is not an accident: someone who uses both should
/// be able to copy history.json across and keep their numbers.
/// </remarks>
public sealed class ClickHistory
{
    private static readonly string HistoryFile = SettingsPath.For("history.json");
    private const int MaxDays = 60;

    public double TotalSeconds { get; set; }
    public long TotalClicks { get; set; }

    private List<HistoryDay> _days = new();

    /// <summary>
    /// A hand-edited history.json with <c>"Days":null</c> deserializes this to
    /// null despite the field initialiser above — System.Text.Json overwrites
    /// an initialiser whenever the JSON carries an explicit null. Load's own
    /// try/catch cannot save <see cref="Add"/> from that: Add runs later, on
    /// every click, with no try/catch of its own (unlike Load and Save), so an
    /// unguarded null here is an unhandled exception on the UI thread the next
    /// time somebody clicks. The setter turns that null into an empty list
    /// instead, which is what "no days recorded yet" already means everywhere
    /// else in this file.
    /// </summary>
    public List<HistoryDay> Days
    {
        get => _days;
        set => _days = value ?? new List<HistoryDay>();
    }

    public static ClickHistory Load()
    {
        try
        {
            if (!File.Exists(HistoryFile)) return new ClickHistory();

            return JsonSerializer.Deserialize<ClickHistory>(File.ReadAllText(HistoryFile))
                   ?? new ClickHistory();
        }
        catch
        {
            return new ClickHistory();
        }
    }

    public void Save()
    {
        try
        {
            SettingsPath.WriteAtomic(HistoryFile,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Losing a day's tally is not worth taking the app down for.
        }
    }

    /// <summary>Folds a slice of activity into today's row and the lifetime totals.</summary>
    public void Add(DateTime whenLocal, double seconds, long clicks)
    {
        if (seconds <= 0 && clicks <= 0) return;

        TotalSeconds += seconds;
        TotalClicks += clicks;

        string key = whenLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        HistoryDay? day = Days.FirstOrDefault(d => d.Date == key);

        if (day == null)
        {
            day = new HistoryDay { Date = key };
            Days.Add(day);
        }

        day.Seconds += seconds;
        day.Clicks += clicks;

        Trim();
    }

    /// <summary>Lifetime average, over counted clicking time rather than wall clock.</summary>
    public string AverageRateText =>
        TotalSeconds > 0.5 ? $"{TotalClicks / TotalSeconds:0.0} /s" : "—";

    /// <summary>
    /// Only counts days still held. Lifetime totals survive the trim, so this is
    /// "days on record", not "days you have ever clicked".
    /// </summary>
    public int DaysRecorded => Days.Count;

    /// <summary>Newest first, which is the order the page shows them in.</summary>
    public List<HistoryDay> RecentDays() =>
        Days.OrderByDescending(d => d.Date, StringComparer.Ordinal).ToList();

    public void Reset()
    {
        TotalSeconds = 0;
        TotalClicks = 0;
        Days.Clear();
    }

    private void Trim()
    {
        if (Days.Count <= MaxDays) return;

        // Lifetime totals are kept separately, so dropping old rows loses only
        // the per-day breakdown.
        Days = Days.OrderByDescending(d => d.Date, StringComparer.Ordinal)
                   .Take(MaxDays)
                   .ToList();
    }

    public static string FormatDuration(TimeSpan span)
    {
        if (span.TotalSeconds < 1) return "0s";

        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes:00}m {span.Seconds:00}s";

        return span.Minutes >= 1
            ? $"{span.Minutes}m {span.Seconds:00}s"
            : $"{span.Seconds}s";
    }
}
