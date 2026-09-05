using Xunit;

namespace JinxyMac.Capture.Tests;

/// <summary>
/// The gate that keeps a second <c>StartAsync</c>/<c>Start</c> call from
/// racing the first through the gap before either assigns the field its own
/// "already running" check reads.
/// </summary>
public class ReentryGuardTests
{
    [Fact]
    public void FirstCallerGetsIn()
    {
        var guard = new ReentryGuard();

        Assert.True(guard.TryEnter());
    }

    /// <summary>
    /// The bug this exists to prevent, reduced to its essence: a second caller
    /// arriving while the first still holds the gate must be refused, not
    /// waved through because nothing has been assigned yet.
    /// </summary>
    [Fact]
    public void SecondCallerIsRefusedWhileTheFirstHoldsIt()
    {
        var guard = new ReentryGuard();

        Assert.True(guard.TryEnter());
        Assert.False(guard.TryEnter());
        Assert.False(guard.TryEnter());
    }

    [Fact]
    public void ExitReopensTheGate()
    {
        var guard = new ReentryGuard();

        Assert.True(guard.TryEnter());
        guard.Exit();

        Assert.True(guard.TryEnter());
    }

    [Fact]
    public void ExitBeforeEverEnteringIsHarmless()
    {
        var guard = new ReentryGuard();

        guard.Exit();

        Assert.True(guard.TryEnter());
    }

    /// <summary>
    /// The actual shape of the original bug: many concurrent callers racing
    /// the check-then-assign gap. Exactly one may hold the gate at a time.
    /// </summary>
    [Fact]
    public void OnlyOneOfManyConcurrentCallersGetsIn()
    {
        var guard = new ReentryGuard();
        int admitted = 0;

        Parallel.For(0, 200, _ =>
        {
            if (guard.TryEnter()) Interlocked.Increment(ref admitted);
        });

        Assert.Equal(1, admitted);
    }
}
