using System;
using System.Collections.Generic;
using System.Linq;

namespace JinxyMac.Core;

/// <summary>
/// The ready-made crosshairs shown in the gallery, in the order they appear.
/// </summary>
/// <remarks>
/// Each is a plain <see cref="CrosshairStyle"/>, so the same geometry that draws
/// the picture also draws the cursor written into Roblox — the tile you pick and
/// the cursor you get are the one thing. Grouped roughly as crosses, X shapes,
/// dots and rings, with the two-tone "Core" and split-colour styles for looks.
/// </remarks>
public static class CrosshairGallery
{
    // A shared palette, so the same green or cyan is used everywhere.
    private const string Green = "#33FF66";
    private const string Cyan = "#00E5FF";
    private const string Sky = "#45B7FF";
    private const string Red = "#FF3B3B";
    private const string Crimson = "#FF2D55";
    private const string Pink = "#FF57E6";
    private const string Yellow = "#FFE14D";
    private const string Orange = "#FF9C3B";
    private const string Purple = "#A855F7";
    private const string Violet = "#8B5CF6";
    private const string White = "#FFFFFF";
    private const string Black = "#111111";

    public static IReadOnlyList<(string Name, CrosshairStyle Style)> All { get; } =
        new (string, CrosshairStyle)[]
    {
        // ---- crosses / plus ----
        ("Green Cross",   new CrosshairStyle(CrosshairShape.Cross, Size: 9, Thickness: 3, Gap: 4, ColorHex: Green)),
        ("Cyan Cross",    new CrosshairStyle(CrosshairShape.Cross, Size: 9, Thickness: 3, Gap: 4, ColorHex: Cyan)),
        ("Red Plus",      new CrosshairStyle(CrosshairShape.Cross, Size: 9, Thickness: 3, Gap: 4, ColorHex: Red)),
        ("Crimson Plus",  new CrosshairStyle(CrosshairShape.Cross, Size: 10, Thickness: 3, Gap: 3, ColorHex: Crimson)),
        ("Black Plus",    new CrosshairStyle(CrosshairShape.Cross, Size: 9, Thickness: 3, Gap: 4, ColorHex: Black)),
        ("Thin Red",      new CrosshairStyle(CrosshairShape.Cross, Size: 12, Thickness: 2, Gap: 5, ColorHex: Red)),
        ("Sniper",        new CrosshairStyle(CrosshairShape.Cross, Size: 20, Thickness: 2, Gap: 6, ColorHex: Crimson)),

        // ---- two-tone "core" crosses (bright centre) ----
        ("White + Purple", new CrosshairStyle(CrosshairShape.CrossDot, Size: 9, Thickness: 3, Gap: 4, DotSize: 3, ColorHex: White, DotColorHex: Purple)),
        ("Red / Cyan",     new CrosshairStyle(CrosshairShape.CrossDot, Size: 9, Thickness: 3, Gap: 4, DotSize: 3, ColorHex: Red, DotColorHex: Cyan)),
        ("Purple Core",    new CrosshairStyle(CrosshairShape.CrossDot, Size: 9, Thickness: 3, Gap: 4, DotSize: 3, ColorHex: Purple, DotColorHex: White)),
        ("Green Core",     new CrosshairStyle(CrosshairShape.CrossDot, Size: 9, Thickness: 3, Gap: 4, DotSize: 3, ColorHex: Green, DotColorHex: White)),
        ("Pink Core",      new CrosshairStyle(CrosshairShape.CrossDot, Size: 9, Thickness: 3, Gap: 4, DotSize: 3, ColorHex: Pink, DotColorHex: White)),
        ("Yellow Core",    new CrosshairStyle(CrosshairShape.CrossDot, Size: 9, Thickness: 3, Gap: 4, DotSize: 3, ColorHex: Yellow, DotColorHex: White)),

        // ---- X shapes ----
        ("Cyan X",     new CrosshairStyle(CrosshairShape.X, Size: 9, Thickness: 3, Gap: 3, ColorHex: Cyan)),
        ("Red X",      new CrosshairStyle(CrosshairShape.X, Size: 9, Thickness: 3, Gap: 3, ColorHex: Red)),
        ("Green X",    new CrosshairStyle(CrosshairShape.X, Size: 9, Thickness: 3, Gap: 3, ColorHex: Green)),
        ("Purple X",   new CrosshairStyle(CrosshairShape.X, Size: 9, Thickness: 3, Gap: 3, ColorHex: Violet)),
        ("X + Dot",    new CrosshairStyle(CrosshairShape.XDot, Size: 9, Thickness: 3, Gap: 3, DotSize: 3, ColorHex: White, DotColorHex: Red)),
        ("X Green",    new CrosshairStyle(CrosshairShape.XDot, Size: 9, Thickness: 3, Gap: 3, DotSize: 3, ColorHex: Green, DotColorHex: White)),

        // ---- dots ----
        ("Red Dot",    new CrosshairStyle(CrosshairShape.Dot, DotSize: 4, ColorHex: Red)),
        ("Cyan Dot",   new CrosshairStyle(CrosshairShape.Dot, DotSize: 4, ColorHex: Cyan)),
        ("White Dot",  new CrosshairStyle(CrosshairShape.Dot, DotSize: 4, ColorHex: White)),
        ("Green Dot",  new CrosshairStyle(CrosshairShape.Dot, DotSize: 4, ColorHex: Green)),
        ("Purple Dot", new CrosshairStyle(CrosshairShape.Dot, DotSize: 4, ColorHex: Purple)),
        ("Pink Dot",   new CrosshairStyle(CrosshairShape.Dot, DotSize: 4, ColorHex: Pink)),
        ("Yellow Dot", new CrosshairStyle(CrosshairShape.Dot, DotSize: 4, ColorHex: Yellow)),
        ("Orange Dot", new CrosshairStyle(CrosshairShape.Dot, DotSize: 4, ColorHex: Orange)),
        ("Sky Dot",    new CrosshairStyle(CrosshairShape.Dot, DotSize: 4, ColorHex: Sky)),
        ("Tiny Dot",   new CrosshairStyle(CrosshairShape.Dot, DotSize: 2, ColorHex: White)),
        ("Big Dot",    new CrosshairStyle(CrosshairShape.Dot, DotSize: 7, ColorHex: Red)),

        // ---- rings and T ----
        ("Ring",       new CrosshairStyle(CrosshairShape.Circle, Size: 8, Thickness: 3, ColorHex: Cyan)),
        ("Ring + Dot", new CrosshairStyle(CrosshairShape.CircleDot, Size: 9, Thickness: 3, DotSize: 2, ColorHex: Cyan, DotColorHex: White)),
        ("Green Ring", new CrosshairStyle(CrosshairShape.Circle, Size: 8, Thickness: 3, ColorHex: Green)),
        ("Bridge T",   new CrosshairStyle(CrosshairShape.TShape, Size: 11, Thickness: 3, Gap: 4, ColorHex: Yellow)),
    };

