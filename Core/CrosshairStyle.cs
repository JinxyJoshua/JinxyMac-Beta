using System;
using System.Collections.Generic;

namespace JinxyMac.Core;

/// <summary>The kinds of crosshair that can be drawn into a cursor image.</summary>
public enum CrosshairShape
{
    /// <summary>A single filled centre dot.</summary>
    Dot,

    /// <summary>Four arms with a centre gap.</summary>
    Cross,

    /// <summary>Four arms and a centre dot.</summary>
    CrossDot,

    /// <summary>Four diagonal arms — an X.</summary>
    X,

    /// <summary>An X with a centre dot.</summary>
    XDot,

    /// <summary>A hollow ring.</summary>
    Circle,

    /// <summary>A hollow ring with a centre dot.</summary>
    CircleDot,

    /// <summary>Left, right and bottom arms — the top left open, so it does not
    /// hide what is above your aim.</summary>
    TShape
}

/// <summary>One straight arm of a crosshair, as offsets from the centre.</summary>
public readonly record struct CrosshairArm(int X1, int Y1, int X2, int Y2);

/// <summary>
/// A crosshair: its shape and the numbers that draw it, with no image or pixels
/// attached.
/// </summary>
/// <remarks>
/// The crosshair is baked into Roblox's own cursor pictures (see
/// <see cref="RobloxCursors"/>), so it becomes the real in-game cursor rather
/// than a separate window. The geometry lives here, apart from the renderer that
/// turns it into a PNG, so the arm positions and the box they need can be worked
/// out and tested without a screen.
///
/// <para><see cref="DotColorHex"/> lets the centre dot be a second colour — a
/// bright core on a darker cross — which is how the two-tone gallery entries
/// (White + Purple, Red / Cyan and the "Core" styles) are built. Null means the
/// dot shares the arm colour.</para>
/// </remarks>
public sealed record CrosshairStyle(
    CrosshairShape Shape = CrosshairShape.Cross,
    int Size = 10,
    int Thickness = 2,
    int Gap = 4,
    int DotSize = 3,
    string ColorHex = "#33FF66",
    string? DotColorHex = null,
    int OpacityPercent = 100,
    bool Outline = true)
{
    public const int MinSize = 0, MaxSize = 60;
    public const int MinThickness = 1, MaxThickness = 12;
    public const int MinGap = 0, MaxGap = 40;
    public const int MinDot = 1, MaxDot = 24;

    private int Arm => Math.Clamp(Size, MinSize, MaxSize);
    private int Stroke => Math.Clamp(Thickness, MinThickness, MaxThickness);
    private int CentreGap => Math.Clamp(Gap, MinGap, MaxGap);
    private int Dot => Math.Clamp(DotSize, MinDot, MaxDot);

    /// <summary>The straight arms, as offsets from the centre. Empty for a dot or ring.</summary>
    public IReadOnlyList<CrosshairArm> Arms()
    {
        var arms = new List<CrosshairArm>(4);
        int g = CentreGap, far = CentreGap + Arm;

        if (Shape is CrosshairShape.Cross or CrosshairShape.CrossDot or CrosshairShape.TShape)
        {
            // The top arm is dropped from the T so it does not sit over the head
            // of whatever you are aiming at.
            if (Shape != CrosshairShape.TShape) arms.Add(new CrosshairArm(0, -g, 0, -far));

            arms.Add(new CrosshairArm(0, g, 0, far));      // down
            arms.Add(new CrosshairArm(-g, 0, -far, 0));    // left
            arms.Add(new CrosshairArm(g, 0, far, 0));      // right
        }
        else if (Shape is CrosshairShape.X or CrosshairShape.XDot)
        {
            arms.Add(new CrosshairArm(g, g, far, far));      // down-right
            arms.Add(new CrosshairArm(-g, -g, -far, -far));  // up-left
            arms.Add(new CrosshairArm(g, -g, far, -far));    // up-right
            arms.Add(new CrosshairArm(-g, g, -far, far));    // down-left
        }

        return arms;
    }

    /// <summary>Whether a centre dot is drawn, and its radius.</summary>
    public bool HasDot => Shape is CrosshairShape.Dot or CrosshairShape.CrossDot
        or CrosshairShape.XDot or CrosshairShape.CircleDot;
    public int DotRadius => Dot;

    /// <summary>The dot's colour — its own if one was given, else the arm colour.</summary>
    public string EffectiveDotColor => string.IsNullOrWhiteSpace(DotColorHex) ? ColorHex : DotColorHex!;

    /// <summary>Whether a ring is drawn, and its radius.</summary>
    public bool HasRing => Shape is CrosshairShape.Circle or CrosshairShape.CircleDot;
    public int RingRadius => Arm;

    /// <summary>
    /// The side of the square box the crosshair needs, big enough for the whole
    /// crosshair plus its stroke and outline with a pixel to spare.
    /// </summary>
    public int BoxSize
    {
        get
        {
            int reach = 0;

            foreach (CrosshairArm a in Arms())
                reach = Math.Max(reach, Math.Max(Math.Abs(a.Y2), Math.Abs(a.X2)));

            if (HasRing) reach = Math.Max(reach, RingRadius);
            if (HasDot) reach = Math.Max(reach, DotRadius);

            // Half the stroke sticks out past the end of an arm, the outline adds
            // one more each side, and a spare pixel keeps nothing clipped.
            int pad = Stroke / 2 + (Outline ? 1 : 0) + 1;

            return (reach + pad) * 2;
        }
    }

    /// <summary>The centre of the box, where every offset is measured from.</summary>
    public int Centre => BoxSize / 2;

    /// <summary>The stroke width arms and the ring are drawn at.</summary>
    public int StrokeWidth => Stroke;

    /// <summary>Opacity as a 0–1 fraction, clamped.</summary>
    public double Opacity => Math.Clamp(OpacityPercent, 0, 100) / 100.0;
}
