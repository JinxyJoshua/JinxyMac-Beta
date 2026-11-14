using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// The Server page: which game server you are on, and where it is.
/// </summary>
/// <remarks>
/// Reads Roblox's own log — see <see cref="RobloxServerLog"/> — so it needs no
/// permission of any kind and touches the game not at all. That makes it one of
/// the few pages that works on a Mac which has granted the app nothing, which is
/// most of them at the moment.
///
/// <para>There is no ping here, and that is deliberate rather than unfinished.
/// Roblox fronts every server with a relay that does not answer ICMP, and timing
/// a TCP handshake against the game port measures the relay rather than the round
/// trip the player feels. A figure that looked like latency but was not one would
/// be worse than no figure, so this answers where, and stops.</para>
/// </remarks>
public partial class MainWindow
{
    private readonly IpRegionLookup _regions = new();

    private DispatcherTimer? _serverTimer;
    private CancellationTokenSource? _serverLookup;

    private ServerEndpoint? _serverSeen;
    private DateTime _serverSince;

    /// <summary>
    /// How often the log is re-read while the page is open.
    /// </summary>
    /// <remarks>
    /// Two seconds: a server hop should show up about as fast as the loading
    /// screen clears, and reading one file that Roblox has already written costs
    /// nothing worth saving.
    /// </remarks>
    private static readonly TimeSpan ServerPoll = TimeSpan.FromSeconds(2);

    private void WireServer()
    {
        ServerRefreshButton.Click += (_, _) =>
        {
            // Forgets what it last saw, so the region is looked up again rather
            // than answered from memory. The point of the button is to re-ask.
            _serverSeen = null;
            RefreshServer();
        };
    }

    /// <summary>Starts watching the log, and reads it once straight away.</summary>
    private void EnterServer()
    {
        _serverTimer ??= new DispatcherTimer { Interval = ServerPoll };
        _serverTimer.Tick -= ServerTick;
        _serverTimer.Tick += ServerTick;
        _serverTimer.Start();

        RefreshServer();
    }

    /// <summary>
    /// Stops when the page is left.
    /// </summary>
    /// <remarks>
    /// A timer left running would keep reading a file and firing lookups for a
    /// page nobody is looking at, which is the kind of thing that shows up as an
    /// idle app using battery.
    /// </remarks>
    private void LeavingServer()
    {
        _serverTimer?.Stop();

        _serverLookup?.Cancel();
        _serverLookup?.Dispose();
        _serverLookup = null;
    }

    private void ServerTick(object? sender, EventArgs e) => RefreshServer();

    private void RefreshServer()
    {
        ServerEndpoint? now = RobloxServerLog.CurrentServer();

        if (now is not { } server)
        {
            _serverSeen = null;

            ServerHeadlineText.Text = "Not in a game";
            ServerDetailText.Text = RobloxServerLog.Explain();
            ServerAddressText.Text = "—";
            ServerPortText.Text = "—";
            ServerSinceText.Text = "—";

            return;
        }

        // Same server as the last read: nothing to re-say, and nothing to look
        // up. Without this the page would ask ip-api every two seconds.
        if (_serverSeen == server)
        {
            ServerSinceText.Text = RobloxServerLog.TimeOnServer(DateTime.UtcNow - _serverSince);
            return;
        }

        _serverSeen = server;
        _serverSince = DateTime.UtcNow;

        ServerAddressText.Text = server.Ip;
        ServerPortText.Text = server.Port.ToString();
        ServerSinceText.Text = "just now";

        ServerHeadlineText.Text = "Looking up…";
        ServerDetailText.Text = "";

        LookUpRegion(server);
    }

    /// <summary>
    /// Asks where this server is, and shows it when the answer comes back.
    /// </summary>
    /// <remarks>
    /// Started rather than awaited: the poll must not stall on a slow lookup, and
    /// a hop to another server while one is in flight cancels it — otherwise the
    /// older answer could land last and label the new server with the old
    /// server's city.
    /// </remarks>
    private void LookUpRegion(ServerEndpoint server)
    {
        _serverLookup?.Cancel();
        _serverLookup?.Dispose();
        _serverLookup = new CancellationTokenSource();

        CancellationToken token = _serverLookup.Token;

        _ = Task.Run(async () =>
        {
            ServerRegion? region = await _regions.LookupAsync(server.Ip, token)
                .ConfigureAwait(false);

            if (token.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Checked again on the UI thread: the player may have hopped
                // between the lookup finishing and this running.
                if (token.IsCancellationRequested || _serverSeen != server) return;

                if (region == null)
                {
                    ServerHeadlineText.Text = "Somewhere unlisted";
                    ServerDetailText.Text =
                        "The server answered, but its address could not be placed.";
                    return;
                }

                ServerHeadlineText.Text = region.Headline;
                ServerDetailText.Text = region.Detail;
            });
        }, token);
    }

}
