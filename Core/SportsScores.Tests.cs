using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Reading a live scoreboard out of ESPN's payload.
/// </summary>
/// <remarks>
/// Against a captured payload rather than the live service, for two reasons: a
/// test that calls out to the internet fails on a bad day rather than on a bad
/// change, and the shape below can hold a live game, a finished one and a
/// half-written one at once, which a real Tuesday afternoon cannot.
/// </remarks>
public class SportsScoresTests
{
    /// <summary>
    /// Trimmed to exactly the fields that are read, in ESPN's own shape: the
    /// first game is live with #17 away at #3 home and the home team holding the
    /// ball, the second is over.
    /// </summary>
    private const string Sample = """
    {
      "events": [
        {
          "id": "401",
          "shortName": "FLA VS UGA",
          "status": { "type": { "state": "in", "shortDetail": "2nd - 7:12" } },
          "competitions": [
            {
              "situation": { "possession": "61", "downDistanceText": "3rd & 7" },
              "competitors": [
                { "homeAway": "home", "score": "14", "curatedRank": { "current": 3 },
                  "team": { "id": "61", "abbreviation": "UGA",
                            "logo": "https://a.espncdn.com/i/teamlogos/ncaa/500/61.png" } },
                { "homeAway": "away", "score": "10", "curatedRank": { "current": 17 },
                  "team": { "id": "57", "abbreviation": "FLA",
                            "logo": "https://a.espncdn.com/i/teamlogos/ncaa/500/57.png" } }
              ]
            }
          ]
        },
        {
          "id": "402",
          "shortName": "ND VS NAVY",
          "status": { "type": { "state": "post", "shortDetail": "Final" } },
          "competitions": [
            { "competitors": [
              { "homeAway": "home", "score": "28", "curatedRank": { "current": 99 },
                "team": { "id": "2426", "abbreviation": "NAVY" } },
              { "homeAway": "away", "score": "31", "curatedRank": { "current": 5 },
                "team": { "id": "87", "abbreviation": "ND" } }
            ] }
          ]
        }
      ]
    }
    """;

    private static GameInfo Game(string id) =>
        SportsScores.ParseScoreboard(Sample).First(g => g.Id == id);

    // ---- reading a game ----

    [Fact]
    public void EveryGameInThePayloadIsRead()
    {
        Assert.Equal(2, SportsScores.ParseScoreboard(Sample).Count);
    }

    [Fact]
    public void TeamsAndScoresKeepTheirHomeAndAwaySides()
    {
        GameInfo g = Game("401");

        Assert.Equal("FLA VS UGA", g.ShortName);
        Assert.Equal("FLA", g.AwayAbbr);
        Assert.Equal(10, g.AwayScore);
        Assert.Equal("UGA", g.HomeAbbr);
        Assert.Equal(14, g.HomeScore);
    }

    [Fact]
    public void LogosAndTeamIdsAreRead()
    {
        GameInfo g = Game("401");

        Assert.Equal("57", g.AwayId);
        Assert.Equal("61", g.HomeId);
        Assert.EndsWith("/57.png", g.AwayLogo);
        Assert.EndsWith("/61.png", g.HomeLogo);
    }

    [Fact]
    public void RanksAreReadAndTheBetterOneLeads()
    {
        GameInfo g = Game("401");

        Assert.Equal(17, g.AwayRank);
        Assert.Equal(3, g.HomeRank);
        Assert.Equal(3, g.BestRank);
    }

    /// <summary>
    /// ESPN writes 99 for an unranked team and omits the field entirely for a
    /// league that has no rankings, and both have to read as "not ranked" rather
    /// than as rank 99 of 25.
    /// </summary>
    [Fact]
    public void AnUnrankedTeamDoesNotCountAsRanked()
    {
        GameInfo g = Game("402");

        Assert.Equal(99, g.HomeRank);
        Assert.Equal(5, g.AwayRank);
        Assert.Equal(5, g.BestRank);
    }

    [Fact]
    public void PossessionAndDownAreReadForFootball()
    {
        GameInfo g = Game("401");

        Assert.Equal("61", g.PossessionId);
        Assert.Equal("3rd & 7", g.DownDistance);
        Assert.True(g.HomeHasBall);
        Assert.False(g.AwayHasBall);
    }

