using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// The Scoreboard page: real games, live, while you play.
/// </summary>
/// <remarks>
/// Rows are built in code rather than from a template with bindings. The machine
/// this is written on cannot launch the app, so a binding path that was wrong
/// would fail silently on someone else's Mac and show an empty row; built this
/// way the compiler catches the same mistake here.
///
/// <para>Everything about what to show and in what order is in
/// <see cref="SportsScores"/>, with no screen attached, and is tested against a
/// captured payload. This turns that into controls.</para>
/// </remarks>
public partial class MainWindow
{
    private ScoreLeague _scoreLeague = ScoreLeague.CollegeFootball;
    private bool _scoreRankedOnly;

    private DispatcherTimer? _scoreTimer;
    private CancellationTokenSource? _scoreFetch;

    /// <summary>
    /// How often the scoreboard re-fetches while the page is open.
    /// </summary>
    /// <remarks>
    /// Half a minute. A score changes a handful of times an hour, and ESPN is
    /// doing this for free — polling it every second would be both rude and no
    /// more informative.
    /// </remarks>
    private static readonly TimeSpan ScorePoll = TimeSpan.FromSeconds(30);

    private static readonly HttpClient LogoHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Logos already fetched, by URL.
    /// </summary>
    /// <remarks>
    /// Keyed on the task rather than the picture so two rows asking for the same
    /// badge at the same time — which is every refresh — share one download.
    /// Held for the life of the app: these are a few kilobytes each and a team's
    /// badge does not change during a session.
    /// </remarks>
    private static readonly Dictionary<string, Task<Bitmap?>> Logos = new();

    private void WireScoreboard()
    {
        foreach (RadioButton choice in ScoreLeaguePanel.Children.OfType<RadioButton>())
        {
            choice.IsCheckedChanged += (sender, _) =>
            {
                if (sender is not RadioButton { IsChecked: true, Tag: string tag }) return;
                if (!Enum.TryParse(tag, out ScoreLeague league)) return;

                // Putting the remembered league back at startup ticks a button,
                // which arrives here; it must not be mistaken for a choice and
                // written back, or fetched before the window is even up.
                if (_loading || league == _scoreLeague) return;

                _scoreLeague = league;
                _settings.ScoreLeague = league.ToString();
                _settings.Save();

                ShowRankedToggle();
                FetchScores();
            };
        }

        ScoreRankedOnly.IsCheckedChanged += (_, _) =>
        {
            bool on = ScoreRankedOnly.IsChecked == true;
            if (_loading || on == _scoreRankedOnly) return;

            _scoreRankedOnly = on;
            _settings.ScoreTop25 = on;
            _settings.Save();

            FetchScores();
        };

        ScoreRefreshButton.Click += (_, _) => FetchScores();
    }

    /// <summary>Puts the remembered league and filter back.</summary>
    private void LoadScoreboard(AppSettings s)
    {
        _scoreLeague = Enum.TryParse(s.ScoreLeague, out ScoreLeague league)
            ? league
            : ScoreLeague.CollegeFootball;

        _scoreRankedOnly = s.ScoreTop25;

        foreach (RadioButton choice in ScoreLeaguePanel.Children.OfType<RadioButton>())
            choice.IsChecked = choice.Tag as string == _scoreLeague.ToString();

        ScoreRankedOnly.IsChecked = _scoreRankedOnly;

        ShowRankedToggle();
    }

    /// <summary>
    /// The Top 25 filter only belongs to college football.
    /// </summary>
    /// <remarks>
    /// The AP poll is a college-football thing: left on the NHL page it would be
    /// a control that silently does nothing, which is worse than one that is not
    /// there.
    /// </remarks>
    private void ShowRankedToggle() =>
        ScoreRankedOnly.IsVisible = _scoreLeague == ScoreLeague.CollegeFootball;

    private void EnterScoreboard()
    {
        _scoreTimer ??= new DispatcherTimer { Interval = ScorePoll };
        _scoreTimer.Tick -= ScoreTick;
        _scoreTimer.Tick += ScoreTick;
        _scoreTimer.Start();

        FetchScores();
    }

    /// <summary>Stops fetching when the page is left.</summary>
    private void LeavingScoreboard()
    {
        _scoreTimer?.Stop();

        _scoreFetch?.Cancel();
        _scoreFetch?.Dispose();
        _scoreFetch = null;
    }

    private void ScoreTick(object? sender, EventArgs e) => FetchScores();

