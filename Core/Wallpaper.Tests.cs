using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The picture behind the window, and the rules that keep it findable. The
/// stored name is the load-bearing part: a path would stop meaning anything
/// the moment the source file moved.
/// </summary>
public class WallpaperTests
{
    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.PNG", true)]
    [InlineData("a.jpg", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.bmp", true)]
    [InlineData("a.gif", false)]
    [InlineData("a.txt", false)]
    [InlineData("a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyDecodableFormatsAreAccepted(string? path, bool expected)
    {
        Assert.Equal(expected, Wallpaper.IsSupported(path));
    }

    [Fact]
    public void TheStoredNameIsOneStemPlusTheSourceExtension()
    {
        Assert.Equal("wallpaper.png", Wallpaper.StoredNameFor(@"C:\pics\Holiday.PNG"));
        Assert.Equal("wallpaper.jpg", Wallpaper.StoredNameFor("/home/me/a.jpg"));
    }

    [Fact]
    public void AMissingSourceStoresNothing()
    {
        Assert.Null(Wallpaper.Store(Path.Combine(Path.GetTempPath(), "no-such-file.png")));
    }

    [Fact]
    public void AnUnsupportedSourceStoresNothing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wp-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "not an image");

        try
        {
            Assert.Null(Wallpaper.Store(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The spec's error handling promises that a failed copy leaves the
    /// previous background in place. Clearing the old file before the new one
    /// is safely on disk would break that the moment a copy failed partway —
    /// e.g. a network share dropping between the existence check and the copy.
    /// </summary>
    [Fact]
    public void AFailedCopyLeavesTheExistingWallpaperInPlace()
    {
        string oldSource = Path.Combine(Path.GetTempPath(), $"wp-test-old-{Guid.NewGuid():N}.png");
        string newSource = Path.Combine(Path.GetTempPath(), $"wp-test-new-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(oldSource, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(newSource, new byte[] { 4, 5, 6 });

        string target = SettingsPath.For(Wallpaper.StoredNameFor(newSource));
        string temp = target + ".tmp";

        try
        {
            // Store the first picture for real, so there is something in
            // place worth protecting.
            Assert.Equal("wallpaper.png", Wallpaper.Store(oldSource));
            Assert.True(File.Exists(target));

            // Block the copy by putting a directory where the temp file needs
            // to land, so File.Copy throws partway through Store.
            Directory.CreateDirectory(temp);

            Assert.Null(Wallpaper.Store(newSource));

            // The old wallpaper is exactly as it was — never cleared.
            Assert.True(File.Exists(target));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            Wallpaper.Clear();
            File.Delete(oldSource);
            File.Delete(newSource);
        }
    }

    [Fact]
    public void NothingStoredResolvesToNothing()
    {
        Assert.Null(Wallpaper.Resolve(null));
        Assert.Null(Wallpaper.Resolve(""));
        Assert.Null(Wallpaper.Resolve("   "));
    }

    /// <summary>
    /// A bare name is what gets stored. Anything carrying a directory came from
    /// a hand-edited settings file and is not followed. The backslash cases
    /// matter as much as the forward-slash ones: this app ships to macOS,
    /// where a backslash is just an ordinary filename character, so a guard
    /// that only recognizes it as a separator on Windows would refuse these
    /// on the platform it was written on and wave them through on the one it
    /// runs on. Path.GetFileName alone does not refuse them on both — it has
    /// to be checked by hand.
    /// </summary>
    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData(@"C:\Windows\System32\config")]
    [InlineData("sub/wallpaper.png")]
    [InlineData(@"sub\wallpaper.png")]
    [InlineData(@"\\server\share\evil.png")]
    public void APathIsNotFollowed(string stored)
    {
        Assert.Null(Wallpaper.Resolve(stored));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(90, 90)]
    [InlineData(100, 90)]
    public void DimmingIsClampedShortOfPaintingItOut(int given, int expected)
    {
        Assert.Equal(expected, Wallpaper.ClampDimming(given));
    }

    [Fact]
    public void DimmingBecomesAnOpacityFraction()
    {
        Assert.Equal(0.45, Wallpaper.DimmingOpacity(45), 3);
        Assert.Equal(0.90, Wallpaper.DimmingOpacity(100), 3);
    }
}
