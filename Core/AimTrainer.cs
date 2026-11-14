using System;
using System.Collections.Generic;
using System.Linq;

namespace JinxyMac.Core;

/// <summary>One target to click, placed in the practice area.</summary>
/// <param name="X">Centre, in the area's own units.</param>
/// <param name="Y">Centre, in the area's own units.</param>
/// <param name="Radius">Half the target's width.</param>
/// <param name="BornMs">When it appeared, measured from the session start.</param>
public readonly record struct AimTarget(double X, double Y, double Radius, double BornMs);

/// <summary>How hard the targets are to hit.</summary>
/// <param name="Radius">Target size. Smaller is harder.</param>
/// <param name="LifetimeMs">How long one waits to be hit before it counts as missed.</param>
public readonly record struct AimLevel(string Name, double Radius, double LifetimeMs)
{
    public static AimLevel Easy { get; } = new("Easy", 34, 1500);
    public static AimLevel Normal { get; } = new("Normal", 24, 1100);
    public static AimLevel Hard { get; } = new("Hard", 17, 850);

    public static IReadOnlyList<AimLevel> All { get; } = new[] { Easy, Normal, Hard };
}

/// <summary>
/// A round of clicking targets, with no screen attached.
/// </summary>
/// <remarks>
/// Every decision — where the next target goes, whether a click hit it, when a
/// target has been up too long — is made here, from a position and a time passed
/// in. The page above only draws what this says and reports clicks.
///
/// <para>That split is the point: the maths of a hit and the arithmetic of
/// accuracy are the parts that can be quietly wrong, and they are testable
/// exactly because they do not need a window. The random placement takes a seed,
/// so a test can know where the target will be.</para>
///
/// <para>It trains the thing the rest of the app cannot do for you. The clicker
/// sends clicks; it does not put the crosshair on anything.</para>
/// </remarks>
public sealed class AimSession
{
    private readonly Random _random;
    private readonly List<double> _reactions = new();

    private double _areaWidth;
    private double _areaHeight;

    public AimSession(AimLevel level, double lengthMs = 30_000, int? seed = null)
    {
        Level = level;
        LengthMs = lengthMs;
        _random = seed is { } s ? new Random(s) : new Random();
    }

    public AimLevel Level { get; }
    public double LengthMs { get; }

    public bool Running { get; private set; }
    public AimTarget? Target { get; private set; }

    public int Hits { get; private set; }

    /// <summary>Clicks that landed on nothing, plus targets that timed out.</summary>
    public int Misses { get; private set; }

    /// <summary>Hits as a percentage of every click and every timeout.</summary>
    /// <remarks>
    /// Zero attempts reads as 0, not as a division by zero and not as 100 —
    /// having done nothing is not perfect aim.
    /// </remarks>
    public double AccuracyPercent =>
        Hits + Misses == 0 ? 0 : Hits * 100.0 / (Hits + Misses);

    /// <summary>Average time from a target appearing to being hit.</summary>
    /// <remarks>Only hits have a reaction time; a miss has nothing to measure.</remarks>
    public double AverageReactionMs => _reactions.Count == 0 ? 0 : _reactions.Average();

    /// <summary>
    /// Hits weighted by how quickly they came and how small the target was.
    /// </summary>
    /// <remarks>
    /// One number to beat, so a round can be compared with the last one. Speed
    /// counts for more than volume: clicking a slow target ten times should not
    /// beat clicking a fast one eight times, or the score would reward the
    /// easiest level.
    /// </remarks>
    public int Score
    {
        get
        {
            if (_reactions.Count == 0) return 0;

            double sizeBonus = AimLevel.Easy.Radius / Level.Radius;
            double speed = _reactions.Average(r => Math.Max(0, Level.LifetimeMs - r) / Level.LifetimeMs);

            return (int)Math.Round(Hits * 100 * sizeBonus * (0.5 + speed));
        }
    }

    /// <summary>Starts a round in an area of this size, and places the first target.</summary>
    public void Start(double areaWidth, double areaHeight)
    {
        _areaWidth = areaWidth;
        _areaHeight = areaHeight;

        Hits = 0;
        Misses = 0;
        _reactions.Clear();

        Running = true;
        Spawn(0);
    }

    public void Stop()
    {
        Running = false;
        Target = null;
    }

    /// <summary>
    /// Moves the round on to <paramref name="elapsedMs"/>, expiring a target that
    /// has been up too long and ending the round when its time is up.
    /// </summary>
    /// <returns>False once the round is over.</returns>
    public bool Tick(double elapsedMs)
    {
        if (!Running) return false;

        if (elapsedMs >= LengthMs)
        {
            Stop();
            return false;
        }

        if (Target is { } target && elapsedMs - target.BornMs >= Level.LifetimeMs)
        {
            // Up too long. That is a miss even though nothing was clicked: not
            // reacting is the thing being practised.
            Misses++;
            Spawn(elapsedMs);
        }

        return true;
    }

    /// <summary>
    /// Reports a click in the practice area.
    /// </summary>
    /// <returns>True if it landed on the target.</returns>
    public bool Click(double x, double y, double elapsedMs)
    {
        if (!Running || Target is not { } target) return false;

        double dx = x - target.X, dy = y - target.Y;

        if (dx * dx + dy * dy > target.Radius * target.Radius)
        {
            Misses++;
            return false;
        }

        Hits++;
        _reactions.Add(Math.Max(0, elapsedMs - target.BornMs));
        Spawn(elapsedMs);
        return true;
    }

    /// <summary>
    /// Places the next target, always fully inside the area.
    /// </summary>
    /// <remarks>
    /// Inset by the radius so a target never hangs over an edge: half a target
    /// in the corner is a smaller target, which would make the level harder in a
    /// way nobody chose.
    /// </remarks>
    private void Spawn(double elapsedMs)
    {
        double r = Level.Radius;
        double x = r + _random.NextDouble() * Math.Max(0, _areaWidth - r * 2);
        double y = r + _random.NextDouble() * Math.Max(0, _areaHeight - r * 2);

        Target = new AimTarget(x, y, r, elapsedMs);
    }
}
