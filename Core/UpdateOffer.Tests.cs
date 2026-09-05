using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The wording decisions behind the launch-time update prompt — the only
/// part of it that is not an Avalonia window and so the only part a test
/// here can reach.
/// </summary>
public class UpdateOfferTests
{
    [Fact]
    public void HeadlineNamesVersionAndSize()
    {
        var update = new Available("1.4.0", "", "https://example.com/x.tar.gz", 42L * 1024 * 1024);

        Assert.Equal("1.4.0 is available  ·  42 MB", UpdateOffer.Headline(update));
    }

    [Fact]
    public void WarningSaysAccessibilityMustBeRegranted()
    {
        Assert.Contains("Accessibility", UpdateOffer.AccessibilityWarning);
        Assert.Contains("unsigned", UpdateOffer.AccessibilityWarning);
    }

    [Fact]
    public void ShortNotesPassThroughUnchanged()
    {
        Assert.Equal("Fixed a bug.", UpdateOffer.Notes("Fixed a bug.", maxLength: 500));
    }

    [Fact]
    public void NotesAreTrimmedBeforeLengthIsJudged()
    {
        Assert.Equal("Fixed a bug.", UpdateOffer.Notes("  Fixed a bug.  \n", maxLength: 500));
    }

    [Fact]
    public void LongNotesAreCutAtALineBreakWithAnEllipsis()
    {
        string notes = "First line of the changelog.\n" + new string('x', 600);

        string result = UpdateOffer.Notes(notes, maxLength: 100);

        Assert.Equal("First line of the changelog.…", result);
    }

    /// <summary>
    /// The fallback for a single line with no break in range: it must still
    /// cut at the limit rather than pass the whole thing through, or a
    /// pathological release body could blow up the window it is offered in.
    /// </summary>
    [Fact]
    public void ALineWithNoBreakIsHardCutAtTheLimit()
    {
        string notes = new string('x', 600);

        string result = UpdateOffer.Notes(notes, maxLength: 100);

        Assert.Equal(101, result.Length); // 100 chars + the ellipsis
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void ExactlyTheLimitIsNotTruncated()
    {
        string notes = new string('x', 100);

        Assert.Equal(notes, UpdateOffer.Notes(notes, maxLength: 100));
    }
}
