using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// A file on the internet that changes how this app behaves. The bounds are
/// the whole safety story, so they are what gets pinned here: a hostile or
/// mistaken config must only ever be able to pick a value the app would have
/// accepted from its own settings screen.
/// </summary>
public class RemoteConfigTests
{
    [Fact]
    public void EmptyObject_KeepsEveryShippedDefault()
    {
        RemoteConfig c = RemoteConfig.Parse("{}");

        Assert.Equal(ClickTimings.DefaultHitFixMinDownMs, c.HitFixMinDownMs);
        Assert.Equal(ClickTimings.DefaultHitFixMinUpMs, c.HitFixMinUpMs);
        Assert.True(c.RecorderEnabled);
        Assert.True(c.KitArtFetchEnabled);
        Assert.Equal("", c.Notice);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    [InlineData("")]
    public void UnusableJson_FallsBackToDefaults(string json)
    {
        Assert.Equal(ClickTimings.DefaultHitFixMinDownMs, RemoteConfig.Parse(json).HitFixMinDownMs);
    }

    [Fact]
    public void ANumberInRange_IsTaken()
    {
        Assert.Equal(12.0, RemoteConfig.Parse("{\"hitFixMinDownMs\": 12}").HitFixMinDownMs);
    }

    [Fact]
    public void ANumberOutOfRange_IsClampedNotRejected()
    {
        Assert.Equal(100.0, RemoteConfig.Parse("{\"hitFixMinDownMs\": 9999}").HitFixMinDownMs);
        Assert.Equal(1.0, RemoteConfig.Parse("{\"hitFixMinDownMs\": -5}").HitFixMinDownMs);
    }

    [Fact]
    public void NaNAndInfinity_FallBackRatherThanPoisonTheTiming()
    {
        // JSON has no NaN literal, so these arrive as strings or as overflow.
        Assert.Equal(
            ClickTimings.DefaultHitFixMinDownMs,
            RemoteConfig.Parse("{\"hitFixMinDownMs\": \"NaN\"}").HitFixMinDownMs);
    }

    [Fact]
    public void AWrongType_KeepsTheDefault()
    {
        Assert.Equal(
            ClickTimings.DefaultHitFixMinDownMs,
            RemoteConfig.Parse("{\"hitFixMinDownMs\": \"twelve\"}").HitFixMinDownMs);
        Assert.True(RemoteConfig.Parse("{\"recorderEnabled\": \"no\"}").RecorderEnabled);
        Assert.True(RemoteConfig.Parse("{\"kitArtFetchEnabled\": \"no\"}").KitArtFetchEnabled);
    }

    [Fact]
    public void AKeyNobodyRecognises_ChangesNothing()
    {
        RemoteConfig c = RemoteConfig.Parse("{\"somethingElse\": 5, \"hitFixMinUpMs\": 20}");

        Assert.Equal(20.0, c.HitFixMinUpMs);
        Assert.Equal(ClickTimings.DefaultHitFixMinDownMs, c.HitFixMinDownMs);
    }

    [Fact]
    public void RecorderCanBeSwitchedOff()
    {
        Assert.False(RemoteConfig.Parse("{\"recorderEnabled\": false}").RecorderEnabled);
    }

    [Fact]
    public void KitArtFetchCanBeSwitchedOff()
    {
        Assert.False(RemoteConfig.Parse("{\"kitArtFetchEnabled\": false}").KitArtFetchEnabled);
    }

    [Fact]
    public void ALongNotice_IsTruncatedRatherThanShownWhole()
    {
        string json = "{\"notice\": \"" + new string('x', 500) + "\"}";

        Assert.Equal(RemoteConfig.MaxNoticeLength, RemoteConfig.Parse(json).Notice.Length);
    }

    [Fact]
    public void ANoticeIsTrimmed()
    {
        Assert.Equal("hello", RemoteConfig.Parse("{\"notice\": \"  hello  \"}").Notice);
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/a/b/main/config.json", true)]
    [InlineData("http://raw.githubusercontent.com/a/b/main/config.json", false)]
    [InlineData("https://example.com/config.json", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void OnlyRawGithubOverHttpsIsTrusted(string? url, bool expected)
    {
        Assert.Equal(expected, RemoteConfig.IsTrusted(url));
    }

    [Fact]
    public void TheShippedUrlIsOneThisAppTrusts()
    {
        Assert.True(RemoteConfig.IsTrusted(RemoteConfig.Url));
        Assert.Contains("JinxyMac-Beta", RemoteConfig.Url);
    }
}
