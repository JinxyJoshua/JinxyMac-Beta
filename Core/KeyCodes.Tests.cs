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
    /// 'A' is unrepresentable on macOS: its real code is 0, which is also
    /// HotkeyBinding.Unbound's sentinel for "no key" — see the remarks there.
    /// </summary>
    [Fact]
    public void MacTableRefusesAWhoseCodeCollidesWithUnbound()
    {
        Assert.Null(KeyCodes.Mac('A'));
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
