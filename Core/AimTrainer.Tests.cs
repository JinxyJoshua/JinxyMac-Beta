using System;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// A round of aim practice, worked out without a screen.
/// </summary>
/// <remarks>
/// Seeded, so the target's position is known and a click can be aimed at it. The
/// page only draws what these decisions produce, so getting them right here is
/// most of getting the feature right — which matters more than usual while the
/// app cannot be launched on this machine to look at.
/// </remarks>
public class AimTrainerTests
{
    private static AimSession Started(AimLevel? level = null, double length = 30_000)
    {
        var session = new AimSession(level ?? AimLevel.Normal, length, seed: 1234);
        session.Start(600, 400);
        return session;
    }

    // ---- placing targets ----

    [Fact]
    public void StartingPutsATargetInTheArea()
    {
        AimSession session = Started();

        Assert.True(session.Running);
        Assert.NotNull(session.Target);
    }

    /// <summary>
    /// Always fully inside: half a target in the corner is a smaller target, and
    /// would make the level harder than the one that was chosen.
    /// </summary>
    [Fact]
    public void EveryTargetFitsInsideTheArea()
    {
        var session = new AimSession(AimLevel.Hard, seed: 7);
        session.Start(600, 400);

        for (int i = 0; i < 200; i++)
        {
            AimTarget t = session.Target!.Value;

            Assert.InRange(t.X, t.Radius, 600 - t.Radius);
            Assert.InRange(t.Y, t.Radius, 400 - t.Radius);

            session.Click(t.X, t.Y, i * 10);
        }
    }

    [Fact]
    public void HittingOneBringsUpAnother()
    {
        AimSession session = Started();
        AimTarget first = session.Target!.Value;

        session.Click(first.X, first.Y, 200);

        Assert.NotNull(session.Target);
        Assert.Equal(200, session.Target!.Value.BornMs);
    }

    // ---- hits and misses ----

    [Fact]
    public void AClickOnTheTargetCounts()
    {
        AimSession session = Started();
        AimTarget t = session.Target!.Value;

        Assert.True(session.Click(t.X, t.Y, 150));
        Assert.Equal(1, session.Hits);
        Assert.Equal(0, session.Misses);
    }

    /// <summary>The edge is still the target; just past it is not.</summary>
    [Fact]
    public void TheEdgeCountsAndJustOutsideDoesNot()
    {
        AimSession session = Started();
        AimTarget t = session.Target!.Value;

        Assert.True(session.Click(t.X + t.Radius, t.Y, 100));

        AimTarget next = session.Target!.Value;
        Assert.False(session.Click(next.X + next.Radius + 0.5, next.Y, 200));
    }

    [Fact]
    public void AClickOnNothingIsAMiss()
    {
        AimSession session = Started();
        AimTarget t = session.Target!.Value;

        Assert.False(session.Click(t.X + t.Radius * 4, t.Y, 100));
        Assert.Equal(0, session.Hits);
        Assert.Equal(1, session.Misses);
    }

    /// <summary>
    /// A target left alone until it times out is a miss. Not reacting is the
    /// thing being practised, so it cannot be free.
    /// </summary>
    [Fact]
    public void ATargetLeftTooLongIsAMiss()
    {
        AimSession session = Started();

        session.Tick(AimLevel.Normal.LifetimeMs + 1);

        Assert.Equal(1, session.Misses);
        Assert.NotNull(session.Target);
    }

    [Fact]
    public void MissingDoesNotMoveTheTarget()
    {
        AimSession session = Started();
        AimTarget before = session.Target!.Value;

        session.Click(before.X + before.Radius * 5, before.Y, 100);

        Assert.Equal(before, session.Target!.Value);
    }

    // ---- the numbers shown afterwards ----

    [Fact]
    public void AccuracyCountsEveryAttempt()
    {
        AimSession session = Started();

        for (int i = 0; i < 3; i++)
        {
            AimTarget t = session.Target!.Value;
            session.Click(t.X, t.Y, 100 + i * 100);
        }

        AimTarget last = session.Target!.Value;
        session.Click(last.X + last.Radius * 6, last.Y, 500);

        Assert.Equal(3, session.Hits);
        Assert.Equal(1, session.Misses);
        Assert.Equal(75, session.AccuracyPercent, 3);
    }

