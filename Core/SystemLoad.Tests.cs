using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The arithmetic behind the CPU tile.
/// </summary>
/// <remarks>
/// Only the tick maths is here. What produces the ticks is a syscall on each
/// platform and one of them cannot be run from this machine, which is exactly
/// why the sum was pulled out of the interop and left testable.
/// </remarks>
public class SystemLoadTests
{
    [Fact]
    public void HalfTheTicksBusyIsFiftyPercent()
    {
        Assert.Equal(50, SystemLoad.BusyPercent(busy: 500, total: 1000, lastBusy: 0, lastTotal: 0));
    }

    /// <summary>
    /// The counters are cumulative, so the answer is about the interval, not the
    /// totals. A machine that has been up for a week and is idle now must read
    /// as idle now.
    /// </summary>
    [Fact]
    public void MeasuresTheIntervalRatherThanTheLifetime()
    {
        double? busy = SystemLoad.BusyPercent(
            busy: 9_000_100, total: 10_001_000,
            lastBusy: 9_000_000, lastTotal: 10_000_000);

        Assert.Equal(10, busy!.Value, 1);
    }

    /// <summary>
    /// The first reading has nothing to subtract. Reporting zero would draw an
    /// idle machine; reporting nothing leaves the tile at "--" for one second.
    /// </summary>
    [Fact]
    public void SaysNothingWithoutAPreviousSample()
    {
        Assert.Null(SystemLoad.BusyPercent(busy: 0, total: 0, lastBusy: 0, lastTotal: 0));
    }

    /// <summary>A counter that goes backwards has wrapped; it has not gone idle.</summary>
    [Fact]
    public void SaysNothingWhenTheCountersGoBackwards()
    {
        Assert.Null(SystemLoad.BusyPercent(busy: 10, total: 100, lastBusy: 50, lastTotal: 90));
        Assert.Null(SystemLoad.BusyPercent(busy: 100, total: 50, lastBusy: 50, lastTotal: 90));
    }

    [Fact]
    public void NeverReportsMoreThanAFullMachine()
    {
        // Busy climbing faster than total should be impossible, but a clamp is
        // cheaper than a tile reading 400%.
        Assert.Equal(100, SystemLoad.BusyPercent(busy: 5000, total: 1000, lastBusy: 0, lastTotal: 0));
    }

    [Fact]
    public void FormatsWhatTheTilesShow()
    {
        var load = new MachineLoad(37.4, UsedBytes: 8UL * 1024 * 1024 * 1024, TotalBytes: 16UL * 1024 * 1024 * 1024);

        Assert.Equal("37%", load.CpuText);
        Assert.Equal("8.0 GB", load.RamText);
    }

    [Fact]
    public void FormatsAnUnknownReadingAsDashes()
    {
        var load = new MachineLoad(null, 0, 0);

        Assert.Equal("--%", load.CpuText);
        Assert.Equal("-- GB", load.RamText);
    }
}
