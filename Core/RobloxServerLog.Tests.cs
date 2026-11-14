using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Finding the game server in Roblox's own log.
/// </summary>
/// <remarks>
/// Against log text and real temporary folders rather than a running game: the
/// lines below are the shapes the client actually writes, and they can hold a
/// server hop, a half-written connection and a private address at once, which one
/// live session cannot.
/// </remarks>
public class RobloxServerLogTests
{
    private const string ServerIdLine =
        "2026-11-13T02:14:51.812Z,1.234,abc,6 [FLog::Network] serverId: 128.116.48.33|64508";

    private const string UdmuxLine =
        "2026-11-13T02:14:51.004Z,0.998,abc,6 [FLog::Network] UDMUX Address = 51.81.46.12, "
        + "Port = 50012 | RCC Server Address = 10.128.4.7, Port = 53640";

    private static ServerEndpoint Found(string? log)
    {
        ServerEndpoint? found = RobloxServerLog.FindCurrentServer(log);

        Assert.True(found.HasValue, "expected a server to be found");
        return found!.Value;
    }

    // ---- picking the address out of the text ----

    [Fact]
    public void TheServerIdLineIsRead()
    {
        ServerEndpoint ep = Found(ServerIdLine);

        Assert.Equal("128.116.48.33", ep.Ip);
        Assert.Equal(64508, ep.Port);
    }

    /// <summary>
    /// The address Roblox writes before serverId, used for the moment between the
    /// connection starting and serverId being logged.
    /// </summary>
    [Fact]
    public void TheUdmuxLineIsReadWhenItIsAllThereIs()
    {
        ServerEndpoint ep = Found(UdmuxLine);

        Assert.Equal("51.81.46.12", ep.Ip);
        Assert.Equal(50012, ep.Port);
    }

    /// <summary>
    /// The RCC address after the pipe is a private 10.x one that means nothing
    /// outside Roblox's network, and it sits on the same line as a usable
    /// address — so taking the wrong one would look like it had worked.
    /// </summary>
    [Fact]
    public void ThePrivateRccAddressOnTheSameLineIsIgnored()
    {
        ServerEndpoint ep = Found(UdmuxLine);

        Assert.DoesNotContain("10.128", ep.Ip);
        Assert.NotEqual(53640, ep.Port);
    }

    /// <summary>
    /// A session hops servers and every hop writes a fresh line, so the last one
    /// is where the player is now and the earlier ones are history.
    /// </summary>
    [Fact]
    public void TheMostRecentServerWins()
    {
        string log = string.Join("\n",
            "[FLog::Network] serverId: 1.2.3.4|100",
            "some other logging",
            "[FLog::Network] serverId: 5.6.7.8|200");

        ServerEndpoint ep = Found(log);

        Assert.Equal("5.6.7.8", ep.Ip);
        Assert.Equal(200, ep.Port);
    }

    /// <summary>
    /// The two lines sit a few lines apart describing one connection, and
    /// serverId is the tidier of them, so it wins wherever it is the newer.
    /// </summary>
    [Fact]
    public void ServerIdWinsOverAnEarlierUdmuxForTheSameConnection()
    {
        Assert.Equal("128.116.48.33", Found(UdmuxLine + "\n" + ServerIdLine).Ip);
    }