    private void FetchScores()
    {
        _scoreFetch?.Cancel();
        _scoreFetch?.Dispose();
        _scoreFetch = new CancellationTokenSource();

        CancellationToken token = _scoreFetch.Token;
        ScoreLeague asked = _scoreLeague;
        bool rankedOnly = _scoreRankedOnly && asked == ScoreLeague.CollegeFootball;

        ScoreHint.Text = $"Loading {SportsScores.DisplayName(asked).ToLowerInvariant()}…";

        _ = Task.Run(async () =>
        {
            List<GameInfo> games = await SportsScores.GetGamesAsync(asked, token)
                .ConfigureAwait(false);

            if (token.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // A league switched while this was in flight: the older answer
                // must not be drawn over the newer league's board.
                if (token.IsCancellationRequested || asked != _scoreLeague) return;

                ShowGames(
                    rankedOnly ? SportsScores.RankedOnly(games) : SportsScores.InWatchingOrder(games),
                    asked,
                    rankedOnly);
            });
        }, token);
    }

    private void ShowGames(List<GameInfo> games, ScoreLeague league, bool rankedOnly)
    {
        ScoreList.Children.Clear();

        if (games.Count == 0)
        {
            ScoreHint.Text = rankedOnly
                ? "No ranked games on today. Turn off Top 25 to see the rest."
                : $"No {SportsScores.DisplayName(league).ToLowerInvariant()} games today.";

            return;
        }

        int live = games.Count(g => g.InProgress);

        ScoreHint.Text = live > 0
            ? $"{live} live now, {games.Count} today."
            : $"{games.Count} today. Nothing live.";

        foreach (GameInfo game in games)
            ScoreList.Children.Add(GameCard(game));
    }

    /// <summary>One game: two teams, their scores, and what is happening.</summary>
    private Control GameCard(GameInfo game)
    {
        var rows = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto")
        };

        AddTeamRow(rows, 0, game.AwayLogo, game.AwayAbbr, game.AwayRank,
            game.AwayScore, game.AwayHasBall, game);

        AddTeamRow(rows, 1, game.HomeLogo, game.HomeAbbr, game.HomeRank,
            game.HomeScore, game.HomeHasBall, game);

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(rows);

        // The status line, and the down and distance when there is one. A live
        // game's status is the accent: it is the one thing on this page worth
        // looking at twice, and everything else stays quiet so that reads.
        string status = game.StatusText;

        if (game.DownDistance.Length > 0 && game.InProgress)
            status = status.Length > 0 ? $"{status}  ·  {game.DownDistance}" : game.DownDistance;

        if (status.Length > 0)
            stack.Children.Add(new TextBlock
            {
                Text = status,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = game.InProgress ? Accent() : Muted(),
                FontWeight = game.InProgress ? FontWeight.SemiBold : FontWeight.Normal
            });

        var card = new Border { Padding = new Thickness(16, 14, 16, 14), Child = stack };
        card.Classes.Add("card");

        return card;
    }

    private void AddTeamRow(
        Grid grid, int row, string logoUrl, string abbr, int rank,
        int score, bool hasBall, GameInfo game)
    {
        var logo = new Image
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 3, 12, 3),
            VerticalAlignment = VerticalAlignment.Center
        };

        ShowLogo(logo, logoUrl);

        Grid.SetRow(logo, row);
        Grid.SetColumn(logo, 0);
        grid.Children.Add(logo);

        var name = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        // The rank, when the team has one. Small and quiet: it qualifies the
        // name rather than competing with the score.
        if (rank is > 0 and <= 25)
            name.Children.Add(new TextBlock
            {
                Text = rank.ToString(),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Accent(),
                VerticalAlignment = VerticalAlignment.Center
            });

        name.Children.Add(new TextBlock
        {
            Text = abbr.Length > 0 ? abbr : "—",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Bright(),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Who has the ball. Football only, and only while the game is live.
        if (hasBall)
            name.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = Accent(),
                VerticalAlignment = VerticalAlignment.Center
            });

        Grid.SetRow(name, row);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var figure = new TextBlock
        {
            // Nothing at all before kick-off: a fixture hours away carries two
            // zeros, and "0" against "0" reads as a scoreless game rather than
            // as one that has not happened.
            Text = game.Upcoming ? "" : score.ToString(),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = game.Upcoming ? Muted() : Bright(),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 36,
            TextAlignment = TextAlignment.Right
        };

        Grid.SetRow(figure, row);
        Grid.SetColumn(figure, 2);
        grid.Children.Add(figure);
    }

    /// <summary>
    /// Puts a team's badge on an image, once it has arrived.
    /// </summary>
    /// <remarks>
    /// Fire and forget, and silent on failure: a missing badge leaves the row
    /// showing the abbreviation, which is already readable. A scoreboard that
    /// refused to draw because a logo 404'd would be a worse trade.
    /// </remarks>
    private static void ShowLogo(Image target, string url)
    {
        if (url.Length == 0) return;

        Task<Bitmap?> fetch;

        lock (Logos)
        {
            if (!Logos.TryGetValue(url, out Task<Bitmap?>? existing))
            {
                existing = FetchLogoAsync(url);
                Logos[url] = existing;
            }

            fetch = existing;
        }

        if (fetch.IsCompletedSuccessfully)
        {
            target.Source = fetch.Result;
            return;
        }

        _ = fetch.ContinueWith(
            done => Dispatcher.UIThread.Post(() => target.Source = done.Result),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private static async Task<Bitmap?> FetchLogoAsync(string url)
    {
        try
        {
            byte[] bytes = await LogoHttp.GetByteArrayAsync(url).ConfigureAwait(false);

            // An error page is not a picture, and decoding one would throw where
            // a missing logo would simply have been blank.
            if (bytes.Length < 64) return null;

            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    // ---- colours, taken from the theme rather than hard-coded ----

    private static IBrush Accent() => Themed("Accent", Colors.OrangeRed);
    private static IBrush Bright() => Themed("TextBright", Colors.White);
    private static IBrush Muted() => Themed("TextMuted", Colors.Gray);

    /// <summary>
    /// The app's own brush for a palette key.
    /// </summary>
    /// <remarks>
    /// The shared brush itself, not a copy of its colour. Switching mode or
    /// accent moves those brushes in place (see <c>Recolour</c>), so a row built
    /// from the brush follows the change, while a row built from a copy of the
    /// colour would keep the old one until the board happened to refresh.
    /// </remarks>
    private static IBrush Themed(string key, Color fallback) =>
        Application.Current?.Resources.TryGetResource(key, null, out object? value) == true
        && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