    /// <summary>Doing nothing is not perfect aim, and must not divide by zero.</summary>
    [Fact]
    public void AccuracyWithNoAttemptsIsZero()
    {
        Assert.Equal(0, Started().AccuracyPercent);
        Assert.Equal(0, Started().AverageReactionMs);
        Assert.Equal(0, Started().Score);
    }

    [Fact]
    public void ReactionIsMeasuredFromWhenTheTargetAppeared()
    {
        AimSession session = Started();
        AimTarget first = session.Target!.Value;

        session.Click(first.X, first.Y, 300);          // 300ms to hit the first
        AimTarget second = session.Target!.Value;
        session.Click(second.X, second.Y, 500);        // 200ms to hit the second

        Assert.Equal(250, session.AverageReactionMs, 3);
    }

    [Fact]
    public void AMissHasNoReactionTimeToAverageIn()
    {
        AimSession session = Started();
        AimTarget t = session.Target!.Value;

        session.Click(t.X, t.Y, 200);
        AimTarget next = session.Target!.Value;
        session.Click(next.X + next.Radius * 9, next.Y, 900);

        Assert.Equal(200, session.AverageReactionMs, 3);
    }

    /// <summary>
    /// Speed has to count for more than volume, or the easiest level would
    /// always win and the score would be worth nothing.
    /// </summary>
    [Fact]
    public void FasterHitsScoreHigherThanSlowOnes()
    {
        var quick = new AimSession(AimLevel.Normal, seed: 5);
        var slow = new AimSession(AimLevel.Normal, seed: 5);
        quick.Start(600, 400);
        slow.Start(600, 400);

        for (int i = 0; i < 5; i++)
        {
            AimTarget q = quick.Target!.Value;
            quick.Click(q.X, q.Y, i * 1000 + 150);

            AimTarget s = slow.Target!.Value;
            slow.Click(s.X, s.Y, i * 1000 + 800);
        }

        Assert.True(quick.Score > slow.Score, $"quick {quick.Score} vs slow {slow.Score}");
    }

    [Fact]
    public void ASmallerTargetScoresHigherForTheSameHits()
    {
        var easy = new AimSession(AimLevel.Easy, seed: 9);
        var hard = new AimSession(AimLevel.Hard, seed: 9);
        easy.Start(600, 400);
        hard.Start(600, 400);

        foreach (AimSession session in new[] { easy, hard })
        {
            for (int i = 0; i < 5; i++)
            {
                AimTarget t = session.Target!.Value;
                session.Click(t.X, t.Y, i * 1000 + 300);
            }
        }

        Assert.True(hard.Score > easy.Score, $"hard {hard.Score} vs easy {easy.Score}");
    }

    // ---- the round ending ----

    [Fact]
    public void TheRoundEndsWhenItsTimeIsUp()
    {
        AimSession session = Started(length: 5_000);

        Assert.True(session.Tick(4_999));
        Assert.False(session.Tick(5_000));

        Assert.False(session.Running);
        Assert.Null(session.Target);
    }

    [Fact]
    public void ClicksAfterTheRoundDoNothing()
    {
        AimSession session = Started(length: 1_000);
        session.Tick(1_000);

        Assert.False(session.Click(100, 100, 1_100));
        Assert.Equal(0, session.Hits);
        Assert.Equal(0, session.Misses);
    }

    [Fact]
    public void StartingAgainClearsTheLastRound()
    {
        AimSession session = Started();
        AimTarget t = session.Target!.Value;
        session.Click(t.X, t.Y, 100);
        session.Click(t.X + t.Radius * 7, t.Y, 200);

        session.Start(600, 400);

        Assert.Equal(0, session.Hits);
        Assert.Equal(0, session.Misses);
        Assert.Equal(0, session.AverageReactionMs);
    }

    [Fact]
    public void EveryLevelIsHarderThanTheOneBefore()
    {
        Assert.True(AimLevel.Normal.Radius < AimLevel.Easy.Radius);
        Assert.True(AimLevel.Hard.Radius < AimLevel.Normal.Radius);

        Assert.True(AimLevel.Normal.LifetimeMs < AimLevel.Easy.LifetimeMs);
        Assert.True(AimLevel.Hard.LifetimeMs < AimLevel.Normal.LifetimeMs);

        Assert.Equal(3, AimLevel.All.Count);
    }
}
