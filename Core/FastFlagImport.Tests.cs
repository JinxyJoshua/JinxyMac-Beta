using System;
using System.Linq;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Reading somebody's pasted flags, and saying which of them do anything.
/// </summary>
/// <remarks>
/// The verdict is the feature, so most of these are about the sorting rather
/// than the parsing. A preset off a forum is usually mostly inert since Roblox's
/// allowlist landed, and this is the only place a user finds that out.
/// </remarks>
public class FastFlagImportTests
{
    // ---- the verdict ----

    /// <summary>
    /// The case the whole thing exists for: a real preset, part working and
    /// part dead, has to come apart into the two piles.
    /// </summary>
    [Fact]
    public void AllowedFlagsAreKeptAndIgnoredOnesAreNamed()
    {
        FlagImport result = FastFlagImport.Read("""
        {
          "DFIntTextureQualityOverride": "0",
          "DFIntTaskSchedulerTargetFps": "9999",
          "FFlagSomethingInvented": "True"
        }
        """);

        Assert.Null(result.Error);

        Assert.Equal(new[] { "DFIntTextureQualityOverride" },
            result.Supported.Select(f => f.Name));

        Assert.Equal(
            new[] { "DFIntTaskSchedulerTargetFps", "FFlagSomethingInvented" },
            result.Ignored.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// The flag this app shipped for a year and that does nothing. Importing a
    /// preset containing it must not quietly reinstate it.
    /// </summary>
    [Fact]
    public void TheDeadFrameRateFlagIsNotImported()
    {
        FlagImport result = FastFlagImport.Read("""{"DFIntTaskSchedulerTargetFps":"9999"}""");

        Assert.Empty(result.Supported);
        Assert.Contains("DFIntTaskSchedulerTargetFps", result.Ignored);
    }

    /// <summary>
    /// Every flag on Roblox's own list is importable — otherwise the feature
    /// would be refusing things that work.
    /// </summary>
    [Fact]
    public void EveryAllowlistedFlagCanBeImported()
    {
        string json = "{"
            + string.Join(",", RobloxFastFlags.Allowlist.Select(n => $"\"{n}\":\"1\""))
            + "}";

        FlagImport result = FastFlagImport.Read(json);

        Assert.Empty(result.Ignored);
        Assert.Empty(result.Unreadable);
        Assert.Equal(RobloxFastFlags.Allowlist.Count, result.Supported.Count);
    }

    // ---- flags that should not be written at all ----

    /// <summary>
    /// The category that gets accounts banned, and the one every "god mode"
    /// preset is built from. Named separately from ordinary unsupported flags,
    /// because it says something about where the preset came from.
    /// </summary>
    [Theory]
    [InlineData("DFIntHumanoidWalkSpeed")]
    [InlineData("FFlagDebugGravityOverride")]
    [InlineData("DFIntPhysicsStepTime")]
    [InlineData("FFlagJumpPowerOverride")]
    [InlineData("DFIntVelocityClamp")]
    public void FlagsThatTouchMovementAreRefusedRatherThanIgnored(string name)
    {
        FlagImport result = FastFlagImport.Read($"{{\"{name}\":\"100\"}}");

        Assert.Contains(name, result.Risky);
        Assert.Empty(result.Supported);
        Assert.DoesNotContain(name, result.Ignored);
    }

    /// <summary>
    /// None of them are on the allowlist anyway, so refusing them costs nothing
    /// that would otherwise have worked.
    /// </summary>
    [Fact]
    public void NothingOnTheAllowlistLooksRisky()
    {
        Assert.DoesNotContain(RobloxFastFlags.Allowlist, FastFlagImport.LooksRisky);
    }

    // ---- what arrives is not always tidy ----

    [Fact]
    public void APasteWithProseAroundItStillWorks()
    {
        FlagImport result = FastFlagImport.Read("""
        yo use this preset its insane +200 fps

        ```json
        { "DFIntTextureQualityOverride": "0" }
        ```

        trust me
        """);

        Assert.Null(result.Error);
        Assert.Single(result.Supported);
    }

    [Fact]
    public void CommentsAndATrailingCommaAreForgiven()
    {
        FlagImport result = FastFlagImport.Read("""
        {
          // the big one
          "DFIntTextureQualityOverride": "0",
          /* and this */
          "FIntDebugForceMSAASamples": "0",
        }
        """);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Supported.Count);
    }

    /// <summary>
    /// Presets are written by hand and arrive with real booleans and bare
    /// numbers, though Roblox's file is all strings. A boolean has to come out
    /// as "True" with the capital, which is the spelling the client reads.
    /// </summary>
    [Fact]
    public void RealBooleansAndNumbersBecomeTheStringsRobloxExpects()
    {
        FlagImport result = FastFlagImport.Read("""
        {
          "DFFlagDisableDPIScale": true,
          "DFFlagTextureQualityOverrideEnabled": false,
          "DFIntTextureQualityOverride": 0
        }
        """);

        Assert.Equal("True", Value(result, "DFFlagDisableDPIScale"));
        Assert.Equal("False", Value(result, "DFFlagTextureQualityOverrideEnabled"));
        Assert.Equal("0", Value(result, "DFIntTextureQualityOverride"));
    }

    [Fact]
    public void AQuotedValueKeepsItsOwnSpelling()
    {
        FlagImport result = FastFlagImport.Read("""{"DFFlagDisableDPIScale":"True"}""");

        Assert.Equal("True", Value(result, "DFFlagDisableDPIScale"));
    }

    [Fact]
    public void SurroundingWhitespaceInAValueIsTrimmed()
    {
        FlagImport result = FastFlagImport.Read("""{"DFIntTextureQualityOverride":"  0  "}""");

        Assert.Equal("0", Value(result, "DFIntTextureQualityOverride"));
    }

    /// <summary>Last one wins, which is what any JSON reader does.</summary>
    [Fact]
    public void ARepeatedFlagTakesItsLastValue()
    {
        FlagImport result = FastFlagImport.Read("""
        {
          "DFIntTextureQualityOverride": "3",
          "DFIntTextureQualityOverride": "0"
        }
        """);

        Assert.Equal("0", Value(result, "DFIntTextureQualityOverride"));
        Assert.Single(result.Supported);
    }

    // ---- things that are not flags ----

    [Fact]
    public void KeysThatAreNotFlagNamesAreReportedAsUnreadable()
    {
        FlagImport result = FastFlagImport.Read("""
        {
          "DFIntTextureQualityOverride": "0",
          "note": "found this on youtube",
          "Version": "1.2"
        }
        """);

        Assert.Single(result.Supported);
        Assert.Equal(new[] { "Version", "note" }, result.Unreadable.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// Another launcher's settings file, with the flags nested inside it. The
    /// wrapper's name is reported rather than its contents guessed at.
    /// </summary>
    [Fact]
    public void ANestedObjectIsNotTreatedAsAFlag()
    {
        FlagImport result = FastFlagImport.Read("""
        { "FFlagWrapper": { "DFIntTextureQualityOverride": "0" } }
        """);

        Assert.Empty(result.Supported);
        Assert.Contains("FFlagWrapper", result.Unreadable);
    }

    /// <summary>
    /// A flag set to nothing would be written into Roblox's file as an empty
    /// value, which is worse than not importing it.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("[1,2]")]
    public void AFlagWithNoRealValueIsNotImported(string value)
    {
        FlagImport result = FastFlagImport.Read(
            $"{{\"DFIntTextureQualityOverride\":{value}}}");

        Assert.Empty(result.Supported);
        Assert.Contains("DFIntTextureQualityOverride", result.Unreadable);
    }

    // ---- nothing usable at all ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void AnEmptyPasteAsksForFlags(string? text)
    {
        FlagImport result = FastFlagImport.Read(text);

        Assert.NotNull(result.Error);
        Assert.False(result.AnythingToApply);
    }

    [Fact]
    public void TextWithNoBracesIsNotFlags()
    {
        FlagImport result = FastFlagImport.Read("just turn your graphics down lol");

        Assert.NotNull(result.Error);
        Assert.Contains("{ }", result.Error!);
    }

    [Fact]
    public void BrokenJsonSaysSoRatherThanFailingSilently()
    {
        FlagImport result = FastFlagImport.Read("""{ "DFIntTextureQualityOverride": }""");

        Assert.NotNull(result.Error);
        Assert.Contains("valid JSON", result.Error!);
    }

    [Fact]
    public void AnEmptyObjectIsNotFlagsEither()
    {
        Assert.NotNull(FastFlagImport.Read("{}").Error);
    }

    /// <summary>
    /// A paste of thousands is somebody's whole settings dump, and writing it
    /// would bury Roblox's file in entries nobody chose.
    /// </summary>
    [Fact]
    public void AnAbsurdlyLongPasteIsRefused()
    {
        string json = "{"
            + string.Join(",", Enumerable.Range(0, FastFlagImport.MaxFlags + 1)
                .Select(i => $"\"FFlagMade{i}\":\"True\""))
            + "}";

        FlagImport result = FastFlagImport.Read(json);

        Assert.NotNull(result.Error);
        Assert.Contains(FastFlagImport.MaxFlags.ToString(), result.Error!);
        Assert.False(result.AnythingToApply);
    }

    // ---- the sentence shown on the page ----

    [Fact]
    public void TheSummaryCountsEachKind()
    {
        FlagImport result = FastFlagImport.Read("""
        {
          "DFIntTextureQualityOverride": "0",
          "FIntDebugForceMSAASamples": "0",
          "DFIntTaskSchedulerTargetFps": "9999",
          "DFIntHumanoidWalkSpeed": "100",
          "note": "hi"
        }
        """);

        Assert.Equal(
            "2 Roblox will use, 1 it ignores, 1 refused and 1 unreadable.",
            result.Summary);
    }

    [Fact]
    public void AllGoodReadsAsOneClause()
    {
        FlagImport result = FastFlagImport.Read("""{"DFIntTextureQualityOverride":"0"}""");

        Assert.Equal("1 Roblox will use.", result.Summary);
    }

    [Fact]
    public void TwoKindsReadAsAPair()
    {
        FlagImport result = FastFlagImport.Read("""
        { "DFIntTextureQualityOverride": "0", "DFIntTaskSchedulerTargetFps": "1" }
        """);

        Assert.Equal("1 Roblox will use and 1 it ignores.", result.Summary);
    }

    /// <summary>The failure reason is the summary, rather than a count of nothing.</summary>
    [Fact]
    public void AFailedImportShowsWhy()
    {
        FlagImport result = FastFlagImport.Read("nonsense");

        Assert.Equal(result.Error, result.Summary);
    }

    private static string Value(FlagImport result, string name) =>
        result.Supported.Single(f => f.Name == name).Value;
}
