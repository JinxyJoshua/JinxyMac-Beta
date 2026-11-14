using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Turning an ip-api answer into a place, and a place into two lines of text.
/// </summary>
public class ServerRegionTests
{
    private const string Success = """
    {"status":"success","country":"United States","countryCode":"US",
     "regionName":"Virginia","city":"Ashburn","lat":39.0438,"lon":-77.4874}
    """;

    // ---- reading the answer ----

    [Fact]
    public void ASuccessfulLookupIsRead()
    {
        ServerRegion r = Assert.IsType<ServerRegion>(
            ServerRegion.FromApiJson(Success, "128.116.48.33"));

        Assert.Equal("128.116.48.33", r.Ip);
        Assert.Equal("US", r.CountryCode);
        Assert.Equal("United States", r.Country);
        Assert.Equal("Virginia", r.Region);
        Assert.Equal("Ashburn", r.City);
        Assert.Equal(39.0438, r.Lat, 4);
        Assert.Equal(-77.4874, r.Lon, 4);
    }

    /// <summary>
    /// ip-api answers a reserved or unplaceable address with a failed status and
    /// an HTTP 200, so the status is the only thing that says it did not work.
    /// </summary>
    [Fact]
    public void AFailedLookupIsNoRegion()
    {
        Assert.Null(ServerRegion.FromApiJson(
            """{"status":"fail","message":"reserved range"}""", "10.0.0.1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("{\"status\":42}")]
    public void AnythingUnreadableIsNoRegionRatherThanAThrow(string? json)
    {
        Assert.Null(ServerRegion.FromApiJson(json, "1.2.3.4"));
    }

    /// <summary>A partial answer is still worth showing whatever it did carry.</summary>
    [Fact]
    public void MissingFieldsReadAsEmptyRatherThanFailing()
    {
        ServerRegion r = Assert.IsType<ServerRegion>(
            ServerRegion.FromApiJson("""{"status":"success","country":"Japan"}""", "1.1.1.1"));

        Assert.Equal("Japan", r.Country);
        Assert.Equal("", r.City);
        Assert.Equal("", r.Region);
        Assert.Equal(0, r.Lat);
        Assert.Equal(0, r.Lon);
    }

    // ---- the two lines on the page ----

    [Fact]
    public void TheHeadlineIsTheFlagAndTheCity()
    {
        Assert.Equal("\U0001F1FA\U0001F1F8 Ashburn", Region("US", "United States", "Virginia", "Ashburn").Headline);
    }

    [Fact]
    public void WithNoCityTheHeadlineFallsBackToTheCountry()
    {
        Assert.Equal("\U0001F1EF\U0001F1F5 Japan", Region("JP", "Japan", "", "").Headline);
    }

    [Fact]
    public void WithNothingPlacedTheHeadlineSaysSo()
    {
        Assert.Equal("Unknown", Region("", "", "", "").Headline);
    }

    [Fact]
    public void TheDetailLineIsRegionThenCountry()
    {
        Assert.Equal("Virginia, United States",
            Region("US", "United States", "Virginia", "Ashburn").Detail);
    }

    /// <summary>
    /// A city-state answers with the same word for city, region and country, so
    /// the quiet line has nothing left to add and says nothing — rather than
    /// "Singapore, Singapore" under a headline that already reads Singapore.
    /// </summary>
    [Fact]
    public void TheDetailLineDoesNotRepeatTheCityOrTheCountry()
    {
        Assert.Equal("", Region("SG", "Singapore", "Singapore", "Singapore").Detail);
    }

    /// <summary>
    /// But a city inside a differently named region keeps both, which is the
    /// ordinary case and the reason the line exists.
    /// </summary>
    [Fact]
    public void ARegionThatAddsSomethingIsKept()
    {
        Assert.Equal("Bavaria, Germany", Region("DE", "Germany", "Bavaria", "Munich").Detail);
    }

    [Fact]
    public void WithNothingLeftToSayTheDetailLineIsEmpty()
    {
        Assert.Equal("", Region("US", "", "", "Ashburn").Detail);
    }

    // ---- flags ----

    /// <summary>
    /// A flag is its two letters as regional-indicator symbols, which is two
    /// surrogate pairs — four chars — not two.
    /// </summary>
    [Fact]
    public void AFlagIsTwoRegionalIndicatorsForTheCountryCode()
    {
        Assert.Equal("\U0001F1FA\U0001F1F8", ServerRegion.FlagFor("US"));
        Assert.Equal("\U0001F1E9\U0001F1EA", ServerRegion.FlagFor("DE"));
        Assert.Equal(4, ServerRegion.FlagFor("GB").Length);
    }

    [Fact]
    public void ALowercaseCodeStillGetsItsFlag()
    {
        Assert.Equal(ServerRegion.FlagFor("US"), ServerRegion.FlagFor("us"));
    }

    /// <summary>No flag at all, rather than a wrong one built from junk.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("U")]
    [InlineData("USA")]
    [InlineData("12")]
    [InlineData("U1")]
    [InlineData("  ")]
    public void AnythingThatIsNotTwoLettersGetsNoFlag(string? code)
    {
        Assert.Equal("", ServerRegion.FlagFor(code));
    }

    private static ServerRegion Region(string code, string country, string region, string city) =>
        new("1.2.3.4", code, country, region, city);
}
