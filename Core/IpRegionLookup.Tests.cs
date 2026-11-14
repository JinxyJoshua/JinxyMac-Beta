using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Looking up where a server is, without going near the real service.
/// </summary>
/// <remarks>
/// A stub handler stands in for the network, which is what makes the two things
/// worth checking here checkable at all: that a lookup sends the server's address
/// and nothing else, and that it happens once per server rather than once per
/// poll — the page re-reads the log every couple of seconds, so a cache that
/// quietly did not work would turn into a few hundred requests an hour and a rate
/// limit.
/// </remarks>
public class IpRegionLookupTests
{
    private const string Success = """
    {"status":"success","country":"United States","countryCode":"US",
     "regionName":"Virginia","city":"Ashburn","lat":39.04,"lon":-77.48}
    """;

    // ---- what is sent ----

    [Fact]
    public void TheLookupAsksAboutTheAddressItWasGiven()
    {
        Assert.Contains("128.116.48.33", IpRegionLookup.UrlFor("128.116.48.33"));
    }

    /// <summary>
    /// Only the fields the page shows. Asking for everything would pull back an
    /// ISP and an organisation name that nothing here displays.
    /// </summary>
    [Fact]
    public void TheLookupAsksOnlyForTheFieldsThatAreShown()
    {
        string url = IpRegionLookup.UrlFor("1.2.3.4");

        Assert.Contains("city", url);
        Assert.Contains("countryCode", url);
        Assert.DoesNotContain("isp", url);
        Assert.DoesNotContain("org", url);
    }

    // ---- the answer ----

    [Fact]
    public async Task ASuccessfulLookupBecomesARegion()
    {
        var stub = new Stub(Success);
        var lookup = new IpRegionLookup(new HttpClient(stub));

        ServerRegion? region = await lookup.LookupAsync("128.116.48.33");

        Assert.NotNull(region);
        Assert.Equal("Ashburn", region!.City);
        Assert.Equal("128.116.48.33", region.Ip);
    }

    /// <summary>
    /// The page polls the log every couple of seconds and asks about the server
    /// each time. A server's location does not move, so the second ask must come
    /// from memory.
    /// </summary>
    [Fact]
    public async Task TheSameServerIsOnlyLookedUpOnce()
    {
        var stub = new Stub(Success);
        var lookup = new IpRegionLookup(new HttpClient(stub));

        for (int i = 0; i < 5; i++)
            Assert.NotNull(await lookup.LookupAsync("128.116.48.33"));

        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task ADifferentServerIsLookedUpSeparately()
    {
        var stub = new Stub(Success);
        var lookup = new IpRegionLookup(new HttpClient(stub));

        await lookup.LookupAsync("128.116.48.33");
        await lookup.LookupAsync("51.81.46.12");

        Assert.Equal(2, stub.Calls);
        Assert.Equal(
            new[] { "128.116.48.33", "51.81.46.12" },
            stub.Asked);
    }

    // ---- things going wrong ----

    /// <summary>
    /// ip-api answers an address it cannot place with a failed status and an HTTP
    /// 200, so there is nothing but the body to tell it apart from a hit.
    /// </summary>
    [Fact]
    public async Task AnAddressTheServiceCannotPlaceIsNoRegion()
    {
        var lookup = new IpRegionLookup(
            new HttpClient(new Stub("""{"status":"fail","message":"reserved range"}""")));

        Assert.Null(await lookup.LookupAsync("10.0.0.1"));
    }

    /// <summary>A failure is not cached: the next poll should try again.</summary>
    [Fact]
    public async Task AFailedLookupIsTriedAgainNextTime()
    {
        var stub = new Stub("""{"status":"fail"}""");
        var lookup = new IpRegionLookup(new HttpClient(stub));

        await lookup.LookupAsync("1.2.3.4");
        await lookup.LookupAsync("1.2.3.4");

        Assert.Equal(2, stub.Calls);
    }

    [Fact]
    public async Task NoNetworkIsNoRegionRatherThanAThrow()
    {
        var lookup = new IpRegionLookup(
            new HttpClient(new Stub(new HttpRequestException("no route to host"))));

        Assert.Null(await lookup.LookupAsync("1.2.3.4"));
    }

    [Fact]
    public async Task ATimeoutIsNoRegionRatherThanAThrow()
    {
        var lookup = new IpRegionLookup(
            new HttpClient(new Stub(new TaskCanceledException("timed out"))));

        Assert.Null(await lookup.LookupAsync("1.2.3.4"));
    }

    /// <summary>
    /// An empty address asks ip-api where the *caller* is, which this app has no
    /// use for and should never ask. It must not even reach the network.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyAddressIsNeverSent(string ip)
    {
        var stub = new Stub(Success);
        var lookup = new IpRegionLookup(new HttpClient(stub));

        Assert.Null(await lookup.LookupAsync(ip));
        Assert.Equal(0, stub.Calls);
    }

    /// <summary>The network in place of the network.</summary>
    private sealed class Stub : HttpMessageHandler
    {
        private readonly string? _body;
        private readonly Exception? _throws;

        public Stub(string body) => _body = body;
        public Stub(Exception throws) => _throws = throws;

        public int Calls { get; private set; }

        /// <summary>Every address asked about, in order.</summary>
        public List<string> Asked { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;

            // The address is the last path segment of the ip-api URL.
            Asked.Add(request.RequestUri!.AbsolutePath.TrimEnd('/').Split('/')[^1]);

            if (_throws != null) throw _throws;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body ?? "")
            });
        }
    }
}
