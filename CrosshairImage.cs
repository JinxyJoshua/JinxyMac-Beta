using System;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// Turns a <see cref="CrosshairStyle"/> into a square PNG, centred, on a
/// transparent background — the picture that becomes a Roblox cursor, a gallery
/// tile and the preview, all from one place.
/// </summary>
/// <remarks>
/// The gallery styles are drawn to look right in a 64-pixel image; every other
/// size scales from that, so a crosshair keeps its proportions whether it is a
/// tiny tile, a big preview or the first-person cursor.
///
/// <para>Here rather than in Core because it draws with Avalonia, and Core is
/// kept free of it (CorePurity.Tests enforces that). Core holds the geometry
/// this reads and the file writing this feeds.</para>
///
/// <para>Needs a rendering platform, so it runs on the UI thread in the app and
/// under <c>[AvaloniaFact]</c> in the tests, where headless Avalonia is
/// configured with real Skia drawing rather than the stub.</para>
/// </remarks>
public static class CrosshairImage
{
    /// <summary>The size the gallery styles are authored against.</summary>
    public const double BaseImage = 64.0;

    /// <summary>A bitmap of the crosshair, for tiles and the preview.</summary>
    public static RenderTargetBitmap RenderBitmap(CrosshairStyle style, int pixels, double sizeFactor)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
            Draw(context, style, pixels, sizeFactor);

        return bitmap;
    }

    /// <summary>The crosshair as PNG bytes.</summary>
    public static byte[] RenderPng(CrosshairStyle style, int pixels, double sizeFactor)
    {
        using RenderTargetBitmap bitmap = RenderBitmap(style, pixels, sizeFactor);
        using var stream = new MemoryStream();

        bitmap.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// The cursor PNG for one of Roblox's cursor slots, sized for the game.
    /// </summary>
    /// <remarks>
    /// Roblox draws the cursor at the image's own pixel size, so the size slider
    /// has to change the image's dimensions, not just how much of a fixed square
    /// the crosshair fills. That was a real bug on Windows: the slider appeared
    /// to do nothing in game because every cursor came out 64 pixels wide.
    ///
    /// A 64-pixel arrow at 150% is written as a 96-pixel image, and the
    /// crosshair keeps its share of the picture because the draw scale grows
    /// with it. Clamped so a cursor can never be a single pixel or fill the
    /// screen.
    /// </remarks>
    public static byte[] RenderCursorPng(CrosshairStyle style, int basePixels, double sizeFactor) =>
        RenderPng(style, CursorPixels(basePixels, sizeFactor), 1.0);

    /// <summary>The side of the cursor image a size factor asks for.</summary>
    public static int CursorPixels(int basePixels, double sizeFactor) =>
        Math.Clamp((int)Math.Round(basePixels * sizeFactor), 16, 160);

    /// <summary>Loads an image file, or null if it will not read.</summary>
    public static Bitmap? TryLoadImage(string path)
    {
        try
        {
            // Read into memory first so the file is not left open: an imported
            // crosshair can be deleted while its tile is on screen.
            using var file = new MemoryStream(File.ReadAllBytes(path));
            return new Bitmap(file);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// An imported picture, centred in a square and scaled to fit.
    /// </summary>
    /// <remarks>
    /// Fitted rather than stretched: a tall or wide picture keeps its shape and
    /// gets transparent space either side, because a squashed crosshair is worse
    /// than a small one.
    /// </remarks>
    public static RenderTargetBitmap? RenderImageBitmap(string path, int pixels, double sizeFactor)
    {
        using Bitmap? source = TryLoadImage(path);
        if (source == null) return null;

        var bitmap = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            double fit = Math.Min(
                pixels / (double)source.PixelSize.Width,
                pixels / (double)source.PixelSize.Height);

            double scale = fit * Math.Clamp(sizeFactor, 0.1, 4.0);
            double w = source.PixelSize.Width * scale, h = source.PixelSize.Height * scale;

            context.DrawImage(source, new Rect((pixels - w) / 2, (pixels - h) / 2, w, h));
        }

        return bitmap;
    }

    /// <summary>The cursor PNG for an imported picture, sized like the built-ins.</summary>
    /// <remarks>
    /// A picture that will not read falls back to the default crosshair rather
    /// than writing nothing: this runs while Roblox's cursors are being
    /// replaced, and leaving one slot unwritten would show the arrow in the
    /// middle of a crosshair set.
    /// </remarks>
    public static byte[] RenderImageCursorPng(string path, int basePixels, double sizeFactor)
    {
        int pixels = CursorPixels(basePixels, sizeFactor);

        using RenderTargetBitmap? bitmap = RenderImageBitmap(path, pixels, 1.0);
        if (bitmap == null) return RenderPng(new CrosshairStyle(), pixels, 1.0);

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    private static void Draw(DrawingContext context, CrosshairStyle style, int pixels, double sizeFactor)
    {
        double centre = pixels / 2.0;
        double s = pixels / BaseImage * sizeFactor;

        IBrush colour = Fill(Parse(style.ColorHex), style.Opacity);
        IBrush dotColour = Fill(Parse(style.EffectiveDotColor), style.Opacity);

        // A contrasting edge keeps the crosshair readable on any background —
        // dark behind a light crosshair, light behind a dark one.
        IBrush armEdge = Fill(Contrast(Parse(style.ColorHex)), style.Opacity);
        IBrush dotEdge = Fill(Contrast(Parse(style.EffectiveDotColor)), style.Opacity);

        double stroke = Math.Max(1.0, style.StrokeWidth * s);
        double edgeExtra = Math.Max(2.0, 2.0 * s);

        foreach (CrosshairArm a in style.Arms())
        {
            var p1 = new Point(centre + a.X1 * s, centre + a.Y1 * s);
            var p2 = new Point(centre + a.X2 * s, centre + a.Y2 * s);

            if (style.Outline) context.DrawLine(RoundPen(armEdge, stroke + edgeExtra), p1, p2);
            context.DrawLine(RoundPen(colour, stroke), p1, p2);
        }

        if (style.HasRing)
        {
            var c = new Point(centre, centre);
            double r = style.RingRadius * s;

            if (style.Outline) context.DrawEllipse(null, RoundPen(armEdge, stroke + edgeExtra), c, r, r);
            context.DrawEllipse(null, RoundPen(colour, stroke), c, r, r);
        }

        if (style.HasDot)
        {
            var c = new Point(centre, centre);
            double r = Math.Max(1.0, style.DotRadius * s);
            double edge = r + Math.Max(1.0, s);

            if (style.Outline) context.DrawEllipse(dotEdge, null, c, edge, edge);
            context.DrawEllipse(dotColour, null, c, r, r);
        }
    }

    private static Pen RoundPen(IBrush brush, double thickness) =>
        new(brush, thickness, lineCap: PenLineCap.Round);

    private static IBrush Fill(Color colour, double opacity) =>
        new SolidColorBrush(colour) { Opacity = opacity };

    /// <summary>A hex colour, or the default green if it will not parse.</summary>
    private static Color Parse(string hex)
    {
        try { return Color.Parse(hex); }
        catch { return Color.FromRgb(0x33, 0xFF, 0x66); }
    }

    /// <summary>Black behind a light colour, white behind a dark one.</summary>
    private static Color Contrast(Color c)
    {
        // Rec. 601 luma, enough to tell a light crosshair from a dark one.
        double luma = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luma < 0.4 ? Colors.White : Colors.Black;
    }
}