    /// <summary>
    /// No "situation" at all, which is every finished game and every sport that
    /// is not football. Neither side may end up marked with the ball.
    /// </summary>
    [Fact]
    public void NoPossessionMeansNeitherSideHasTheBall()
    {
        GameInfo g = Game("402");

        Assert.False(g.HomeHasBall);
        Assert.False(g.AwayHasBall);
    }

    [Fact]
    public void ALiveGameReadsAsInProgress()
    {
        GameInfo g = Game("401");

        Assert.Equal("2nd - 7:12", g.StatusText);
        Assert.True(g.InProgress);
        Assert.False(g.Final);
        Assert.False(g.Upcoming);
    }

    [Fact]
    public void AFinishedGameReadsAsFinal()
    {
        GameInfo g = Game("402");

        Assert.True(g.Final);
        Assert.False(g.InProgress);
        Assert.Equal("Final", g.StatusText);
    }

    // ---- the scoreline ----

    [Fact]
    public void ALiveGameShowsItsScore()
    {
        Assert.Equal("10 - 14", Game("401").ScoreLine);
    }

    /// <summary>
    /// A game hours away carries two zeros, and "0 - 0" reads as a nil-nil draw
    /// rather than as a fixture, so it shows nothing instead.
    /// </summary>
    [Fact]
    public void AGameThatHasNotStartedShowsNoScore()
    {
        var fixture = new GameInfo("1", "A VS B",
            "A", 0, "1", "", 99,
            "B", 0, "2", "", 99,
            "7:30 PM", "pre", "", "");

        Assert.Equal("", fixture.ScoreLine);
        Assert.True(fixture.Upcoming);
    }

    // ---- the order they are shown in ----

    /// <summary>
    /// What someone opening the page during a Saturday slate wants at the top:
    /// a live game, and the best live game first.
    /// </summary>
    [Fact]
    public void LiveGamesComeFirstThenUpcomingThenFinished()
    {
        var games = new List<GameInfo>
        {
            Fake("done", "post", 99),
            Fake("soon", "pre", 99),
            Fake("live", "in", 99)
        };

        List<GameInfo> ordered = SportsScores.InWatchingOrder(games);

        Assert.Equal(new[] { "live", "soon", "done" }, ordered.Select(g => g.Id));
    }

    [Fact]
    public void AmongLiveGamesTheBetterRankedLeads()
    {
        var games = new List<GameInfo>
        {
            Fake("unranked", "in", 99),
            Fake("ninth", "in", 9),
            Fake("second", "in", 2)
        };

        Assert.Equal(
            new[] { "second", "ninth", "unranked" },
            SportsScores.InWatchingOrder(games).Select(g => g.Id));
    }

    /// <summary>
    /// The page refreshes while someone is reading it, so two games that tie on
    /// every other count must not swap places between refreshes.
    /// </summary>
    [Fact]
    public void GamesThatTieKeepAStableOrder()
    {
        var games = new List<GameInfo> { Fake("b", "in", 5), Fake("a", "in", 5) };

        Assert.Equal(
            SportsScores.InWatchingOrder(games).Select(g => g.Id),
            SportsScores.InWatchingOrder(games).Select(g => g.Id));

        Assert.Equal("a", SportsScores.InWatchingOrder(games)[0].Id);
    }

    [Fact]
    public void TheRankedFilterKeepsOnlyTop25GamesBestFirst()
    {
        List<GameInfo> ranked = SportsScores.RankedOnly(SportsScores.ParseScoreboard(Sample));

        Assert.Equal(2, ranked.Count);
        Assert.Equal("401", ranked[0].Id);   // live, best rank 3
        Assert.Equal("402", ranked[1].Id);   // finished, rank 5
    }

    [Fact]
    public void TheRankedFilterDropsAGameWithNoRankedTeam()
    {
        var games = new List<GameInfo> { Fake("nobody", "in", 99), Fake("somebody", "in", 25) };

        Assert.Equal("somebody", Assert.Single(SportsScores.RankedOnly(games)).Id);
    }

    // ---- payloads that are wrong ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"events\": \"oops\"}")]
    public void ABadPayloadIsAnEmptyListRatherThanAThrow(string? json)
    {
        Assert.Empty(SportsScores.ParseScoreboard(json));
    }

