using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace JinxyMac;

/// <summary>
/// Reads a kit's picture, trims it to the character, and puts it on the backdrop.
/// </summary>
/// <remarks>
/// The pictures are cut-outs on transparency, at anything from 240x316 to
/// 2000x2000, with surrounding empty space of wildly different margins. Drawn as
/// they come, one kit fills its tile and the next floats in the middle of it,
/// and a black silhouette disappears against a dark panel entirely.
///
/// Not a partial of <c>MainWindow</c> despite the file name matching the
/// original: everything here is a pure function from a path or a decoded
/// picture to another picture, with no dependency on the window itself, so it
/// stays out of MainWindow's already-large partial-class graph and is testable
/// on its own.
///
/// This lives outside <c>Core</c> rather than inside it because
/// <see cref="Avalonia.Media.Imaging.Bitmap"/> is an Avalonia type — Core has
/// no Avalonia dependency and the test project compiles it without one.
/// </remarks>
public static class KitArtImage
{
    public const int Canvas = 256;

    /// <summary>
    /// Reads a picture off disk with its transparency intact.
    /// </summary>
    /// <remarks>
    /// On Windows, this needed <c>PreservePixelFormat</c> plus an explicit
    /// <c>Bgra32</c> conversion: WPF's ordinary decode colour-converted a
    /// downloaded picture to whatever format it judged "closest," which for
    /// these files was one with no alpha channel at all, and every kit drew as
    /// a solid rectangle of whatever colour sat underneath.
    ///
    /// Avalonia decodes through SkiaSharp, which does not have that bug —
    /// checked directly rather than assumed: a synthetic WebP saved under a
    /// ".png" name (the exact mislabelling the wiki serves) round-tripped
    /// through <see cref="Bitmap"/> with a fully transparent pixel still
    /// reading alpha 0 and an opaque one still reading alpha 255, and a real
    /// bundled kit picture decodes with <see cref="Bitmap.Format"/> already
    /// <c>Bgra8888</c>. There is nothing left here to work around, so the
    /// decode is just the constructor.
    /// </remarks>
    public static Bitmap? Decode(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>How much of the square the character may occupy.</summary>
    /// <remarks>
    /// Short of the full square even though the surround is invisible: it is
    /// what keeps every kit the same size as every other, instead of each one
    /// scaled by however tightly its own artwork happens to be cropped.
    /// </remarks>
    private const double Fill = 0.88;

    /// <summary>
    /// The visible bounds of a picture, ignoring its transparent surround.
    /// </summary>
    /// <remarks>
    /// Framing on the file's own size instead would scale by however much empty
    /// space each one happens to carry, which is the difference between a kit
    /// filling its tile and floating in the middle of it.
    /// </remarks>
    public static PixelRect VisibleBounds(Bitmap source)
    {
        int width = source.PixelSize.Width;
        int height = source.PixelSize.Height;
        int stride = width * 4;

        var pixels = new byte[stride * height];

        // Avalonia's CopyPixels wants an unmanaged buffer rather than a
        // managed array, so the pixels are copied out through a temporary
        // native allocation instead of pinning (which would need this project
        // to allow unsafe code for one method).
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

        int left = width, top = height, right = -1, bottom = -1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Above a low floor rather than any alpha at all: these carry a
                // faint halo of near-transparent pixels that would otherwise set
                // the bounds and undo the trim.
                if (pixels[y * stride + x * 4 + 3] <= 12) continue;

                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        return right < left || bottom < top
            ? new PixelRect(0, 0, width, height)
            : new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>Lit in the middle, dark at the rim.</summary>
    /// <remarks>
    /// A flat dark backdrop swallows the dark kits — one measured 8% visible,
    /// near enough a black square. Light gathered behind the character is what
    /// lets a black silhouette and a white yeti both read, and the rim colour
    /// matches the panel so the square's edges do not show.
    /// </remarks>
    private static readonly Color GlowColour = Color.FromRgb(0x4A, 0x3A, 0x76);
    private static readonly Color EdgeColour = Color.FromRgb(0x17, 0x11, 0x29);

    /// <summary>Draws a picture trimmed and centred on the backdrop.</summary>
    public static Bitmap OnBackdrop(Bitmap source)
    {
        PixelRect bounds = VisibleBounds(source);

        var art = new CroppedBitmap(source, bounds);

        double scale = Math.Min(Canvas * Fill / bounds.Width, Canvas * Fill / bounds.Height);
        double drawnWidth = bounds.Width * scale;
        double drawnHeight = bounds.Height * scale;

        var rendered = new RenderTargetBitmap(new PixelSize(Canvas, Canvas));

        using (DrawingContext dc = rendered.CreateDrawingContext())
        {
            var glow = new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            };

            glow.GradientStops.Add(new GradientStop(GlowColour, 0));
            glow.GradientStops.Add(new GradientStop(EdgeColour, 1));

            dc.FillRectangle(glow, new Rect(0, 0, Canvas, Canvas));

            dc.DrawImage(art, new Rect(
                (Canvas - drawnWidth) / 2, (Canvas - drawnHeight) / 2,
                drawnWidth, drawnHeight));
        }

        return rendered;
    }
}
