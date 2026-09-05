using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Asking the wiki for kit pictures, and reading what it sends back.
/// </summary>
/// <remarks>
/// The download itself needs a network. What is tested is everything deciding
/// <em>which</em> picture a kit gets and <em>where</em> it may come from — a
/// picture matched to the wrong kit is worse than none, and an address the app
/// writes to disk without checking is worse than that.
/// </remarks>
public class KitArtTests
{
    /// <summary>
    /// The suffixed name is asked for first. Several kits share a name with an
    /// item, and the plain file is the item — Ember's is a lump of rock.
    /// </summary>
    [Fact]
    public void AsksForTheKitVariantBeforeThePlainName()
    {
        List<string> titles = KitArt.TitlesFor("Ember").ToList();

        Assert.Equal("File:Ember_(kit).png", titles[0]);
        Assert.Equal("File:Ember.png", titles[1]);
    }

    [Theory]
    [InlineData("File:Ember (kit).png", "Ember")]
    [InlineData("File:Ember_(kit).png", "Ember")]
    [InlineData("File:Melody.png", "Melody")]
    [InlineData("File:Axolotl_Amy.png", "Axolotl Amy")]
    public void ReadsTheKitBackOutOfATitle(string title, string expected)
    {
        Assert.Equal(expected, KitArt.KitFromTitle(title));
    }

    // ---- where a picture may come from ----

    /// <summary>
    /// The address arrives in a JSON reply and the app writes what is behind it
    /// to disk, so the host is pinned rather than trusted.
    /// </summary>
    [Theory]
    [InlineData("https://static.wikia.nocookie.net/robloxbedwars/images/9/98/Melody.png")]
    [InlineData("https://robloxbedwars.fandom.com/images/Melody.png")]
    public void AcceptsTheWikisOwnHosts(string url)
    {
        Assert.True(KitArt.IsWikiImage(url));
    }

