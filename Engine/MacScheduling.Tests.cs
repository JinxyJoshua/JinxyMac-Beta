using Xunit;

namespace JinxyMac.Engine.Tests;

/// <summary>
/// The macOS side of keeping the clicks on time while the game is in front.
/// </summary>
/// <remarks>
/// The calls themselves only do anything on a Mac, and this runs on a PC, so
/// what is pinned here is what can be checked anywhere: the activity flags
/// asked for, and that every entry point is a safe no-op off macOS. The flags
/// matter most. Asking for too little leaves App Nap on; asking for too much
/// keeps someone's Mac from ever going to sleep while Jinxy is open.
/// </remarks>
public class MacSchedulingTests
{
    [Fact]
    public void AsksForLatencyCriticalTiming()
    {
        // NSActivityLatencyCritical. The flag that keeps timers precise, which
        // is the part of App Nap that delays the click loop's sleeps.
        const ulong latencyCritical = 0xFF00000000UL;

        Assert.Equal(latencyCritical, MacScheduling.ActivityOptions & latencyCritical);
    }

    [Fact]
    public void AsksForUserInitiatedWork()
    {
        // The user-initiated bits bar the idle-system-sleep one. Having them is
        // what tells macOS this is not background work to be throttled.
        const ulong userInitiatedLow = 0x000FFFFFUL;

        Assert.Equal(userInitiatedLow, MacScheduling.ActivityOptions & userInitiatedLow);
    }

    [Fact]
    public void DoesNotStopTheMacFromSleeping()
    {
        // NSActivityIdleSystemSleepDisabled and NSActivityIdleDisplaySleepDisabled.
        // The activity is held for as long as Jinxy is open, and an app sitting
        // idle in the background has no business keeping the machine awake.
        const ulong idleSystemSleepDisabled = 1UL << 20;
        const ulong idleDisplaySleepDisabled = 1UL << 40;

        Assert.Equal(0UL, MacScheduling.ActivityOptions & idleSystemSleepDisabled);
        Assert.Equal(0UL, MacScheduling.ActivityOptions & idleDisplaySleepDisabled);
    }

    [Fact]
    public void AsksForTheUserInteractiveThreadClass()
    {
        // QOS_CLASS_USER_INTERACTIVE: highest ordinary class, kept on the
        // performance cores on Apple silicon.
        Assert.Equal(0x21u, MacScheduling.UserInteractiveQos);
    }

    [Fact]
    public void IsANoOpOffMacOs()
    {
        if (OperatingSystem.IsMacOS()) return;

        Assert.False(MacScheduling.KeepAppAwake());
        Assert.False(MacScheduling.MakeCurrentThreadInteractive());
    }
}
