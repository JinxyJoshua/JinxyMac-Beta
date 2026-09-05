using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Which button gets pressed, and what it is called on screen. Small, but the
/// parse is what a hand-edited settings file lands in, and the wrong answer
/// there is a clicker that presses nothing.
/// </summary>
public class ClickButtonTests
{
    [Theory]
    [InlineData(ClickButton.Left, "Left")]
    [InlineData(ClickButton.Right, "Right")]
    [InlineData(ClickButton.Middle, "Wheel")]
    public void EachButtonHasItsOwnName(ClickButton button, string expected)
    {
        Assert.Equal(expected, ClickButtons.Label(button));
    }

    [Theory]
    [InlineData("Left", ClickButton.Left)]
    [InlineData("Right", ClickButton.Right)]
    [InlineData("Middle", ClickButton.Middle)]
    [InlineData("right", ClickButton.Right)]
    [InlineData("MIDDLE", ClickButton.Middle)]
    public void ANameRoundTrips(string name, ClickButton expected)
    {
        Assert.Equal(expected, ClickButtons.Parse(name));
    }

    /// <summary>
    /// A settings file naming a button that does not exist should leave a
    /// working clicker, not one that presses nothing.
    /// </summary>
    [Theory]
    [InlineData("Thumb")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("4")]
    [InlineData("99")]
    public void AnythingUnrecognisedIsTheLeftButton(string? name)
    {
        Assert.Equal(ClickButton.Left, ClickButtons.Parse(name));
    }

    [Fact]
    public void EveryButtonSurvivesItsOwnLabelBeingParsedBack()
    {
        foreach (ClickButton button in Enum.GetValues<ClickButton>())
        {
            // "Wheel" is a display name, not the stored one, so the round trip
            // is through the enum name — which is what actually gets persisted.
            Assert.Equal(button, ClickButtons.Parse(button.ToString()));
        }
    }
}
