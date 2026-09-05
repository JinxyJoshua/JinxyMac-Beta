using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The letter/digit -&gt; platform key code table, and the macOS table
/// specifically, proven here rather than only on hardware nobody on this
/// project has on their desk.
/// </summary>
public class KeyCodesTests
{
    /// <summary>
    /// Mirrors Engine/MacHotkeyWatcher.cs's Name(int) switch, inverted. '5'
    /// and '6' are asserted separately because the hardware really does swap
    /// them — a copy-paste or an "obvious" formula would get exactly these two
    /// wrong while everything else happened to look right.
    /// </summary>
    [Theory]
    [InlineData('1', 18)]
    [InlineData('2', 19)]
    [InlineData('R', 15)]
    [InlineData('5', 23)]
    [InlineData('6', 22)]
    public void MacTableMatchesMacHotkeyWatchersNameSwitch(char typed, int expected)
    {
        Assert.Equal(expected, KeyCodes.Mac(typed));
    }

    /// <summary>
    /// 'A' is kVK_ANSI_A, code 0 — no longer refused now that
    /// HotkeyBinding.Unbound's sentinel moved to -1 and stopped colliding
    /// with it.
    /// </summary>
    [Fact]
    public void MacTableGivesAItsRealCode()
    {
        Assert.Equal(0, KeyCodes.Mac('A'));
    }

    /// <summary>
    /// For('A') must dispatch to the macOS table's 0 when running on macOS —
    /// same pattern as ForReturnsTheRunningPlatformsOwnValue, computing the
    /// expected value from the platform actually running this suite rather
    /// than hard-coding an assumption a non-Mac CI machine would fail on.
    /// </summary>
    [Fact]
    public void ForGivesAItsMacCodeOfZeroOnMacOS()
    {
        int? expected = OperatingSystem.IsMacOS() ? 0 : (int)'A';

        Assert.Equal(expected, KeyCodes.For('A'));
    }

    [Fact]
    public void MacTableIsCaseInsensitive()
    {
        Assert.Equal(KeyCodes.Mac('R'), KeyCodes.Mac('r'));
    }

    [Fact]
    public void MacTableRefusesWhatItDoesNotCover()
    {
        Assert.Null(KeyCodes.Mac('!'));
    }

    /// <summary>
    /// Proves For() actually dispatches to the platform it is running on,
    /// rather than only ever exercising one branch in this suite.
    /// </summary>
    [Fact]
    public void ForReturnsTheRunningPlatformsOwnValue()
    {
        int? expected = OperatingSystem.IsMacOS() ? KeyCodes.Mac('R') : (int)'R';

        Assert.Equal(expected, KeyCodes.For('R'));
    }

    [Fact]
    public void ForIsCaseInsensitive()
    {
        Assert.Equal(KeyCodes.For('R'), KeyCodes.For('r'));
    }

    [Fact]
    public void ForRefusesWhatItDoesNotCover()
    {
        Assert.Null(KeyCodes.For('!'));
    }
}
