using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The wait a rotation is built out of.
/// </summary>
/// <remarks>
/// A dwell is only worth computing if it is then actually taken. Thread.Sleep
/// returns when the scheduler next gets round to it, so a dwell slept in slices
/// compounds every overshoot — the 150 ms crossbow dip measured off the hand
/// this copies was arriving nearer 180 ms, and the swing it was supposed to
/// leave room for never happened.
///
/// These are timing tests and therefore the flakiest in the suite. The bounds
/// are deliberately loose: they are sized to catch a sliced sleep coming back,
/// not to pin down the exact microsecond.
/// </remarks>
public class KeyMacroWaitTests
{
    /// <summary>
    /// Raised the system timer for the duration, on the Windows build that
    /// no longer exists.
    /// </summary>
    /// <remarks>
    /// The ported MacroRunner does not raise the timer or touch background
    /// throttling — both were Win32-specific (winmm's timeBeginPeriod and a
    /// Windows-only process throttling API) and were deliberately not carried
    /// across the port. So on every platform this test now runs on, a sleep
    /// lands on whatever the scheduler's default tick is.
    ///
    /// That is fine here: macOS sleeps at roughly 1 ms granularity already,
    /// without needing anything raised, so this test's premise — that the
    /// wait does not accumulate drift across many sleeps — holds without this
    /// type doing anything. It is kept, guarded to be a no-op off Windows, so
    /// the test still reads as measuring the same thing it always has.
    /// </remarks>
    private sealed class RaisedTimer : IDisposable
    {
        private readonly bool _raised;

        public RaisedTimer() => _raised = OperatingSystem.IsWindows() && TimeBeginPeriod(1) == 0;

        public void Dispose() { if (_raised) TimeEndPeriod(1); }

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint period);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint period);
    }

    /// <summary>A wait that returns early is a key sent before the equip finished.</summary>
    [Fact]
    public void NeverReturnsEarly()
    {
        var clock = Stopwatch.StartNew();

        MacroRunner.Wait(50, CancellationToken.None);

        Assert.True(clock.Elapsed.TotalMilliseconds >= 49.0,
            $"returned after {clock.Elapsed.TotalMilliseconds:F1}ms, before the 50ms asked for");
    }

    /// <summary>
    /// The regression this exists for. Ten 20 ms waits is 200 ms of intent; the
    /// sliced version drifted to 240 ms and beyond, and that drift is what made
    /// the rotation slower than switching by hand.
    /// </summary>
    [Fact]
    public void DoesNotAccumulateDriftAcrossManyWaits()
    {
        using var timer = new RaisedTimer();

        var clock = Stopwatch.StartNew();

        for (int i = 0; i < 10; i++) MacroRunner.Wait(20, CancellationToken.None);

        double elapsed = clock.Elapsed.TotalMilliseconds;

        Assert.True(elapsed < 225.0,
            $"ten 20ms waits took {elapsed:F1}ms; 200ms was asked for and a sliced sleep is back");
    }

    /// <summary>
    /// A stop has to be felt during the long half of a rotation, not after it.
    /// </summary>
    [Fact]
    public void GivesUpPromptlyWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(20);

        var clock = Stopwatch.StartNew();
        bool completed = MacroRunner.Wait(2000, cts.Token);

        Assert.False(completed);
        Assert.True(clock.Elapsed.TotalMilliseconds < 200.0,
            $"took {clock.Elapsed.TotalMilliseconds:F1}ms to notice a stop during a 2s wait");
    }

    [Fact]
    public void ReportsCompletionWhenNotCancelled()
    {
        Assert.True(MacroRunner.Wait(5, CancellationToken.None));
    }

    /// <summary>
    /// A zero dwell is reachable through per-key holds, and must not spin.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ReturnsAtOnceForNothingToWait(double ms)
    {
        var clock = Stopwatch.StartNew();

        Assert.True(MacroRunner.Wait(ms, CancellationToken.None));
        Assert.True(clock.Elapsed.TotalMilliseconds < 10.0);
    }

    [Fact]
    public void ReportsCancellationEvenWithNothingToWait()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.False(MacroRunner.Wait(0, cts.Token));
    }
}
