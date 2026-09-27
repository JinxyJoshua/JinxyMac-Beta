using System;
using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using JinxyMac.Core;
using Xunit;

namespace JinxyMac.Tests;

/// <summary>
/// Drawing a crosshair into the picture that becomes a Roblox cursor.
/// </summary>
/// <remarks>
/// <c>[AvaloniaFact]</c> rather than <c>[Fact]</c>: these render through real
/// Skia under headless Avalonia, so they check the actual pixels rather than a
/// stub that draws nothing.
/// </remarks>
public class CrosshairImageTests
{
    /// <summary>
    /// The bug this exists to prevent, and it shipped once on Windows: the size
    /// slider looked like it did nothing in game, because every cursor came out
    /// the same 64 pixels and only the crosshair's share of that square changed.
    /// Roblox draws the cursor at the image's own size, so the image has to grow.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(64, 1.0, 64)]
    [InlineData(64, 1.5, 96)]
    [InlineData(64, 0.4, 26)]
    [InlineData(32, 1.5, 48)]
    public void TheSizeChangesTheCursorImagesRealDimensions(int basePixels, double factor, int expected)
    {
        byte[] png = CrosshairImage.RenderCursorPng(new CrosshairStyle(), basePixels, factor);

        Assert.Equal(expected, WidthOf(png));
    }

    [AvaloniaTheory]
    [InlineData(64, 0.01, 16)]
    [InlineData(64, 99.0, 160)]
    public void ACursorIsNeverAPixelWideOrTheSizeOfTheScreen(int basePixels, double factor, int expected)
    {
        Assert.Equal(expected, WidthOf(CrosshairImage.RenderCursorPng(new CrosshairStyle(), basePixels, factor)));
    }

    [AvaloniaFact]
    public void ACrosshairActuallyDrawsSomething()
    {
        using RenderTargetBitmap bitmap =
            CrosshairImage.RenderBitmap(new CrosshairStyle(CrosshairShape.Cross), 64, 1.0);

        Assert.True(OpaquePixels(bitmap) > 0, "the crosshair rendered as an empty image");
    }

    /// <summary>
    /// The background has to stay clear, or the cursor is a coloured square in
    /// the middle of the game.
    /// </summary>
    [AvaloniaFact]
    public void TheCornersStayTransparent()
    {
        using RenderTargetBitmap bitmap =
            CrosshairImage.RenderBitmap(new CrosshairStyle(CrosshairShape.Cross, Size: 9, Gap: 4), 64, 1.0);

        Assert.Equal(0, AlphaAt(bitmap, 1, 1));
        Assert.Equal(0, AlphaAt(bitmap, 62, 62));
    }

    [AvaloniaFact]
    public void ADotDrawsAtTheCentre()
    {
        using RenderTargetBitmap bitmap =
            CrosshairImage.RenderBitmap(new CrosshairStyle(CrosshairShape.Dot, DotSize: 5), 64, 1.0);

        Assert.True(AlphaAt(bitmap, 32, 32) > 0, "nothing drawn at the centre");
    }

    /// <summary>
    /// A bigger cursor image should carry a bigger crosshair, not the same
    /// crosshair adrift in more empty space.
    /// </summary>
    [AvaloniaFact]
    public void ABiggerCursorCarriesABiggerCrosshair()
    {
        var style = new CrosshairStyle(CrosshairShape.Cross);

        using RenderTargetBitmap small = CrosshairImage.RenderBitmap(style, 64, 1.0);
        using RenderTargetBitmap large = CrosshairImage.RenderBitmap(style, 128, 1.0);

        // Four times the pixels, so a crosshair that scaled with the image
        // covers far more than the same crosshair centred in a larger square.
        Assert.True(OpaquePixels(large) > OpaquePixels(small) * 2,
            "the crosshair did not grow with the image");
    }

    // ---- imported pictures ----

    [AvaloniaFact]
    public void AnImportedPictureKeepsItsShapeRatherThanBeingStretched()
    {
        string path = WriteWideImage();

        try
        {
            using RenderTargetBitmap? bitmap = CrosshairImage.RenderImageBitmap(path, 64, 1.0);
            Assert.NotNull(bitmap);

            // The source is twice as wide as it is tall, so fitted into a square
            // it fills the middle band and leaves the top and bottom clear.
            Assert.True(AlphaAt(bitmap!, 32, 32) > 0, "nothing in the middle");
            Assert.Equal(0, AlphaAt(bitmap!, 32, 2));
            Assert.Equal(0, AlphaAt(bitmap!, 32, 61));
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void AnImportedPictureIsWrittenAtTheCursorSizeToo()
    {
        string path = WriteWideImage();

        try
        {
            Assert.Equal(96, WidthOf(CrosshairImage.RenderImageCursorPng(path, 64, 1.5)));
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// The picture can be gone by the time a crosshair is applied — deleted from
    /// the folder by hand. Writing nothing would leave Roblox's own arrow in the
    /// middle of a replaced set, so it falls back to a drawn crosshair.
    /// </summary>
    [AvaloniaFact]
    public void AMissingPictureFallsBackToADrawnCrosshair()
    {
        byte[] png = CrosshairImage.RenderImageCursorPng(
            Path.Combine(Path.GetTempPath(), "jinxy-no-such-crosshair.png"), 64, 1.0);

        Assert.Equal(64, WidthOf(png));
        Assert.True(png.Length > 0);
    }

    [AvaloniaFact]
    public void AFileThatIsNotAnImageReadsAsNothing()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(path, "not a picture");

        try { Assert.Null(CrosshairImage.TryLoadImage(path)); }
        finally { File.Delete(path); }
    }

    // ---- helpers ----

    private static string WriteWideImage()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");

        var bitmap = new RenderTargetBitmap(new PixelSize(64, 32), new Vector(96, 96));
        using (Avalonia.Media.DrawingContext context = bitmap.CreateDrawingContext())
            context.FillRectangle(Avalonia.Media.Brushes.Red, new Rect(0, 0, 64, 32));

        using (var file = File.Create(path)) bitmap.Save(file);
        bitmap.Dispose();

        return path;
    }

    /// <summary>The width recorded in a PNG's header.</summary>
    private static int WidthOf(byte[] png) =>
        (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];

    private static byte AlphaAt(RenderTargetBitmap bitmap, int x, int y)
    {
        byte[] pixels = Pixels(bitmap);
        return pixels[(y * bitmap.PixelSize.Width + x) * 4 + 3];
    }

    private static int OpaquePixels(RenderTargetBitmap bitmap)
    {
        byte[] pixels = Pixels(bitmap);
        int count = 0;

        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] > 0) count++;

        return count;
    }

    private static byte[] Pixels(RenderTargetBitmap bitmap)
    {
        int width = bitmap.PixelSize.Width, height = bitmap.PixelSize.Height;
        var buffer = new byte[width * height * 4];

        // Through unmanaged memory rather than a fixed pointer, so the test
        // project does not have to be compiled with unsafe code enabled just to
        // read a few pixels.
        IntPtr block = System.Runtime.InteropServices.Marshal.AllocHGlobal(buffer.Length);

        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), block, buffer.Length, width * 4);
            System.Runtime.InteropServices.Marshal.Copy(block, buffer, 0, buffer.Length);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(block);
        }

        return buffer;
    }
}
