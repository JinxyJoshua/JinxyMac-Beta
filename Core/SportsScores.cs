using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JinxyMac.Core;

/// <summary>The leagues the scoreboard can follow.</summary>
public enum ScoreLeague { CollegeFootball, NFL, NBA, NHL, MLB }

/// <summary>
/// One game's live state.
/// </summary>
/// <remarks>
/// No screen attached, so a payload can be parsed and checked without a window.
/// <paramref name="State"/> is ESPN's own "pre" / "in" / "post".
/// </remarks>
public sealed record GameInfo(
    string Id,
    string ShortName,
    string AwayAbbr, int AwayScore, string AwayId, string AwayLogo, int AwayRank,
    string HomeAbbr, int HomeScore, string HomeId, string HomeLogo, int HomeRank,
    string StatusText, string State, string PossessionId, string DownDistance)
{
    public bool InProgress => State == "in";
    public bool Final => State == "post";

    /// <summary>Not started yet.</summary>
    public bool Upcoming => State == "pre";

    /// <summary>The better (lower) of the two ranks; 99 when neither team is ranked.</summary>
    public int BestRank => Math.Min(AwayRank <= 0 ? 99 : AwayRank, HomeRank <= 0 ? 99 : HomeRank);

    public bool AwayHasBall => PossessionId.Length > 0 && PossessionId == AwayId;
    public bool HomeHasBall => PossessionId.Length > 0 && PossessionId == HomeId;

    /// <summary>
    /// The scoreline, or nothing at all before kick-off.
    /// </summary>
    /// <remarks>
    /// A game at "pre" carries two zeros, and showing "0 - 0" for a game that is
    /// hours away reads as a nil-nil draw rather than as a fixture.
    /// </remarks>
    public string ScoreLine => Upcoming ? "" : $"{AwayScore} - {HomeScore}";
}

/// <summary>A team: abbreviation, full name, logo URL.</summary>
public sealed record TeamInfo(string Abbr, string Name, string Logo);

