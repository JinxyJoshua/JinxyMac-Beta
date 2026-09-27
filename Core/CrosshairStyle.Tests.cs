using System.Linq;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The crosshair's geometry, worked out without a screen.
/// </summary>
/// <remarks>
/// The renderer draws exactly what this returns, offset to the centre of the
/// image, so if the arms and the box are right here the crosshair is centred and
/// unclipped in the cursor picture, the tile and the preview.
/// </remarks>
public class CrosshairStyleTests
{
    // ---- the shapes draw the right parts ----

    [Fact]
    public void ACrossHasFourArmsAndNoDotOrRing()
    {
        var c = new CrosshairStyle(CrosshairShape.Cross);

        Assert.Equal(4, c.Arms().Count);
        Assert.False(c.HasDot);
        Assert.False(c.HasRing);
    }

    [Fact]
    public void TheTShapeDropsTheTopArm()
    {
        var t = new CrosshairStyle(CrosshairShape.TShape, Size: 10, Gap: 4);

        // Three arms, and none of them reaches above the centre.
        Assert.Equal(3, t.Arms().Count);
        Assert.DoesNotContain(t.Arms(), a => a.Y2 < 0);
    }

    [Fact]
    public void ADotIsJustADot()
    {
        var d = new CrosshairStyle(CrosshairShape.Dot, DotSize: 3);

        Assert.Empty(d.Arms());
        Assert.True(d.HasDot);
        Assert.Equal(3, d.DotRadius);
        Assert.False(d.HasRing);
    }

    [Fact]
    public void ACircleIsJustARing()
    {
        var r = new CrosshairStyle(CrosshairShape.Circle, Size: 9);

        Assert.Empty(r.Arms());
        Assert.True(r.HasRing);
        Assert.Equal(9, r.RingRadius);
        Assert.False(r.HasDot);
    }

    [Fact]
    public void CrossPlusDotHasBoth()
    {
        var c = new CrosshairStyle(CrosshairShape.CrossDot);

        Assert.Equal(4, c.Arms().Count);
        Assert.True(c.HasDot);
    }

    [Fact]
    public void AnXHasFourDiagonalArms()
    {
        var x = new CrosshairStyle(CrosshairShape.X, Size: 9, Gap: 3);

        Assert.Equal(4, x.Arms().Count);
        // Every arm moves in both axes at once — that is what makes it diagonal.
        Assert.All(x.Arms(), a => Assert.True(a.X1 != 0 && a.Y1 != 0 && a.X2 != 0 && a.Y2 != 0));
        Assert.False(x.HasDot);
    }

    [Fact]
    public void AnXDotAddsTheDot()
    {
        var x = new CrosshairStyle(CrosshairShape.XDot, Size: 9, Gap: 3, DotSize: 3);

        Assert.Equal(4, x.Arms().Count);
        Assert.True(x.HasDot);
    }

    [Fact]
    public void ACircleDotHasBothRingAndDot()
    {
        var c = new CrosshairStyle(CrosshairShape.CircleDot, Size: 9, DotSize: 2);

        Assert.True(c.HasRing);
        Assert.True(c.HasDot);
    }

    [Fact]
    public void TheDotTakesItsOwnColourWhenGiven()
    {
        var plain = new CrosshairStyle(CrosshairShape.CrossDot, ColorHex: "#FF0000");
        var twoTone = new CrosshairStyle(CrosshairShape.CrossDot, ColorHex: "#FF0000", DotColorHex: "#00FFFF");

        Assert.Equal("#FF0000", plain.EffectiveDotColor);
        Assert.Equal("#00FFFF", twoTone.EffectiveDotColor);
    }

    // ---- the gap and length place the arms ----

    [Fact]
    public void ArmsStartAtTheGapAndRunOutByTheSize()
    {
        var c = new CrosshairStyle(CrosshairShape.Cross, Size: 10, Gap: 4);

        // The right arm goes from x=4 (the gap) to x=14 (gap + size).
        CrosshairArm right = c.Arms().Single(a => a.X2 > 0 && a.Y2 == 0);
        Assert.Equal(4, right.X1);
        Assert.Equal(14, right.X2);
    }

    [Fact]
    public void ANoGapCrossMeetsInTheMiddle()
    {
        var c = new CrosshairStyle(CrosshairShape.Cross, Size: 8, Gap: 0);

        Assert.All(c.Arms(), a => Assert.True(a.X1 == 0 && a.Y1 == 0));
    }

