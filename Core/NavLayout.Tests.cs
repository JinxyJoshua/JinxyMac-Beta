using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The sidebar as the user has arranged it.
/// </summary>
/// <remarks>
/// The cases that matter are the ones nobody can try by hand: an order saved by
/// a build that had different pages, and a settings file somebody edited into a
/// state the editor would not allow.
/// </remarks>
public class NavLayoutTests
{
    private static readonly List<NavEntry> Pages = new()
    {
        new NavEntry("NavClicker", "Clicker"),
        new NavEntry("NavPresets", "Presets"),
        new NavEntry("NavTheme", "Theme"),
        new NavEntry("NavSettings", "Settings")
    };

    private static List<string> Ids() => Pages.Select(p => p.Id).ToList();

    // ---- the order ----

    [Fact]
    public void WithNothingSavedTheAppsOwnOrderIsUsed()
    {
        Assert.Equal(Ids(), NavLayout.Reconcile(null, Pages));
        Assert.Equal(Ids(), NavLayout.Reconcile(new List<string>(), Pages));
    }

    [Fact]
    public void ASavedOrderIsKept()
    {
        var saved = new List<string> { "NavSettings", "NavTheme", "NavPresets", "NavClicker" };

        Assert.Equal(saved, NavLayout.Reconcile(saved, Pages));
    }

    /// <summary>
    /// The case an update creates: a page that did not exist when the order was
    /// saved. It has to appear, or the update would silently deliver a feature
    /// nobody can reach.
    /// </summary>
    [Fact]
    public void APageTheSavedOrderNeverHeardOfStillAppears()
    {
        var saved = new List<string> { "NavSettings", "NavClicker" };

        List<string> order = NavLayout.Reconcile(saved, Pages);

        Assert.Equal(new[] { "NavSettings", "NavClicker", "NavPresets", "NavTheme" }, order);
        Assert.Equal(Pages.Count, order.Count);
    }

    /// <summary>
    /// And the other side of an update: a page the saved order mentions that has
    /// since been removed must not come back as a row that goes nowhere.
    /// </summary>
    [Fact]
    public void APageThatNoLongerExistsIsDropped()
    {
        var saved = new List<string> { "NavClicker", "NavDailyRewards", "NavTheme" };

        List<string> order = NavLayout.Reconcile(saved, Pages);

        Assert.DoesNotContain("NavDailyRewards", order);
        Assert.Equal(Pages.Count, order.Count);
    }

    /// <summary>A hand-edited file can repeat an id; the sidebar cannot.</summary>
    [Fact]
    public void ARepeatedIdAppearsOnlyOnce()
    {
        var saved = new List<string> { "NavTheme", "NavTheme", "NavClicker", "NavTheme" };

        List<string> order = NavLayout.Reconcile(saved, Pages);

        Assert.Equal(1, order.Count(id => id == "NavTheme"));
        Assert.Equal(Pages.Count, order.Count);
    }

    [Fact]
    public void EveryPageAppearsExactlyOnceWhateverWasSaved()
    {
        foreach (List<string>? saved in new List<string>?[]
        {
            null,
            new() { "NavSettings" },
            new() { "nonsense", "NavTheme", "nonsense" },
            new() { "NavTheme", "NavTheme" },
            Ids()
        })
        {
            List<string> order = NavLayout.Reconcile(saved, Pages);

            Assert.Equal(Pages.Count, order.Count);
            Assert.Equal(Pages.Count, order.Distinct().Count());
            Assert.All(Ids(), id => Assert.Contains(id, order));
        }
    }

    // ---- hiding ----

    [Fact]
    public void AHiddenPageIsNotShown()
    {
        List<string> shown = NavLayout.Visible(Ids(), new[] { "NavPresets" });

        Assert.DoesNotContain("NavPresets", shown);
        Assert.Equal(3, shown.Count);
    }

    [Fact]
    public void HidingNothingShowsEverything()
    {
        Assert.Equal(Ids(), NavLayout.Visible(Ids(), null));
        Assert.Equal(Ids(), NavLayout.Visible(Ids(), new List<string>()));
    }

    /// <summary>
    /// A hand-edited file that hides every page would otherwise leave an app
    /// with no navigation at all — and no way to undo it from inside, since the
    /// editor is itself on a page.
    /// </summary>
    [Fact]
    public void HidingEveryPageShowsThemAllInstead()
    {
        Assert.Equal(Ids(), NavLayout.Visible(Ids(), Ids()));
    }

    [Fact]
    public void AHiddenIdThatIsNotAPageChangesNothing()
    {
        Assert.Equal(Ids(), NavLayout.Visible(Ids(), new[] { "NavLongGone" }));
    }

    /// <summary>What the editor uses to refuse to hide the last one.</summary>
    [Fact]
    public void TheLastShownPageIsReportedAsSuch()
    {
        Assert.False(NavLayout.IsLastShown(Ids(), null, "NavClicker"));

        var allButOne = new[] { "NavPresets", "NavTheme", "NavSettings" };

        Assert.True(NavLayout.IsLastShown(Ids(), allButOne, "NavClicker"));
    }

    /// <summary>
    /// An already-hidden page is not the last one showing, so unhiding it is
    /// never blocked.
    /// </summary>
    [Fact]
    public void AnAlreadyHiddenPageIsNotTheLastShown()
    {
        var allButOne = new[] { "NavPresets", "NavTheme", "NavSettings" };

        Assert.False(NavLayout.IsLastShown(Ids(), allButOne, "NavPresets"));
    }