/// <summary>
/// Reads live scores from ESPN's public scoreboard API.
/// </summary>
/// <remarks>
/// The fetch and the parsing are deliberately separate. Parsing is the part that
/// breaks when a payload shifts, and keeping it apart means it can be checked
/// against a captured payload with no network call — which is the only way any
/// of this gets verified at all, given the machine it is written on cannot run
/// the app.
///
/// Nothing of the user's is ever sent: the requests carry no identifier, ask for
/// a public scoreboard, and go nowhere else.
/// </remarks>
public static class SportsScores
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        // ESPN's site API serves this JSON to browsers and refuses a request with
        // no user agent at all, so it needs one. It names the app, and nothing
        // about the machine or the person running it.
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) JinxyMac");

        return c;
    }

    public static string PathFor(ScoreLeague league) => league switch
    {
        ScoreLeague.CollegeFootball => "football/college-football",
        ScoreLeague.NFL => "football/nfl",
        ScoreLeague.NBA => "basketball/nba",
        ScoreLeague.NHL => "hockey/nhl",
        ScoreLeague.MLB => "baseball/mlb",
        _ => "football/nfl"
    };

    public static string DisplayName(ScoreLeague league) => league switch
    {
        ScoreLeague.CollegeFootball => "College football",
        ScoreLeague.NFL => "NFL",
        ScoreLeague.NBA => "NBA",
        ScoreLeague.NHL => "NHL",
        ScoreLeague.MLB => "MLB",
        _ => league.ToString()
    };

    /// <summary>The short label for the league picker.</summary>
    public static string ShortName(ScoreLeague league) =>
        league == ScoreLeague.CollegeFootball ? "CFB" : DisplayName(league);

    public static IReadOnlyList<ScoreLeague> Leagues { get; } = new[]
    {
        ScoreLeague.CollegeFootball,
        ScoreLeague.NFL,
        ScoreLeague.NBA,
        ScoreLeague.NHL,
        ScoreLeague.MLB
    };

    public static string UrlFor(ScoreLeague league) =>
        $"https://site.api.espn.com/apis/site/v2/sports/{PathFor(league)}/scoreboard";

    /// <summary>Today's games for a league, or an empty list on any failure.</summary>
    public static async Task<List<GameInfo>> GetGamesAsync(
        ScoreLeague league, CancellationToken token = default)
    {
        try
        {
            string json = await Http.GetStringAsync(UrlFor(league), token).ConfigureAwait(false);
            return ParseScoreboard(json);
        }
        catch
        {
            // No network, a rate limit, a shape that cannot be read: all mean no
            // games, which the page says plainly. None of it deserves a dialog.
            return new List<GameInfo>();
        }
    }

    /// <summary>
    /// The games worth watching, first.
    /// </summary>
    /// <remarks>
    /// Live games lead, then games still to come, then finished ones; inside each
    /// group a ranked matchup comes before an unranked one. Opening this during a
    /// Saturday slate should put a live top-ten game at the top rather than
    /// whatever ESPN happened to list first.
    /// </remarks>
    public static List<GameInfo> InWatchingOrder(IEnumerable<GameInfo> games)
    {
        var ordered = new List<GameInfo>(games);

        ordered.Sort((a, b) =>
        {
            int byState = StateOrder(a).CompareTo(StateOrder(b));
            if (byState != 0) return byState;

            int byRank = a.BestRank.CompareTo(b.BestRank);

            // Last tiebreak on id so the order is stable: a list that reshuffles
            // on every refresh is unreadable while you are looking at it.
            return byRank != 0 ? byRank : string.CompareOrdinal(a.Id, b.Id);
        });

        return ordered;

        static int StateOrder(GameInfo g) => g.State switch
        {
            "in" => 0,
            "pre" => 1,
            _ => 2
        };
    }

    /// <summary>Only the games involving an AP Top 25 team, best first.</summary>
    public static List<GameInfo> RankedOnly(IEnumerable<GameInfo> games)
    {
        var ranked = new List<GameInfo>();

        foreach (GameInfo g in games)
            if (g.BestRank <= 25) ranked.Add(g);

        return InWatchingOrder(ranked);
    }

    /// <summary>Every team in a league, or empty on failure.</summary>
    public static async Task<List<TeamInfo>> GetTeamsAsync(
        ScoreLeague league, CancellationToken token = default)
    {
        try
        {
            string url =
                $"https://site.api.espn.com/apis/site/v2/sports/{PathFor(league)}/teams?limit=1000";

            return ParseTeams(await Http.GetStringAsync(url, token).ConfigureAwait(false));
        }
        catch
        {
            return new List<TeamInfo>();
        }
    }

    /// <summary>Parses an ESPN teams payload. Never throws; a bad entry is skipped.</summary>
    public static List<TeamInfo> ParseTeams(string? json)
    {
        var teams = new List<TeamInfo>();
        if (string.IsNullOrWhiteSpace(json)) return teams;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("sports", out JsonElement sports)
                || sports.ValueKind != JsonValueKind.Array || sports.GetArrayLength() == 0)
                return teams;

            if (!sports[0].TryGetProperty("leagues", out JsonElement leagues)
                || leagues.ValueKind != JsonValueKind.Array || leagues.GetArrayLength() == 0)
                return teams;

            if (!leagues[0].TryGetProperty("teams", out JsonElement arr)
                || arr.ValueKind != JsonValueKind.Array)
                return teams;

            foreach (JsonElement t in arr.EnumerateArray())
            {
                if (!t.TryGetProperty("team", out JsonElement team)) continue;

                string abbr = Str(team, "abbreviation");

                // No abbreviation means nothing to label the team with.
                if (abbr.Length == 0) continue;

                string name = Str(team, "displayName");
                string logo = "";

                if (team.TryGetProperty("logos", out JsonElement logos)
                    && logos.ValueKind == JsonValueKind.Array && logos.GetArrayLength() > 0)
                    logo = Str(logos[0], "href");

                teams.Add(new TeamInfo(abbr, name.Length == 0 ? abbr : name, logo));
            }
        }
        catch (JsonException)
        {
            // Whatever parsed before the bad part is still worth returning.
        }

        return teams;
    }

    /// <summary>
    /// Parses an ESPN scoreboard payload into games.
    /// </summary>
    /// <remarks>
    /// Never throws. A bad or half-written event is skipped rather than failing
    /// the whole list: one odd fixture should not empty a Saturday scoreboard.
    /// </remarks>
    public static List<GameInfo> ParseScoreboard(string? json)
    {
        var games = new List<GameInfo>();
        if (string.IsNullOrWhiteSpace(json)) return games;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("events", out JsonElement events)
                || events.ValueKind != JsonValueKind.Array)
                return games;

            foreach (JsonElement ev in events.EnumerateArray())
                if (ParseEvent(ev) is { } g) games.Add(g);
        }
        catch (JsonException)
        {
            // Malformed payload: return whatever parsed.
        }

        return games;
    }

    private static GameInfo? ParseEvent(JsonElement ev)
    {
        try
        {
            string id = Str(ev, "id");
            string shortName = Str(ev, "shortName");

            if (!ev.TryGetProperty("competitions", out JsonElement comps)
                || comps.ValueKind != JsonValueKind.Array || comps.GetArrayLength() == 0)
                return null;

            JsonElement comp = comps[0];

            string statusText = "", state = "";

            if (ev.TryGetProperty("status", out JsonElement st)
                && st.TryGetProperty("type", out JsonElement stType))
            {
                statusText = Str(stType, "shortDetail");
                state = Str(stType, "state");
            }

            // Possession and down live in the competition's "situation", and are
            // simply absent outside football.
            string possession = "", down = "";

            if (comp.TryGetProperty("situation", out JsonElement sit))
            {
                possession = Str(sit, "possession");
                down = Str(sit, "downDistanceText");
            }

            if (!comp.TryGetProperty("competitors", out JsonElement cs)
                || cs.ValueKind != JsonValueKind.Array)
                return null;

            string awA = "", awId = "", awLogo = "", hmA = "", hmId = "", hmLogo = "";
            int awS = 0, hmS = 0, awRank = 99, hmRank = 99;
            bool haveHome = false, haveAway = false;

            foreach (JsonElement c in cs.EnumerateArray())
            {
                bool home = Str(c, "homeAway") == "home";
                string abbr = "", teamId = "", logo = "";

                if (c.TryGetProperty("team", out JsonElement team))
                {
                    abbr = Str(team, "abbreviation");
                    teamId = Str(team, "id");
                    logo = Str(team, "logo");
                }

                int score = int.TryParse(Str(c, "score"), out int sv) ? sv : 0;
                int rank = 99;

                if (c.TryGetProperty("curatedRank", out JsonElement cr)
                    && cr.TryGetProperty("current", out JsonElement cur)
                    && cur.TryGetInt32(out int rv) && rv > 0 && rv <= 25)
                    rank = rv;

                if (home)
                {
                    hmA = abbr; hmS = score; hmId = teamId; hmLogo = logo; hmRank = rank;
                    haveHome = true;
                }
                else
                {
                    awA = abbr; awS = score; awId = teamId; awLogo = logo; awRank = rank;
                    haveAway = true;
                }
            }

            // One side is a half-written entry, not a game.
            if (!haveHome || !haveAway) return null;

            return new GameInfo(id, shortName,
                awA, awS, awId, awLogo, awRank,
                hmA, hmS, hmId, hmLogo, hmRank,
                statusText, state, possession, down);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
