using System.Globalization;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The two palettes, and whether the light one is actually readable.
/// </summary>
/// <remarks>
/// A light mode is easy to write and easy to get subtly wrong — muted text on a
/// white panel is the classic version, and it looks fine to whoever picked the
/// colours on the monitor they picked them on. Contrast is arithmetic, so it is
/// checked rather than eyeballed.
/// </remarks>
public class PaletteTests
{
    [Fact]
    public void BothPalettesDefineEveryColour()
    {
        Assert.Equal(16, Palette.Dark.Entries().Count());
        Assert.Equal(16, Palette.Light.Entries().Count());
    }

    [Fact]
    public void TheTwoPalettesCoverTheSameKeys()
    {
        Assert.Equal(
            Palette.Dark.Entries().Select(e => e.Key),
            Palette.Light.Entries().Select(e => e.Key));
    }

    [Fact]
    public void EveryColourParses()
    {
        foreach ((string key, string hex) in Palette.Dark.Entries().Concat(Palette.Light.Entries()))
        {
            Assert.True(TryParse(hex, out _), $"{key} = {hex}");
        }
    }

    [Fact]
    public void ForPicksThePalette()
    {
        Assert.Same(Palette.Dark, Palette.For(true));
        Assert.Same(Palette.Light, Palette.For(false));
    }

    /// <summary>
    /// Body text has to clear WCAG AA on the surface it sits on. 4.5:1 is the
    /// threshold; below it, text on a panel is a strain rather than a style.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BodyTextIsReadableOnEverySurface(bool dark)
    {
        Palette palette = Palette.For(dark);

        foreach (string surface in new[] { palette.Backdrop, palette.Panel, palette.Panel2, palette.Sunken })
        {
            Assert.True(Contrast(palette.Text, surface) >= 4.5,
                $"Text on {surface} is {Contrast(palette.Text, surface):0.00}:1");
        }
    }

    /// <summary>
    /// Muted text is the hint line under a label. It is allowed to be quieter
    /// than body text, but 3:1 is the floor at which it stops being text.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MutedTextStaysAboveTheLargeTextFloor(bool dark)
    {
        Palette palette = Palette.For(dark);

        Assert.True(Contrast(palette.TextMuted, palette.Panel) >= 3.0,
            $"Muted on panel is {Contrast(palette.TextMuted, palette.Panel):0.00}:1");
    }

    /// <summary>
    /// The raised surfaces have to be distinguishable from the page behind them
    /// or the cards stop reading as cards.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PanelsAreDistinctFromTheBackdrop(bool dark)
    {
        Palette palette = Palette.For(dark);

        Assert.NotEqual(palette.Panel, palette.Backdrop);
        Assert.NotEqual(palette.Sunken, palette.Panel);
    }

    // ---- accents ----

    [Fact]
    public void TwelveAccentsWithDistinctColours()
    {
        Assert.Equal(12, Accents.All.Count);
        Assert.Equal(12, Accents.All.Select(a => a.Hex).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryAccentParsesAndIsNamed()
    {
        foreach ((string name, string hex) in Accents.All)
        {
            Assert.True(TryParse(hex, out _), hex);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    [Fact]
    public void NamesTheColourWhenItIsOneOfOurs()
    {
        Assert.Equal("Sky", Accents.NameOf("#45B7FF"));
        Assert.Equal("Sky", Accents.NameOf("#45b7ff"));
    }

    /// <summary>A hand-typed colour has no name, so it shows as itself.</summary>
    [Fact]
    public void FallsBackToTheHexForACustomColour()
    {
        Assert.Equal("#123456", Accents.NameOf("#123456"));
    }

    // ---- WCAG relative luminance ----

    private static double Contrast(string a, string b)
    {
        double first = Luminance(a);
        double second = Luminance(b);

        (double lighter, double darker) = first > second ? (first, second) : (second, first);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        TryParse(hex, out (int R, int G, int B) rgb);

        return 0.2126 * Channel(rgb.R) + 0.7152 * Channel(rgb.G) + 0.0722 * Channel(rgb.B);
    }

    private static double Channel(int value)
    {
        double srgb = value / 255.0;

        return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Reads #RRGGBB and #AARRGGBB alike.
    /// </summary>
    /// <remarks>
    /// The hairline and hover colours carry alpha, and their RGB is what matters
    /// for the parse check — nothing here composites them, so the alpha is read
    /// off the front and dropped.
    /// </remarks>
    private static bool TryParse(string hex, out (int R, int G, int B) rgb)
    {
        rgb = default;

        string digits = hex.TrimStart('#');

        if (digits.Length == 8) digits = digits[2..];
        if (digits.Length != 6) return false;

        if (!int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int packed))
            return false;

        rgb = ((packed >> 16) & 0xFF, (packed >> 8) & 0xFF, packed & 0xFF);

        return true;
    }
}