    // ---- renaming ----

    [Fact]
    public void WithNoRenameThePageKeepsItsOwnName()
    {
        Assert.Equal("Clicker", NavLayout.Label("NavClicker", "Clicker", null));

        Assert.Equal("Clicker",
            NavLayout.Label("NavClicker", "Clicker", new Dictionary<string, string>()));
    }

    [Fact]
    public void ARenameIsUsed()
    {
        var labels = new Dictionary<string, string> { ["NavClicker"] = "Autoclicker" };

        Assert.Equal("Autoclicker", NavLayout.Label("NavClicker", "Clicker", labels));
    }

    /// <summary>A nameless row in the sidebar is worse than the default name.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void ABlankRenameFallsBackToTheDefault(string stored)
    {
        var labels = new Dictionary<string, string> { ["NavClicker"] = stored };

        Assert.Equal("Clicker", NavLayout.Label("NavClicker", "Clicker", labels));
    }

    [Fact]
    public void ATypedNameIsTrimmed()
    {
        Assert.Equal("My clicker", NavLayout.CleanLabel("  My clicker  ", "Clicker"));
    }

    /// <summary>
    /// A name pasted out of a document arrives with newlines and runs of
    /// spaces, which the sidebar would render as a gap rather than a word.
    /// </summary>
    [Fact]
    public void APastedNameIsCollapsedToSingleSpaces()
    {
        Assert.Equal("My clicker", NavLayout.CleanLabel("My\n\n  clicker", "Clicker"));
    }

    /// <summary>
    /// The sidebar is a fixed width and does not wrap, so a long name is cut
    /// here — where the editor can show the real result as it is typed — rather
    /// than mid-word on screen.
    /// </summary>
    [Fact]
    public void AnOverlongNameIsCutToTheLimit()
    {
        string? cleaned = NavLayout.CleanLabel(new string('x', 80), "Clicker");

        Assert.NotNull(cleaned);
        Assert.Equal(NavLayout.MaxLabelLength, cleaned!.Length);
    }

    [Fact]
    public void ACutNameDoesNotEndInASpace()
    {
        string? cleaned = NavLayout.CleanLabel(
            new string('x', NavLayout.MaxLabelLength - 1) + " tail", "Clicker");

        Assert.NotNull(cleaned);
        Assert.Equal(cleaned, cleaned!.TrimEnd());
    }

    /// <summary>Clearing the box removes the rename rather than storing a blank.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData(null)]
    public void ClearingTheNameStoresNothing(string? typed)
    {
        Assert.Null(NavLayout.CleanLabel(typed, "Clicker"));
    }

    /// <summary>
    /// And typing the original name back removes it too, rather than storing a
    /// rename that renames nothing.
    /// </summary>
    [Fact]
    public void TypingTheDefaultNameBackStoresNothing()
    {
        Assert.Null(NavLayout.CleanLabel("Clicker", "Clicker"));
        Assert.Null(NavLayout.CleanLabel("  Clicker  ", "Clicker"));
    }

    // ---- moving ----

    [Fact]
    public void APageMovesUp()
    {
        List<string> order = Ids();

        Assert.True(NavLayout.Move(order, "NavTheme", -1));
        Assert.Equal(new[] { "NavClicker", "NavTheme", "NavPresets", "NavSettings" }, order);
    }

    [Fact]
    public void APageMovesDown()
    {
        List<string> order = Ids();

        Assert.True(NavLayout.Move(order, "NavClicker", 1));
        Assert.Equal(new[] { "NavPresets", "NavClicker", "NavTheme", "NavSettings" }, order);
    }

    /// <summary>
    /// The ends do nothing rather than wrapping around, and report that nothing
    /// happened so the caller can skip writing an identical file.
    /// </summary>
    [Fact]
    public void MovingPastTheEndDoesNothing()
    {
        List<string> order = Ids();

        Assert.False(NavLayout.Move(order, "NavClicker", -1));
        Assert.False(NavLayout.Move(order, "NavSettings", 1));
        Assert.Equal(Ids(), order);
    }

    [Fact]
    public void MovingAPageThatIsNotThereDoesNothing()
    {
        List<string> order = Ids();

        Assert.False(NavLayout.Move(order, "NavLongGone", 1));
        Assert.Equal(Ids(), order);
    }

    [Fact]
    public void MovingByNothingDoesNothing()
    {
        List<string> order = Ids();

        Assert.False(NavLayout.Move(order, "NavTheme", 0));
        Assert.Equal(Ids(), order);
    }

    /// <summary>A move never loses or duplicates a page.</summary>
    [Fact]
    public void MovingKeepsEveryPageExactlyOnce()
    {
        List<string> order = Ids();

        NavLayout.Move(order, "NavSettings", -2);
        NavLayout.Move(order, "NavClicker", 3);
        NavLayout.Move(order, "NavTheme", -5);

        Assert.Equal(Pages.Count, order.Count);
        Assert.Equal(Pages.Count, order.Distinct().Count());
    }

    /// <summary>A clamped move still goes as far as it can.</summary>
    [Fact]
    public void AMoveBeyondTheEndGoesAsFarAsItCan()
    {
        List<string> order = Ids();

        Assert.True(NavLayout.Move(order, "NavClicker", 99));
        Assert.Equal("NavClicker", order[^1]);
    }
}