    // ---- the box centres it and clips nothing ----

    [Fact]
    public void TheBoxIsBigEnoughForEveryArmPlusTheStroke()
    {
        var c = new CrosshairStyle(CrosshairShape.Cross, Size: 10, Gap: 4, Thickness: 2, Outline: true);

        int farthest = c.Arms().Max(a => System.Math.Max(System.Math.Abs(a.X2), System.Math.Abs(a.Y2)));

        // The centre plus the reach still leaves room for the stroke and outline.
        Assert.True(c.Centre >= farthest + 1);
        Assert.Equal(c.BoxSize, c.Centre * 2);
    }

    [Fact]
    public void TheBoxGrowsWithASniperArm()
    {
        var small = new CrosshairStyle(CrosshairShape.Cross, Size: 10);
        var sniper = new CrosshairStyle(CrosshairShape.Cross, Size: 30);

        Assert.True(sniper.BoxSize > small.BoxSize);
    }

    [Fact]
    public void ARingBoxFitsTheRing()
    {
        var r = new CrosshairStyle(CrosshairShape.Circle, Size: 12, Thickness: 3);

        Assert.True(r.Centre >= 12 + 1);
    }

    // ---- values are clamped, so a hand-edited file cannot break it ----

    [Theory]
    [InlineData(-5, CrosshairStyle.MinSize)]
    [InlineData(9999, CrosshairStyle.MaxSize)]
    public void SizeIsClamped(int given, int expected)
    {
        var c = new CrosshairStyle(CrosshairShape.Cross, Size: given, Gap: 0);

        Assert.Equal(expected, c.Arms().Max(a => System.Math.Abs(a.Y2)));
    }

    [Theory]
    [InlineData(0, CrosshairStyle.MinThickness)]
    [InlineData(50, CrosshairStyle.MaxThickness)]
    public void ThicknessIsClamped(int given, int expected)
    {
        Assert.Equal(expected, new CrosshairStyle(Thickness: given).StrokeWidth);
    }

    [Theory]
    [InlineData(-10, 0.0)]
    [InlineData(200, 1.0)]
    [InlineData(50, 0.5)]
    public void OpacityIsAClampedFraction(int percent, double expected)
    {
        Assert.Equal(expected, new CrosshairStyle(OpacityPercent: percent).Opacity, 3);
    }

    // ---- the gallery is all usable ----

    [Fact]
    public void EveryGalleryEntryHasSomethingToDraw()
    {
        Assert.NotEmpty(CrosshairGallery.All);

        foreach ((string name, CrosshairStyle style) in CrosshairGallery.All)
        {
            bool draws = style.Arms().Count > 0 || style.HasDot || style.HasRing;
            Assert.True(draws, $"{name} draws nothing");
            Assert.True(style.BoxSize > 0, $"{name} has no box");
        }
    }

    [Fact]
    public void GalleryNamesAreUnique()
    {
        var names = CrosshairGallery.All.Select(e => e.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void GalleryCoversTheMainShapes()
    {
        var shapes = CrosshairGallery.All.Select(p => p.Style.Shape).Distinct().ToList();

        Assert.Contains(CrosshairShape.Dot, shapes);
        Assert.Contains(CrosshairShape.Cross, shapes);
        Assert.Contains(CrosshairShape.CrossDot, shapes);
        Assert.Contains(CrosshairShape.X, shapes);
        Assert.Contains(CrosshairShape.Circle, shapes);
        Assert.Contains(CrosshairShape.TShape, shapes);
    }

    [Fact]
    public void GalleryHasPlentyOfDots()
    {
        int dots = CrosshairGallery.All.Count(e => e.Style.Shape == CrosshairShape.Dot);
        Assert.True(dots >= 6, $"only {dots} dot crosshairs");
    }

    [Fact]
    public void ByNameFindsAnEntryAndFallsBackToDefault()
    {
        Assert.Equal("Red Dot", CrosshairGallery.ByName("Red Dot").Name);
        Assert.Equal(CrosshairGallery.Default.Name, CrosshairGallery.ByName("no such crosshair").Name);
        Assert.Equal(CrosshairGallery.Default.Name, CrosshairGallery.ByName(null).Name);
    }
}