    /// <summary>The first entry, used when nothing has been chosen yet.</summary>
    public static (string Name, CrosshairStyle Style) Default => All[0];

    /// <summary>Finds a gallery entry by name, falling back to the default.</summary>
    public static (string Name, CrosshairStyle Style) ByName(string? name)
    {
        foreach ((string n, CrosshairStyle s) in All)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return (n, s);

        return Default;
    }

    /// <summary>Names in gallery order, for building the tiles.</summary>
    public static IEnumerable<string> Names => All.Select(e => e.Name);

    /// <summary>Whether a name belongs to a built-in gallery entry.</summary>
    public static bool IsBuiltIn(string? name) =>
        All.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A crosshair the user built and saved, stored in the settings file.
/// </summary>
/// <remarks>
/// Kept as plain strings rather than a <see cref="CrosshairStyle"/> so the
/// settings file stays readable and an unknown shape or a bad colour falls back
/// to something drawable rather than throwing. The size is not stored here — it
/// lives with every other crosshair's size, by name, in the settings.
/// </remarks>
public sealed class CustomCrosshair
{
    public string Name { get; set; } = "";
    public string Shape { get; set; } = "Cross";
    public string Color { get; set; } = "#33FF66";
    public string? DotColor { get; set; }

    /// <summary>
    /// The bare file name of an imported image, when this crosshair is a picture
    /// the user chose rather than one built from a shape and colour. Null for a
    /// built-with-controls crosshair.
    /// </summary>
    public string? ImageFile { get; set; }

    /// <summary>Whether this is an imported image rather than a drawn shape.</summary>
    public bool IsImage => !string.IsNullOrWhiteSpace(ImageFile);

    /// <summary>The drawable style, with sensible proportions filled in.</summary>
    public CrosshairStyle ToStyle()
    {
        CrosshairShape shape = Enum.TryParse(Shape, out CrosshairShape s) ? s : CrosshairShape.Cross;
        return new CrosshairStyle(shape, Size: 9, Thickness: 3, Gap: 4, DotSize: 3,
            ColorHex: Color, DotColorHex: DotColor);
    }
}
