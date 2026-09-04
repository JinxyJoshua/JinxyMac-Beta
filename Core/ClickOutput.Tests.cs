using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The arithmetic behind the sentence under the measured rate. Worth pinning
/// down: it is the only place the app tells someone their setting is not what
/// is being sent, and getting it backwards would recommend the losing setting.
/// </summary>
public class ClickOutputTests
{
    [Fact]
    public void NotRunning_IsIdle()
    {
        Assert.Equal(OutputState.Idle, ClickOutput.Classify(running: false, 100, 33));
    }

    [Fact]
    public void NaN_IsRefusedRatherThanTreatedAsShortfall()
    {
        Assert.Equal(OutputState.Idle, ClickOutput.Classify(true, double.NaN, 33));
        Assert.Equal(OutputState.Idle, ClickOutput.Classify(true, 100, double.NaN));
    }

    [Fact]
    public void FarBelowTheSetting_IsShortfall()
    {
        Assert.Equal(OutputState.Shortfall, ClickOutput.Classify(true, setCps: 193.62, deliveredCps: 33.3));
    }

    [Fact]
    public void FarBelowTheSetting_WithHitFixHolding_NamesHitFix()
    {
        Assert.Equal(
            OutputState.ClampedByHitFix,
            ClickOutput.Classify(true, setCps: 193.62, deliveredCps: 33.3, hitFixClamping: true));
    }

    [Fact]
    public void MeetingAnAskAboveTheThreshold_IsOverDriven()
    {
        Assert.Equal(OutputState.OverDriven, ClickOutput.Classify(true, setCps: 50, deliveredCps: 50));
    }

    [Fact]
    public void MeetingAnAskInsideTheThreshold_IsMatching()
    {
        Assert.Equal(OutputState.Matching, ClickOutput.Classify(true, 33.3, 33.3));
    }

    /// <summary>
    /// Measurement is a difference of click counts over a wall-clock second, so
    /// it jitters by a click either way. Inside the tolerance is not a shortfall.
    /// </summary>
    [Fact]
    public void SlightlyUnderTheSetting_IsNotAShortfall()
    {
        Assert.Equal(OutputState.Matching, ClickOutput.Classify(true, setCps: 33.0, deliveredCps: 32.0));
    }

    [Fact]
    public void ZeroSetting_DoesNotDivideOrReportShortfall()
    {
        Assert.Equal(OutputState.Matching, ClickOutput.Classify(true, setCps: 0, deliveredCps: 0));
    }

    [Fact]
    public void ShortfallVerdict_NamesBothNumbers()
    {
        string verdict = ClickOutput.Verdict(OutputState.Shortfall, 193.62, 33.3);

        Assert.Contains("193.6", verdict);
        Assert.Contains("33.3", verdict);
    }

    [Fact]
    public void OverDrivenVerdict_CitesTheMeasuredRate()
    {
        string verdict = ClickOutput.Verdict(OutputState.OverDriven, 193.62, 193.62);

        Assert.Contains("33", verdict);
        Assert.Contains("34", verdict);

        // The literals above live in the format string and would pass for any
        // input. This is the assertion that proves the argument is read.
        Assert.Contains("193.6", verdict);
    }

    [Fact]
    public void OnlyTheStatesAskingForAChangeAreAccented()
    {
        Assert.True(ClickOutput.IsWarning(OutputState.Shortfall));
        Assert.True(ClickOutput.IsWarning(OutputState.OverDriven));
        Assert.True(ClickOutput.IsWarning(OutputState.ClampedByHitFix));
        Assert.False(ClickOutput.IsWarning(OutputState.Matching));
        Assert.False(ClickOutput.IsWarning(OutputState.Idle));
    }
}
