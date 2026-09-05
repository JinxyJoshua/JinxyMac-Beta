using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Turning a typed kit name into a file name.
/// </summary>
/// <remarks>
/// Kit names are typed, so they can contain anything. This is what stands
/// between a name and a file write, and a name carrying a path separator would
/// otherwise send that write somewhere nobody chose.
/// </remarks>
public class KitImagesTests
{
    [Theory]
    [InlineData("Melody", "Melody.png")]
    [InlineData("Axolotl Amy", "Axolotl Amy.png")]
    [InlineData("Dino-Tamer", "Dino-Tamer.png")]
    public void KeepsAnOrdinaryNameAsItIs(string kit, string expected)
    {
        Assert.Equal(expected, KitImages.FileNameFor(kit, ".png"));
    }

    /// <summary>
    /// The one that matters. A separator or a drive letter must not survive
    /// into a path the app then writes to.
    /// </summary>
    [Theory]
    [InlineData(@"..\..\Windows\System32\evil")]
    [InlineData("kits/../../etc/passwd")]
    [InlineData(@"C:\Windows\notepad")]
    [InlineData("a:b*c?d")]
    public void StripsAnythingThatCouldPointElsewhere(string kit)
    {
        string name = KitImages.FileNameFor(kit, ".png");

        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain("..", name);
    }

    /// <summary>
    /// A name made entirely of stripped characters must still produce a usable
    /// file name rather than a bare extension.
    /// </summary>
    [Theory]
    [InlineData("///")]
    [InlineData("...")]
    [InlineData("   ")]
    public void StillProducesAFileNameWhenNothingSurvives(string kit)
    {
        string name = KitImages.FileNameFor(kit, ".png");

        Assert.NotEqual(".png", name);
        Assert.EndsWith(".png", name);
    }

    [Theory]
    [InlineData("art.png")]
    [InlineData("art.JPG")]
    [InlineData("art.webp")]
    public void AcceptsThePicturesSkiaCanDecode(string path)
    {
        Assert.True(KitImages.IsSupported(path));
    }

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("notes.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesWhatItCannotShow(string? path)
    {
        Assert.False(KitImages.IsSupported(path));
    }

    [Fact]
    public void HasNoPictureForAKitNobodyGaveOne()
    {
        Assert.Null(KitImages.Find("A Kit That Was Never Given Art 12345"));
    }

    [Fact]
    public void RefusesToStoreAFileThatIsNotThere()
    {
        Assert.Null(KitImages.Set("Melody", @"C:\nowhere\missing.png"));
    }

    [Fact]
    public void RefusesToStoreSomethingItCannotShow()
    {
        Assert.Null(KitImages.Set("Melody", @"C:\nowhere\clip.mp4"));
    }

    [Fact]
    public void OffersEveryAcceptedFormatInThePicker()
    {
        string filter = KitImages.FileFilter;

        Assert.Contains("*.png", filter);
        Assert.Contains("*.jpg", filter);
        Assert.Contains("*.webp", filter);
    }

    // ---- the two folders ----
    //
    // The install ships a picture for every kit, beside the executable. The
    // folder under AppData holds the ones a person chose. Which of the two wins
    // is the whole behaviour, so both directions are pinned here.
    //
    // Real kit names are deliberately not used: these write files, and a test
    // must not be able to disturb art the app is actually showing.

    private const string Invented = "Test Kit 8f2c1e";

    private static string ShippedPath(string kit)
    {
        string folder = System.IO.Path.Combine(System.AppContext.BaseDirectory, "kits");

        System.IO.Directory.CreateDirectory(folder);

        return System.IO.Path.Combine(folder, KitImages.FileNameFor(kit, ".png"));
    }

    [Fact]
    public void FindsThePictureThatCameWithTheApp()
    {
        string shipped = ShippedPath(Invented);

        try
        {
            System.IO.File.WriteAllText(shipped, "shipped");

            Assert.Equal(shipped, KitImages.Find(Invented));
        }
        finally
        {
            System.IO.File.Delete(shipped);
        }
    }

    /// <summary>
    /// A picture somebody chose beats the one the install shipped. Otherwise
    /// choosing your own art for a kit would appear to do nothing.
    /// </summary>
    [Fact]
    public void PrefersTheChosenPictureOverTheShippedOne()
    {
        string shipped = ShippedPath(Invented);
        string chosen = KitImages.PathFor(Invented, ".png");

        try
        {
            System.IO.File.WriteAllText(shipped, "shipped");
            System.IO.File.WriteAllText(chosen, "chosen");

            Assert.Equal(chosen, KitImages.Find(Invented));
        }
        finally
        {
            System.IO.File.Delete(shipped);
            System.IO.File.Delete(chosen);
        }
    }

    /// <summary>
    /// Clearing a chosen picture uncovers the shipped one rather than leaving
    /// the kit blank.
    /// </summary>
    [Fact]
    public void FallsBackToTheShippedPictureWhenTheChosenOneGoes()
    {
        string shipped = ShippedPath(Invented);
        string chosen = KitImages.PathFor(Invented, ".png");

        try
        {
            System.IO.File.WriteAllText(shipped, "shipped");
            System.IO.File.WriteAllText(chosen, "chosen");

            KitImages.Remove(Invented);

            Assert.Equal(shipped, KitImages.Find(Invented));
        }
        finally
        {
            System.IO.File.Delete(shipped);
            System.IO.File.Delete(chosen);
        }
    }

    /// <summary>
    /// Removing a kit's picture must not reach into the install folder. It is
    /// not this account's to change, and the deletion would be permanent.
    /// </summary>
    [Fact]
    public void NeverDeletesAShippedPicture()
    {
        string shipped = ShippedPath(Invented);

        try
        {
            System.IO.File.WriteAllText(shipped, "shipped");

            KitImages.Remove(Invented);

            Assert.True(System.IO.File.Exists(shipped));
        }
        finally
        {
            System.IO.File.Delete(shipped);
        }
    }
}
