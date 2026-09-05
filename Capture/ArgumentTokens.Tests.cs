using Xunit;

namespace JinxyMac.Capture.Tests;

/// <summary>
/// Splitting CaptureBackend's ready-made flag strings into ArgumentList
/// tokens, without corrupting the one value they quote.
/// </summary>
public class ArgumentTokensTests
{
    [Fact]
    public void SplitsOnWhitespace()
    {
        Assert.Equal(new[] { "-y", "-c:v", "libx264" }, ArgumentTokens.Split("-y -c:v libx264"));
    }

    [Fact]
    public void CollapsesRepeatedWhitespace()
    {
        Assert.Equal(new[] { "-y", "-f", "avfoundation" }, ArgumentTokens.Split("-y   -f  avfoundation"));
    }

    [Fact]
    public void IgnoresLeadingAndTrailingWhitespace()
    {
        Assert.Equal(new[] { "-y" }, ArgumentTokens.Split("  -y  "));
    }

    /// <summary>
    /// The one thing this exists to get right. MacInputArgs quotes the device
    /// spec because it is destined for a single command-line string; splitting
    /// naively on whitespace would leave the quote characters embedded in the
    /// token, and ffmpeg would then look for a device literally named
    /// <c>"1:none"</c>, quotes included, rather than <c>1:none</c>.
    /// </summary>
    [Fact]
    public void TreatsAQuotedSpanAsOneTokenAndDropsTheQuotes()
    {
        Assert.Equal(new[] { "-i", "1:none" }, ArgumentTokens.Split("-i \"1:none\""));
    }

    [Fact]
    public void AQuotedTokenCanContainWhitespace()
    {
        Assert.Equal(new[] { "-i", "1:none extra" }, ArgumentTokens.Split("-i \"1:none extra\""));
    }

    [Fact]
    public void EmptyStringYieldsNoTokens()
    {
        Assert.Empty(ArgumentTokens.Split(""));
    }

    [Fact]
    public void WhitespaceOnlyYieldsNoTokens()
    {
        Assert.Empty(ArgumentTokens.Split("   "));
    }

    /// <summary>
    /// The exact strings CaptureBackend actually produces, round-tripped, so a
    /// change to either side is caught by the other.
    /// </summary>
    [Fact]
    public void SplitsARealMacInputArgsString()
    {
        string args = CaptureBackend.MacInputArgs(new CaptureDevice(2, "Capture screen 1"), 60, withFramerate: true);

        Assert.Equal(
            new[]
            {
                "-f", "avfoundation", "-capture_cursor", "1", "-capture_mouse_clicks", "1",
                "-use_wallclock_as_timestamps", "1", "-framerate", "60", "-i", "2:none"
            },
            ArgumentTokens.Split(args));
    }
}
