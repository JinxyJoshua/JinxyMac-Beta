using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using JinxyMac.Core;
using Xunit;

namespace JinxyMac.Tests;

/// <summary>
/// Reading a kit's picture without losing its transparency.
/// </summary>
/// <remarks>
/// On Windows this covered a bug that shipped and was visible on every kit:
/// WPF's ordinary decode converted the real files to a format with no alpha
/// channel at all, so every transparent pixel took whatever colour was stored
/// underneath it and a kit drew as a solid rectangle instead of a character.
///
/// Avalonia decodes through SkiaSharp, which does not have that fault. This
/// suite is what confirms that, on the same fixture that caught the original
/// bug, rather than assuming it from the framework change alone.
/// </remarks>
public class KitArtImageTests
{
    /// <summary>
    /// The fixture is a WebP, deliberately, and that is the whole point.
    /// </summary>
    /// <remarks>
    /// The wiki serves its artwork as WebP. The bug that motivated this file
    /// came from writing those bytes to disk under a hardcoded ".png" name, so
    /// the fixture keeps that same mislabelling — a real PNG would decode fine
    /// either way and prove nothing.
    ///
    /// 40x40 with a 10px clear margin — 400 opaque pixels, 1200 transparent —
    /// and a loud colour stored underneath the transparent ones, so a decode
    /// that discarded alpha would fail loudly rather than produce a dark
    /// square that might pass unnoticed against a dark panel.
    /// </remarks>
    private static string Fixture() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "transparent.webp");

    private static byte[] RawPixels(Bitmap source)
    {
        int width = source.PixelSize.Width;
        int height = source.PixelSize.Height;
        int stride = width * 4;

        var pixels = new byte[stride * height];
        nint buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            source.CopyPixels(new PixelRect(0, 0, width, height), buffer, pixels.Length, stride);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static int ClearPixels(Bitmap source)
    {
        byte[] pixels = RawPixels(source);

        int clear = 0;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] <= 12) clear++;

        return clear;
    }

    /// <summary>Alpha has to survive the read. The WPF-era bug returned none.</summary>
    [AvaloniaFact]
    public void KeepsTheTransparencyWhenReadingAPicture()
    {
        Bitmap? read = KitArtImage.Decode(Fixture());

        Assert.NotNull(read);
        Assert.Equal(1200, ClearPixels(read!));
    }

    /// <summary>
    /// The consequence of alpha surviving: the trim keeps the character
    /// instead of the whole file.
    /// </summary>
    [AvaloniaFact]
    public void TrimsToTheVisiblePartRatherThanTheWholeFile()
    {
        Bitmap? read = KitArtImage.Decode(Fixture());
        Assert.NotNull(read);

        PixelRect bounds = KitArtImage.VisibleBounds(read!);

        Assert.Equal(10, bounds.X);
        Assert.Equal(10, bounds.Y);
        Assert.Equal(20, bounds.Width);
        Assert.Equal(20, bounds.Height);
    }

    /// <summary>A picture that is not there is not a crash.</summary>
    [AvaloniaFact]
    public void ReturnsNothingForAFileThatIsNotAPicture()
    {
        Assert.Null(KitArtImage.Decode(
            Path.Combine(Path.GetTempPath(), "jinxy-no-such-file-9f21.png")));
    }

    /// <summary>
    /// A fully opaque picture must still work — the trim falls back to the
    /// whole image rather than to nothing.
    /// </summary>
    [AvaloniaFact]
    public void KeepsTheWholeImageWhenNothingIsTransparent()
    {
        var pixels = new byte[16 * 16 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = 255;

        nint buffer = Marshal.AllocHGlobal(pixels.Length);
        Bitmap opaque;

        try
        {
            Marshal.Copy(pixels, 0, buffer, pixels.Length);

            // The constructor copies the buffer into its own native surface,
            // so it is safe to free ours as soon as it returns.
            opaque = new Bitmap(
                PixelFormat.Bgra8888, AlphaFormat.Premul,
                buffer, new PixelSize(16, 16), new Vector(96, 96), 16 * 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        using (opaque)
        {
            PixelRect bounds = KitArtImage.VisibleBounds(opaque);

            Assert.Equal(0, bounds.X);
            Assert.Equal(0, bounds.Y);
            Assert.Equal(16, bounds.Width);
            Assert.Equal(16, bounds.Height);
        }
    }

    /// <summary>
    /// The whole point of this task: a picture actually appears. Resolves a
    /// real, shipped kit picture through <see cref="KitImages.Find"/> exactly
    /// the way the page will, then decodes it — a path string built and never
    /// opened would prove nothing.
    /// </summary>
    [AvaloniaFact]
    public void ResolvesAndDecodesARealBundledKitPicture()
    {
        string? path = KitImages.Find("Abaddon");
        Assert.NotNull(path);

        Bitmap? decoded = KitArtImage.Decode(path!);

        Assert.NotNull(decoded);
        Assert.Equal(202, decoded!.PixelSize.Width);
        Assert.Equal(256, decoded.PixelSize.Height);
    }

    /// <summary>
    /// The framed result is always exactly the tile size, whatever shape the
    /// source picture was — the page lays out a grid of squares and cannot
    /// have one kit a different size than the rest.
    /// </summary>
    /// <remarks>
    /// <c>OnBackdrop</c> builds a <c>CroppedBitmap</c>, which — unlike the
    /// plain <c>Bitmap</c> the other tests in this class decode — is an
    /// <c>AvaloniaObject</c> and enforces being touched only from the UI
    /// thread, which is why every test in this class runs as
    /// <c>[AvaloniaFact]</c> rather than <c>[Fact]</c>. See
    /// <see cref="TestAppBuilder"/> for what sets that thread up.
    /// </remarks>
    [AvaloniaFact]
    public void FramesARealPictureToTheCanvasSize()
    {
        string? path = KitImages.Find("Abaddon");
        Assert.NotNull(path);

        Bitmap? decoded = KitArtImage.Decode(path!);
        Assert.NotNull(decoded);

        using Bitmap framed = KitArtImage.OnBackdrop(decoded!);

        Assert.Equal(KitArtImage.Canvas, framed.PixelSize.Width);
        Assert.Equal(KitArtImage.Canvas, framed.PixelSize.Height);
    }
}