    [Theory]
    [InlineData("http://static.wikia.nocookie.net/x.png")]        // not https
    [InlineData("https://fandom.com.evil.example/x.png")]         // lookalike
    [InlineData("https://evil.example/x.png")]
    [InlineData("file:///C:/Windows/System32/drivers/etc/hosts")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void RefusesAnywhereElse(string? url)
    {
        Assert.False(KitArt.IsWikiImage(url));
    }

    // ---- reading the reply ----

    private const string Reply = """
    {
      "query": {
        "pages": {
          "-1": { "title": "File:Nosuchkit.png" },
          "12": {
            "title": "File:Melody.png",
            "imageinfo": [ { "url": "https://static.wikia.nocookie.net/x/Melody.png" } ]
          },
          "34": {
            "title": "File:Ember (kit).png",
            "imageinfo": [ { "url": "https://static.wikia.nocookie.net/x/Ember_kit.png" } ]
          },
          "35": {
            "title": "File:Ember.png",
            "imageinfo": [ { "url": "https://static.wikia.nocookie.net/x/Ember_item.png" } ]
          }
        }
      }
    }
    """;

    [Fact]
    public void FindsThePictureForEachKit()
    {
        Dictionary<string, string> urls = KitArt.ReadUrls(Reply);

        Assert.Equal("https://static.wikia.nocookie.net/x/Melody.png", urls["Melody"]);
    }

    /// <summary>
    /// When both come back, the kit render wins over the item of the same name.
    /// This is the whole reason the suffixed title is asked for.
    /// </summary>
    [Fact]
    public void PrefersTheKitRenderOverTheItem()
    {
        Assert.Equal("https://static.wikia.nocookie.net/x/Ember_kit.png",
            KitArt.ReadUrls(Reply)["Ember"]);
    }

    /// <summary>
    /// A file the wiki does not have comes back as a page with no imageinfo
    /// rather than as an error, and must not look like a picture.
    /// </summary>
    [Fact]
    public void SkipsAKitTheWikiDoesNotHave()
    {
        Assert.False(KitArt.ReadUrls(Reply).ContainsKey("Nosuchkit"));
    }

    /// <summary>This runs against whatever the network returns. Nothing may throw.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"query\": {}}")]
    [InlineData("{\"error\": {\"code\": \"toomanyvalues\"}}")]
    public void SurvivesAnythingTheWikiReturns(string json)
    {
        Assert.Empty(KitArt.ReadUrls(json));
    }

    /// <summary>An address on a host the app does not trust is dropped here too.</summary>
    [Fact]
    public void IgnoresAPictureHostedSomewhereElse()
    {
        const string sneaky = """
        {"query":{"pages":{"1":{"title":"File:Melody.png",
         "imageinfo":[{"url":"https://evil.example/Melody.png"}]}}}}
        """;

        Assert.Empty(KitArt.ReadUrls(sneaky));
    }

    // ---- batching ----

    [Fact]
    public void SplitsTheRosterIntoRequestsTheApiAccepts()
    {
        var kits = Enumerable.Range(0, 113).Select(i => $"Kit {i}").ToList();

        List<List<string>> batches = KitArt.Batches(kits);

        Assert.Equal(113, batches.Sum(b => b.Count));
        Assert.All(batches, b => Assert.True(b.Count <= KitArt.BatchSize));
    }

    /// <summary>
    /// Each kit costs two titles and the wiki takes fifty a call, so a batch
    /// has to stay under half that.
    /// </summary>
    [Fact]
    public void ABatchFitsInsideTheApisLimit()
    {
        Assert.True(KitArt.BatchSize * 2 <= 50);
    }

    [Fact]
    public void HasNothingToDoForAnEmptyRoster()
    {
        Assert.Empty(KitArt.Batches(new List<string>()));
    }

    [Fact]
    public void OnlyEverAsksTheBedwarsWiki()
    {
        Assert.StartsWith("https://robloxbedwars.fandom.com/api.php",
            KitArt.QueryUrl(new[] { "File:Melody.png" }));
    }

    /// <summary>
    /// The game labels a few kits with a bracketed qualifier the wiki does not
    /// use. "Zeno (Wizard)" is filed as plain "Zeno", and without this it is the
    /// one kit on the roster that never gets a picture.
    /// </summary>
    [Fact]
    public void FallsBackToTheNameWithoutItsQualifier()
    {
        List<string> titles = KitArt.TitlesFor("Zeno_(Wizard)").ToList();

        Assert.Contains("File:Zeno.png", titles);
    }

    [Theory]
    [InlineData("Zeno (Wizard)", "Zeno")]
    [InlineData("Zeno_(Wizard)", "Zeno")]
    [InlineData("Melody", "Melody")]
    [InlineData("Axolotl Amy", "Axolotl Amy")]
    public void StripsOnlyATrailingQualifier(string kit, string expected)
    {
        Assert.Equal(expected, KitArt.WithoutQualifier(kit).Replace('_', ' '));
    }

    /// <summary>A name that is only brackets must not be trimmed to nothing.</summary>
    [Fact]
    public void LeavesANameThatIsAllQualifierAlone()
    {
        Assert.Equal("(Wizard)", KitArt.WithoutQualifier("(Wizard)"));
    }

    // ---- naming a download after what it actually is ----
    //
    // Every kit picture was written as ".png" whatever came back. This wiki
    // serves WebP, so the whole folder was mislabelled, and the mislabelling
    // led to a decode path that dropped the transparency and drew each kit as a
    // solid coloured rectangle.

    private static byte[] Magic(params int[] head)
    {
        var bytes = new byte[16];
        for (int i = 0; i < head.Length; i++) bytes[i] = (byte)head[i];
        return bytes;
    }

    /// <summary>What this wiki actually sends, and what started all of it.</summary>
    [Fact]
    public void RecognisesTheWebpTheWikiActuallySends()
    {
        byte[] webp = Magic('R', 'I', 'F', 'F', 0, 0, 0, 0, 'W', 'E', 'B', 'P');

        Assert.Equal(".webp", KitArt.ExtensionFor(webp));
    }

    [Fact]
    public void RecognisesARealPng()
    {
        Assert.Equal(".png", KitArt.ExtensionFor(Magic(0x89, 'P', 'N', 'G', 0x0D, 0x0A, 0x1A, 0x0A)));
    }

    [Theory]
    [InlineData(0xFF, 0xD8, 0xFF, ".jpg")]
    [InlineData('B', 'M', 0, ".bmp")]
    public void RecognisesTheOtherFormatsItCanShow(int a, int b, int c, string expected)
    {
        Assert.Equal(expected, KitArt.ExtensionFor(Magic(a, b, c)));
    }

    /// <summary>
    /// A RIFF container that is not WebP is not a picture. Naming it ".webp"
    /// would leave a file that exists, never decodes, and stops the app ever
    /// trying that kit again.
    /// </summary>
    [Fact]
    public void RefusesARiffThatIsNotWebp()
    {
        byte[] wav = Magic('R', 'I', 'F', 'F', 0, 0, 0, 0, 'W', 'A', 'V', 'E');

        Assert.Equal("", KitArt.ExtensionFor(wav));
    }

    /// <summary>
    /// Anything unrecognised is not written at all. An error page saved under a
    /// picture's name is the failure that never heals by itself.
    /// </summary>
    [Theory]
    [InlineData('<', 'h', 't', 'm', 'l')]
    [InlineData('G', 'I', 'F', '8', '9')]
    public void RefusesAnythingItCannotShow(int a, int b, int c, int d, int e)
    {
        Assert.Equal("", KitArt.ExtensionFor(Magic(a, b, c, d, e)));
    }

    [Fact]
    public void RefusesAReplyTooShortToIdentify()
    {
        Assert.Equal("", KitArt.ExtensionFor(new byte[] { 0x89, (byte)'P' }));
        Assert.Equal("", KitArt.ExtensionFor(System.Array.Empty<byte>()));
    }
}