    /// <summary>One side is a half-written entry, not a game.</summary>
    [Fact]
    public void AGameMissingASideIsSkipped()
    {
        const string oneSided = """
        {"events":[{"id":"9","shortName":"X VS Y",
          "competitions":[{"competitors":[
            {"homeAway":"home","score":"3","team":{"abbreviation":"X"}}
          ]}]}]}
        """;

        Assert.Empty(SportsScores.ParseScoreboard(oneSided));
    }

    /// <summary>
    /// A game whose entry is broken must not take the rest of the slate with it.
    /// </summary>
    [Fact]
    public void OneBadGameDoesNotEmptyTheRest()
    {
        const string mixed = """
        {"events":[
          {"id":"bad","shortName":"X VS Y","competitions":[]},
          {"id":"good","shortName":"A VS B","competitions":[{"competitors":[
            {"homeAway":"home","score":"7","team":{"abbreviation":"A","id":"1"}},
            {"homeAway":"away","score":"3","team":{"abbreviation":"B","id":"2"}}
          ]}]}
        ]}
        """;

        Assert.Equal("good", Assert.Single(SportsScores.ParseScoreboard(mixed)).Id);
    }

    [Fact]
    public void AMissingScoreReadsAsZeroRatherThanFailing()
    {
        const string noScore = """
        {"events":[{"id":"1","shortName":"A VS B",
          "competitions":[{"competitors":[
            {"homeAway":"home","team":{"abbreviation":"A"}},
            {"homeAway":"away","team":{"abbreviation":"B"}}
          ]}]}]}
        """;

        GameInfo g = Assert.Single(SportsScores.ParseScoreboard(noScore));

        Assert.Equal(0, g.HomeScore);
        Assert.Equal(0, g.AwayScore);
        Assert.Equal(99, g.BestRank);
    }

    // ---- teams ----

    private const string TeamsSample = """
    {
      "sports": [{ "leagues": [{ "teams": [
        { "team": { "abbreviation": "UGA", "displayName": "Georgia Bulldogs",
          "logos": [{ "href": "https://a.espncdn.com/i/teamlogos/ncaa/500/61.png" }] } },
        { "team": { "abbreviation": "FLA", "displayName": "Florida Gators", "logos": [] } },
        { "team": { "displayName": "No Abbr U" } }
      ] }] }]
    }
    """;

    [Fact]
    public void TeamsAreReadWithTheirNameAndLogo()
    {
        List<TeamInfo> teams = SportsScores.ParseTeams(TeamsSample);

        // The entry with no abbreviation is skipped: there would be nothing to
        // label it with.
        Assert.Equal(2, teams.Count);

        TeamInfo uga = teams.First(t => t.Abbr == "UGA");
        Assert.Equal("Georgia Bulldogs", uga.Name);
        Assert.EndsWith("/61.png", uga.Logo);

        Assert.Equal("", teams.First(t => t.Abbr == "FLA").Logo);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nope")]
    [InlineData("{}")]
    [InlineData("{\"sports\":[]}")]
    public void ABadTeamsPayloadIsEmpty(string? json)
    {
        Assert.Empty(SportsScores.ParseTeams(json));
    }

    // ---- the leagues themselves ----

    [Fact]
    public void EveryLeagueHasAPathAndANameAndNoneRepeat()
    {
        var paths = new HashSet<string>();
        var names = new HashSet<string>();

        foreach (ScoreLeague league in SportsScores.Leagues)
        {
            Assert.True(paths.Add(SportsScores.PathFor(league)), $"{league} repeats a path");
            Assert.True(names.Add(SportsScores.DisplayName(league)), $"{league} repeats a name");
            Assert.StartsWith("https://site.api.espn.com/", SportsScores.UrlFor(league));
        }

        Assert.Equal(5, SportsScores.Leagues.Count);
    }

    /// <summary>
    /// The picker is a row of small buttons, so "College football" is shortened
    /// there and nowhere else.
    /// </summary>
    [Fact]
    public void OnlyCollegeFootballIsShortenedForThePicker()
    {
        Assert.Equal("CFB", SportsScores.ShortName(ScoreLeague.CollegeFootball));

        foreach (ScoreLeague league in SportsScores.Leagues)
            if (league != ScoreLeague.CollegeFootball)
                Assert.Equal(SportsScores.DisplayName(league), SportsScores.ShortName(league));
    }

    private static GameInfo Fake(string id, string state, int rank) =>
        new(id, id,
            "A", 0, "1", "", rank,
            "B", 0, "2", "", 99,
            "", state, "", "");
}
