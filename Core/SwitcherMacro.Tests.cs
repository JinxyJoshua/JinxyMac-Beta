using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Building the switcher's macro from its five settings — the one piece of
/// this feature that is plain arithmetic and text, and so belongs in Core
/// rather than in the page that reads the boxes.
/// </summary>
public class SwitcherMacroTests
{
    [Fact]
    public void BuildsBothSlotsInOrder()
    {
        SwitcherMacro.Result result = SwitcherMacro.Build("3", "1", 150, 900, 60, 100);

        Assert.NotNull(result.Macro);
        Assert.Null(result.Error);
        Assert.Equal("3, 1", result.Macro!.KeysText);
        Assert.Equal(2, result.Macro.Keys.Length);
    }

    [Fact]
    public void NameIsReservedAndUnreachableByATypedMacroName()
    {
        // Leading space: MacroStore.ParseKeys and the macros page both trim
        // what a user types before comparing or saving it, so nothing typed
        // can ever produce this exact string.
        Assert.StartsWith(" ", SwitcherMacro.Name);
        Assert.Equal(" AutoSwitcher", SwitcherMacro.Name);
    }

    [Theory]
    [InlineData("", "1")]
    [InlineData("3", "")]
    [InlineData("33", "1")]
    [InlineData("!", "1")]
    public void RefusesWhenASlotIsNotExactlyOneLetterOrDigit(string slotA, string slotB)
    {
        SwitcherMacro.Result result = SwitcherMacro.Build(slotA, slotB, 150, 900, 60, 100);

        Assert.Null(result.Macro);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// A is one letter, same as any other — "Both slots need one letter or
    /// digit" would be untrue of it specifically. Whichever slot names A gets
    /// MacroStore's own explanation instead, the same one the Macros page
    /// gives for a typed A.
    /// </summary>
    [Theory]
    [InlineData("A", "1")]
    [InlineData("3", "a")]
    public void NamingTheASlotExplainsWhyRatherThanBlamingTheFormat(string slotA, string slotB)
    {
        SwitcherMacro.Result result = SwitcherMacro.Build(slotA, slotB, 150, 900, 60, 100);

        Assert.Null(result.Macro);
        Assert.Equal(MacroStore.UnbindableAMessage, result.Error);
    }

    [Theory]
    [InlineData(0, 900)]
    [InlineData(150, 0)]
    [InlineData(KeyMacro.MaxIntervalMs + 1, 900)]
    public void RefusesHoldsOutsideTheAllowedRange(int holdFirst, int holdSecond)
    {
        SwitcherMacro.Result result = SwitcherMacro.Build("3", "1", holdFirst, holdSecond, 60, 100);

        Assert.Null(result.Macro);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// A hold shorter than the equip plus two click periods would draw the
    /// weapon and swap away again before it ever fired — the same floor
    /// <see cref="KeyMacro.MinimumDwellMs"/> exists to enforce, applied here
    /// rather than trusted to have already been applied to the typed value.
    /// </summary>
    [Fact]
    public void RaisesAFirstHoldThatIsTooShortToFire()
    {
        // At 100ms a click, two clicks plus a 60ms equip need at least
        // 60 + 200 = 260ms — comfortably above the 10ms asked for.
        SwitcherMacro.Result result = SwitcherMacro.Build("3", "1", 10, 900, 60, 100);

        Assert.NotNull(result.Macro);
        Assert.True(result.Raised);
        Assert.True(result.FirstHoldMs >= 260);
        Assert.Equal(result.FirstHoldMs, result.Macro!.HoldsMs![0]);
    }

    [Fact]
    public void DoesNotRaiseAFirstHoldThatAlreadyClearsTheFloor()
    {
        SwitcherMacro.Result result = SwitcherMacro.Build("3", "1", 150, 900, 60, 10);

        Assert.False(result.Raised);
        Assert.Equal(150, result.FirstHoldMs);
    }

    [Fact]
    public void KeepsTheSecondHoldExactlyAsTyped()
    {
        SwitcherMacro.Result result = SwitcherMacro.Build("3", "1", 150, 900, 60, 10);

        Assert.Equal(900, result.Macro!.HoldsMs![1]);
    }

    [Fact]
    public void SendsTwoClicksBeforeMovingOn()
    {
        SwitcherMacro.Result result = SwitcherMacro.Build("3", "1", 150, 900, 60, 10);

        Assert.Equal(SwitcherMacro.ClicksWanted, result.Macro!.ClicksWanted);
        Assert.Equal(60, result.Macro.EquipMs);
    }
}