    /// <summary>
    /// And loses when the UDMUX line is the newer, which is the player having
    /// just hopped to a server whose serverId has not been written yet.
    /// </summary>
    [Fact]
    public void ANewerUdmuxWinsOverAnOlderServerId()
    {
        Assert.Equal("51.81.46.12", Found(ServerIdLine + "\n" + UdmuxLine).Ip);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a log with no connection in it at all")]
    [InlineData("[FLog::Network] serverId: not-an-address|64508")]
    public void NothingToFindIsNull(string? log)
    {
        Assert.Null(RobloxServerLog.FindCurrentServer(log));
    }

    /// <summary>A port outside the range is not a port.</summary>
    [Theory]
    [InlineData("[FLog::Network] serverId: 1.2.3.4|0")]
    [InlineData("[FLog::Network] serverId: 1.2.3.4|99999")]
    public void AnImpossiblePortIsRejected(string log)
    {
        Assert.Null(RobloxServerLog.FindCurrentServer(log));
    }

    // ---- addresses that cannot be a game server ----

    [Theory]
    [InlineData("10.128.4.7")]       // private
    [InlineData("127.0.0.1")]        // loopback
    [InlineData("192.168.1.10")]     // private
    [InlineData("172.16.0.1")]       // private
    [InlineData("172.31.255.254")]   // private, top of the range
    [InlineData("169.254.1.1")]      // link-local
    [InlineData("100.64.0.1")]       // carrier-grade NAT
    [InlineData("0.0.0.0")]          // this-network
    public void APrivateOrReservedAddressIsNotPublic(string ip)
    {
        Assert.False(RobloxServerLog.IsPublic(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("128.116.48.33")]
    [InlineData("51.81.46.12")]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.0.1")]       // just below the private block
    [InlineData("172.32.0.1")]       // just above it
    [InlineData("100.63.0.1")]       // just below carrier-grade NAT
    [InlineData("100.128.0.1")]      // just above it
    public void ARealAddressIsPublic(string ip)
    {
        Assert.True(RobloxServerLog.IsPublic(IPAddress.Parse(ip)));
    }

    /// <summary>Roblox logs IPv4 servers only; anything else is not one.</summary>
    [Fact]
    public void AnIpv6AddressIsNotTreatedAsAServer()
    {
        Assert.False(RobloxServerLog.IsPublic(IPAddress.Parse("2001:4860:4860::8888")));
    }

    [Fact]
    public void APrivateAddressInTheLogIsNeverReturned()
    {
        Assert.Null(RobloxServerLog.FindCurrentServer("[FLog::Network] serverId: 10.0.0.5|64508"));
    }

    // ---- finding the log on disk ----

    [Fact]
    public void TheNewestPlayerLogIsChosen()
    {
        using var dir = new TempDir();

        dir.Write("0.1_20260101T000000Z_Player_1_last.log", "old", DateTime.UtcNow.AddHours(-2));
        dir.Write("0.1_20260102T000000Z_Player_2_last.log", "new", DateTime.UtcNow);

        Assert.EndsWith("_Player_2_last.log", RobloxServerLog.NewestPlayerLog(dir.Path));
    }

    /// <summary>
    /// The crash handler shares the folder and writes no connection lines, so
    /// picking its log — often the newest file there — would report no server
    /// while the player is in a game.
    /// </summary>
    [Fact]
    public void ACrashHandlerLogIsPassedOver()
    {
        using var dir = new TempDir();

        dir.Write("0.1_Player_1_last.log", "real", DateTime.UtcNow.AddHours(-1));
        dir.Write("0.1_Player_CrashHandler_2.log", "not a session", DateTime.UtcNow);

        Assert.EndsWith("_Player_1_last.log", RobloxServerLog.NewestPlayerLog(dir.Path));
    }

    /// <summary>
    /// The Mac client has moved these files about between versions, and a rename
    /// would otherwise turn the whole page off with nothing to show why.
    /// </summary>
    [Fact]
    public void ALogNotMatchingTheUsualNameIsStillConsidered()
    {
        using var dir = new TempDir();

        dir.Write("RobloxPlayer.log", "renamed by a future client", DateTime.UtcNow);

        Assert.EndsWith("RobloxPlayer.log", RobloxServerLog.NewestPlayerLog(dir.Path));
    }

    /// <summary>But the properly named log still wins when both are there.</summary>
    [Fact]
    public void TheProperlyNamedLogIsPreferredEvenIfItIsOlder()
    {
        using var dir = new TempDir();

        dir.Write("0.1_Player_1_last.log", "the real one", DateTime.UtcNow.AddDays(-1));
        dir.Write("something-else.log", "newer but not a session", DateTime.UtcNow);

        Assert.EndsWith("_Player_1_last.log", RobloxServerLog.NewestPlayerLog(dir.Path));
    }

    [Fact]
    public void NoFolderMeansNoLog()
    {
        Assert.Null(RobloxServerLog.NewestPlayerLog(Missing()));
    }

    [Fact]
    public void AnEmptyFolderMeansNoLog()
    {
        using var dir = new TempDir();

        Assert.Null(RobloxServerLog.NewestPlayerLog(dir.Path));
    }

    /// <summary>
    /// The live log is held open by the client for writing, and an ordinary read
    /// of it throws — which is the whole reason it is read the way it is, so it
    /// is worth pinning.
    /// </summary>
    [Fact]
    public void ALogRobloxStillHasOpenCanBeRead()
    {
        using var dir = new TempDir();

        string path = Path.Combine(dir.Path, "0.1_Player_1_last.log");

        using var held = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

        byte[] bytes = Encoding.UTF8.GetBytes(ServerIdLine);
        held.Write(bytes, 0, bytes.Length);
        held.Flush();

        Assert.Contains("serverId", RobloxServerLog.ReadShared(path) ?? "");
    }

    [Fact]
    public void AMissingFileReadsAsNullRatherThanThrowing()
    {
        Assert.Null(RobloxServerLog.ReadShared(
            Path.Combine(Path.GetTempPath(), "jinxy-missing-" + Guid.NewGuid() + ".log")));
    }

    [Fact]
    public void TheCurrentServerIsFoundFromAFolderInOneCall()
    {
        using var dir = new TempDir();

        dir.Write("0.1_Player_1_last.log", ServerIdLine, DateTime.UtcNow);

        ServerEndpoint? ep = RobloxServerLog.CurrentServer(dir.Path);

        Assert.True(ep.HasValue);
        Assert.Equal("128.116.48.33", ep!.Value.Ip);
    }

    // ---- saying why there is nothing ----

    /// <summary>
    /// Three different nothings reach the page, and each calls for something
    /// different from the person reading it. One shared "no server found" is what
    /// makes a working feature look broken.
    /// </summary>
    [Fact]
    public void EachKindOfNothingSaysSomethingDifferent()
    {
        string noFolder = RobloxServerLog.Explain(Missing());

        using var empty = new TempDir();
        string noLogs = RobloxServerLog.Explain(empty.Path);

        using var quiet = new TempDir();
        quiet.Write("0.1_Player_1_last.log", "no connection in here", DateTime.UtcNow);
        string noGame = RobloxServerLog.Explain(quiet.Path);

        Assert.Contains("Launch Roblox", noFolder);
        Assert.Contains("nothing in it", noLogs);
        Assert.Contains("Not in a game", noGame);

        Assert.Equal(3, new[] { noFolder, noLogs, noGame }.Distinct().Count());
    }

    [Fact]
    public void TheLogsAreListedNewestFirst()
    {
        using var dir = new TempDir();

        dir.Write("old.log", "a", DateTime.UtcNow.AddHours(-3));
        dir.Write("new.log", "b", DateTime.UtcNow);

        Assert.Equal(new[] { "new.log", "old.log" }, RobloxServerLog.Logs(dir.Path));
    }

    [Fact]
    public void NoFolderListsNoLogs()
    {
        Assert.Empty(RobloxServerLog.Logs(Missing()));
    }

    // ---- how long you have been on it ----

    /// <summary>
    /// The question this answers is "did my rejoin actually move me?", so the
    /// first stretch has to read as fresh and anything older must not.
    /// </summary>
    [Theory]
    [InlineData(0, "just now")]
    [InlineData(44, "just now")]
    [InlineData(45, "1 min")]
    [InlineData(119, "1 min")]
    [InlineData(120, "2 min")]
    [InlineData(59 * 60, "59 min")]
    [InlineData(60 * 60, "1h")]
    [InlineData(90 * 60, "1h 30m")]
    [InlineData(3 * 60 * 60, "3h")]
    public void TimeOnAServerIsRoundedToSomethingReadable(int seconds, string expected)
    {
        Assert.Equal(expected, RobloxServerLog.TimeOnServer(TimeSpan.FromSeconds(seconds)));
    }

    /// <summary>
    /// A clock that steps backwards — which a Mac does on waking from sleep —
    /// must not produce a negative duration on the page.
    /// </summary>
    [Fact]
    public void ABackwardsClockStillReadsAsJustNow()
    {
        Assert.Equal("just now", RobloxServerLog.TimeOnServer(TimeSpan.FromSeconds(-30)));
    }

    // ---- where it looks ----

    /// <summary>
    /// The same folder the app already clears as Roblox's "Logs" on this
    /// platform, so the two cannot drift into one of them being wrong.
    /// </summary>
    [Fact]
    public void TheDefaultFolderIsWhereThisPlatformKeepsRobloxLogs()
    {
        Assert.EndsWith(
            OperatingSystem.IsMacOS()
                ? Path.Combine("Library", "Logs", "Roblox")
                : Path.Combine("Roblox", "logs"),
            RobloxServerLog.DefaultLogDirectory);
    }

    private static string Missing() =>
        Path.Combine(Path.GetTempPath(), "jinxy-no-such-folder-" + Guid.NewGuid().ToString("N"));

    /// <summary>A folder that cleans itself up, so a failed test leaves nothing.</summary>
    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "jinxy-logs-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string name, string text, DateTime writtenUtc)
        {
            string file = System.IO.Path.Combine(Path, name);

            File.WriteAllText(file, text);
            File.SetLastWriteTimeUtc(file, writtenUtc);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A temp folder left behind is harmless.
            }
        }
    }
}
