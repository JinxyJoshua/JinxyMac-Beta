# JinxyMac 1.1.0 — Click Output, Remote Config, Wallpaper Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the click-output verdict, right/middle/wheel buttons, remote config, wallpaper and the build-path privacy fix from JinxyClicker (Windows) into JinxyMac.

**Architecture:** Portable logic lands in `Core/` and is covered by tests that run on Windows. Anything platform-specific goes behind the existing `IClickEngine` seam, which gains a button parameter — so both the Mac and Windows implementations change together and the app stays runnable on a PC.

**Tech Stack:** C#, .NET 10 (`net10.0`), Avalonia 11.3, xunit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-09-04-jinxymac-timing-and-click-output-design.md`

## Global Constraints

- Target framework is `net10.0` with no `-windows` suffix. Never add WPF.
- Namespaces: `JinxyMac.Core`, `JinxyMac.Engine`. Tests: `JinxyMac.Core.Tests`.
- `Testing/JinxyMac.Tests.csproj` sets `EnableDefaultCompileItems=false`. **Every** new source and test file must be added to it by hand or it will not compile.
- Tests live beside the code they cover, named `<Thing>.Tests.cs`. The app csproj excludes `**/*.Tests.cs`.
- Nothing in this feature set may crash the app. Every file, network and interop call is wrapped and falls back to a shipped default.
- `RemoteConfig` clamps every value **in the app**, never in the file. It may move numbers within shipped bounds and turn features off. Nothing else.
- The version constant lives only in `Core/Updater.cs`; `Packaging/build-mac.sh` stamps it into `Info.plist`.
- Build must stay at **0 warnings**.
- Commit messages end with: `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`

---

### Task 1: Stop the build path shipping inside the binary

The shipped 1.0.8 bundle carries `C:\Users\rschi\_dev\Joshua\JinxyMac\obj\Release\...\JinxyMac.pdb` in both architecture DLLs — the developer's account name in a public download. Windows fixed this in `d821eb6`. No test framework covers a csproj, so the verification is a grep of the built binary.

**Files:**
- Modify: `JinxyMac.csproj`

**Interfaces:**
- Consumes: nothing
- Produces: nothing — build configuration only

- [ ] **Step 1: Confirm the leak exists**

```bash
cd /c/Users/rschi/_dev/Joshua/JinxyMac
dotnet publish -c Release -r osx-arm64 --self-contained false -o /tmp/jm-before
grep -a -c 'rschi' /tmp/jm-before/JinxyMac.dll
```

Expected: `1` — the leak is present.

- [ ] **Step 2: Add PathMap and Deterministic**

In `JinxyMac.csproj`, inside the existing first `<PropertyGroup>`, after `<AvaloniaUseCompiledBindingsByDefault>`:

```xml
    <!--
      The compiler records where it built, and that record ships. A release DLL
      carried the full build path in its CodeView entry — the folder name and
      the account name it was built under — readable in any hex editor by
      anyone who downloaded the tarball.

      PathMap rewrites those paths to a relative "." so nothing about this
      machine travels with the binary. Deterministic builds make the rewrite
      total: without it some paths are still recorded absolutely.
    -->
    <PathMap>$(MSBuildProjectDirectory)=.</PathMap>
    <Deterministic>true</Deterministic>
```

- [ ] **Step 3: Drop the debug record from Release**

Add a new `<PropertyGroup>` immediately after the first one closes:

```xml
  <!--
    Release ships no debug record at all. PathMap cleans the source paths, but
    the reference to the .pdb itself is still an absolute path to a file the
    user does not have — leaking the same two names to no benefit, since the
    symbols are not shipped either. Debug builds keep theirs so local
    debugging still works.
  -->
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <DebugType>none</DebugType>
    <DebugSymbols>false</DebugSymbols>
  </PropertyGroup>
```

- [ ] **Step 4: Verify the leak is gone in both architectures**

```bash
cd /c/Users/rschi/_dev/Joshua/JinxyMac
dotnet publish -c Release -r osx-arm64 --self-contained false -o /tmp/jm-arm64
dotnet publish -c Release -r osx-x64   --self-contained false -o /tmp/jm-x64
grep -a -c 'rschi'  /tmp/jm-arm64/JinxyMac.dll /tmp/jm-x64/JinxyMac.dll
grep -a -c 'Joshua' /tmp/jm-arm64/JinxyMac.dll /tmp/jm-x64/JinxyMac.dll
```

Expected: `0` for every line. A non-zero count means the property did not apply — check it is inside a `<PropertyGroup>` that is not conditioned away.

- [ ] **Step 5: Verify Debug still builds with symbols**

```bash
dotnet build -c Debug -v minimal
ls bin/Debug/net10.0/JinxyMac.pdb
```

Expected: build succeeds with 0 warnings, and the `.pdb` exists.

- [ ] **Step 6: Commit**

```bash
git add JinxyMac.csproj
git commit -m "$(cat <<'EOF'
Stop the build path shipping inside the binary

A release DLL carried the full build path in its CodeView entry - the
folder name and the account name it was built under - readable in any hex
editor by anyone who downloaded the tarball. Both architecture binaries in
the shipped 1.0.8 bundle have it.

PathMap rewrites those paths to a relative "." and Deterministic makes the
rewrite total. Release also drops the debug record entirely: the reference
to the .pdb is itself an absolute path to a file the user does not have,
leaking the same two names to no benefit when the symbols are not shipped.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: ClickOutput — say what is actually being sent

Verbatim port. Pure arithmetic and strings, no platform surface.

**Files:**
- Create: `Core/ClickOutput.cs`
- Create: `Core/ClickOutput.Tests.cs`
- Modify: `Testing/JinxyMac.Tests.csproj`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `enum OutputState { Idle, Shortfall, ClampedByHitFix, OverDriven, Matching }`
  - `ClickOutput.Classify(bool running, double setCps, double deliveredCps, bool hitFixClamping = false) -> OutputState`
  - `ClickOutput.Verdict(OutputState state, double setCps, double deliveredCps) -> string`
  - `ClickOutput.IsWarning(OutputState state) -> bool`
  - `ClickOutput.DiminishingReturnsCps = 36.0`, `MeasuredBestCps = 33.3`, `MismatchTolerance = 0.15`

- [ ] **Step 1: Write the failing tests**

Create `Core/ClickOutput.Tests.cs`:

```csharp
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
```

- [ ] **Step 2: Add both files to the test project**

In `Testing/JinxyMac.Tests.csproj`, inside the first `<ItemGroup>`, after the `ClickTiming` pair:

```xml
    <Compile Include="../Core/ClickOutput.cs" />
    <Compile Include="../Core/ClickOutput.Tests.cs" />
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Testing/JinxyMac.Tests.csproj --filter "FullyQualifiedName~ClickOutputTests"`
Expected: FAIL — `The type or namespace name 'OutputState' could not be found`.

- [ ] **Step 4: Write the implementation**

Create `Core/ClickOutput.cs`:

```csharp
namespace JinxyMac.Core;

/// <summary>How what is being delivered compares to what was asked for.</summary>
public enum OutputState
{
    /// <summary>Not clicking, so there is nothing to measure yet.</summary>
    Idle,

    /// <summary>Delivering materially less than the setting asks for.</summary>
    Shortfall,

    /// <summary>
    /// Short of the setting because HitFix's floors are holding it back.
    /// </summary>
    /// <remarks>
    /// Separate from a plain shortfall because the remedy is different, and
    /// pointing at the slider here would be wrong advice: HitFix caps every
    /// setting at the same rate, so lowering the slider changes nothing until
    /// it drops under that cap.
    /// </remarks>
    ClampedByHitFix,

    /// <summary>Delivering the setting, but the setting is past the point of use.</summary>
    OverDriven,

    /// <summary>Delivering the setting, inside a rate the server keeps up with.</summary>
    Matching
}

/// <summary>
/// The difference between the rate someone set and the rate that leaves the app.
/// </summary>
/// <remarks>
/// The sliders are a request. HitFix's floors, the duty cycle and the operating
/// system's scheduler all sit between that request and what the game receives,
/// and the gap is routinely large — a 193 CPS setting delivering 33 /s is the
/// ordinary case rather than a fault.
///
/// Left unsaid, the bigger number reads as the better setting, which is exactly
/// backwards: a run measured at 33.3 CPS landed 34 hits where a 193 CPS run
/// landed 33. This is the arithmetic behind saying so.
/// </remarks>
public static class ClickOutput
{
    /// <summary>
    /// Past this, more clicks stop becoming more hits.
    /// </summary>
    /// <remarks>
    /// Set from this app's own measurements, not from advice found online.
    ///
    /// Two runs, both recorded here: a profile delivering 33.3 clicks a second
    /// landed 34 hits, and one delivering about 193 landed 33. The slower one
    /// won. A personal best of 34 was then repeated at that same 33.3.
    ///
    /// A brief detour set this to 15, on the strength of community posts
    /// recommending 8 to 12 — those turned out to be about Minecraft's Bedwars,
    /// a different game with a different server. Following them would have told
    /// every user to abandon the only setting measured to work here. Kept as a
    /// note because the mistake is easy to repeat: this game's numbers have to
    /// come from this game.
    ///
    /// A little above 33.3 rather than exactly on it, so the configuration that
    /// produced the best result does not sit on the wrong side of its own
    /// warning.
    /// </remarks>
    public const double DiminishingReturnsCps = 36.0;

    /// <summary>The delivered rate measured to produce the best result.</summary>
    public const double MeasuredBestCps = 33.3;

    /// <summary>
    /// How far short delivery must fall before it is worth mentioning.
    /// </summary>
    /// <remarks>
    /// Measurement is a difference of click counts over a wall-clock second, so
    /// it jitters by a click either way at any rate. A tolerance stops the panel
    /// flickering between verdicts while nothing has actually changed.
    /// </remarks>
    public const double MismatchTolerance = 0.15;

    /// <param name="hitFixClamping">
    /// Whether HitFix's floors are what is holding the rate down, rather than
    /// the setting simply being unreachable for some other reason.
    /// </param>
    public static OutputState Classify(
        bool running, double setCps, double deliveredCps, bool hitFixClamping = false)
    {
        if (!running) return OutputState.Idle;

        // A nonsense reading is not evidence of a shortfall. NaN slips through
        // any comparison it is put in, so it is refused up front.
        if (double.IsNaN(deliveredCps) || double.IsNaN(setCps)) return OutputState.Idle;

        if (setCps > 0 && deliveredCps < setCps * (1.0 - MismatchTolerance))
            return hitFixClamping ? OutputState.ClampedByHitFix : OutputState.Shortfall;

        return setCps > DiminishingReturnsCps ? OutputState.OverDriven : OutputState.Matching;
    }

    /// <summary>The sentence shown under the delivered rate.</summary>
    public static string Verdict(OutputState state, double setCps, double deliveredCps) => state switch
    {
        OutputState.Idle =>
            "Start clicking to measure what the game actually receives.",

        OutputState.Shortfall =>
            $"Your {setCps:0.0} setting is really sending {deliveredCps:0.0}. "
            + "Lowering the slider until these match costs you nothing and makes the rate honest.",

        OutputState.ClampedByHitFix =>
            $"HitFix is holding this to {deliveredCps:0.0}, not the {setCps:0.0} you set — and that "
            + $"is the rate measured to land the most hits. Every setting above about "
            + $"{MeasuredBestCps:0} produces exactly this, so the slider above it changes nothing "
            + "but the number on it.",

        OutputState.OverDriven =>
            $"Every click is landing, but {setCps:0.0} is past the point where more clicks became "
            + $"more hits. Measured here: {MeasuredBestCps:0} a second landed 34, about 193 landed 33.",

        _ => "Matching the setting, inside the range the server turns into hits."
    };

    /// <summary>
    /// Whether the verdict is worth colouring. A line that is always lit stops
    /// being read, so only the states asking for a change are accented.
    /// </summary>
    public static bool IsWarning(OutputState state) =>
        state is OutputState.Shortfall or OutputState.OverDriven or OutputState.ClampedByHitFix;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Testing/JinxyMac.Tests.csproj --filter "FullyQualifiedName~ClickOutputTests"`
Expected: PASS, 11 tests.

- [ ] **Step 6: Commit**

```bash
git add Core/ClickOutput.cs Core/ClickOutput.Tests.cs Testing/JinxyMac.Tests.csproj
git commit -m "$(cat <<'EOF'
Say what is actually being sent, not what was asked for

The sliders are a request. HitFix's floors, the duty cycle and the
scheduler all sit between that request and what the game receives, and the
gap is routinely large - a 193 CPS setting delivering 33 a second is the
ordinary case rather than a fault. Unsaid, the bigger number reads as the
better setting, which is backwards: 33.3 delivered landed 34 hits where
193 landed 33.

The threshold is 36, just above the rate measured to work. A detour set it
to 15 on the strength of community posts recommending 8-12, which turned
out to describe Minecraft's Bedwars - a different game on a different
server. The reasoning is in the code because the mistake is easy to repeat.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: RemoteConfig — numbers fixable without a release

An unsigned Mac bundle makes every release expensive for users: the Accessibility grant goes stale and must be removed and re-added by hand. Correcting a timing floor by editing one file in a browser skips that.

**Files:**
- Create: `Core/RemoteConfig.cs`
- Create: `Core/RemoteConfig.Tests.cs`
- Create: `config.json` (repository root)
- Modify: `Core/Updater.cs` (add `Owner`/`Repo`, rebuild `Feed` from them)
- Modify: `Core/ClickTiming.cs` (floors read the config; delete the disproved model)
- Modify: `Testing/JinxyMac.Tests.csproj`

**Interfaces:**
- Consumes: nothing from earlier tasks
- Produces:
  - `RemoteConfig.Current -> RemoteConfig` (static, replaced once at startup)
  - `RemoteConfig.Parse(string json) -> RemoteConfig`
  - `RemoteConfig.LoadAsync(CancellationToken) -> Task`
  - `RemoteConfig.IsTrusted(string? url) -> bool`
  - `RemoteConfig.Url -> string`, `MaxNoticeLength = 200`
  - Instance: `HitFixMinDownMs`, `HitFixMinUpMs` (double), `RecorderEnabled` (bool), `Notice` (string)
  - `Updater.Owner = "JinxyJoshua"`, `Updater.Repo = "JinxyMac-Beta"`
  - `ClickTimings.DefaultHitFixMinDownMs = 15.0`, `DefaultHitFixMinUpMs = 15.0` (const)
  - `ClickTimings.HitFixMinDownMs`, `HitFixMinUpMs` become **static properties**, not consts

- [ ] **Step 1: Write the failing tests**

Create `Core/RemoteConfig.Tests.cs`:

```csharp
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// A file on the internet that changes how this app behaves. The bounds are
/// the whole safety story, so they are what gets pinned here: a hostile or
/// mistaken config must only ever be able to pick a value the app would have
/// accepted from its own settings screen.
/// </summary>
public class RemoteConfigTests
{
    [Fact]
    public void EmptyObject_KeepsEveryShippedDefault()
    {
        RemoteConfig c = RemoteConfig.Parse("{}");

        Assert.Equal(ClickTimings.DefaultHitFixMinDownMs, c.HitFixMinDownMs);
        Assert.Equal(ClickTimings.DefaultHitFixMinUpMs, c.HitFixMinUpMs);
        Assert.True(c.RecorderEnabled);
        Assert.Equal("", c.Notice);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    [InlineData("")]
    public void UnusableJson_FallsBackToDefaults(string json)
    {
        Assert.Equal(ClickTimings.DefaultHitFixMinDownMs, RemoteConfig.Parse(json).HitFixMinDownMs);
    }

    [Fact]
    public void ANumberInRange_IsTaken()
    {
        Assert.Equal(12.0, RemoteConfig.Parse("{\"hitFixMinDownMs\": 12}").HitFixMinDownMs);
    }

    [Fact]
    public void ANumberOutOfRange_IsClampedNotRejected()
    {
        Assert.Equal(100.0, RemoteConfig.Parse("{\"hitFixMinDownMs\": 9999}").HitFixMinDownMs);
        Assert.Equal(1.0, RemoteConfig.Parse("{\"hitFixMinDownMs\": -5}").HitFixMinDownMs);
    }

    [Fact]
    public void NaNAndInfinity_FallBackRatherThanPoisonTheTiming()
    {
        // JSON has no NaN literal, so these arrive as strings or as overflow.
        Assert.Equal(
            ClickTimings.DefaultHitFixMinDownMs,
            RemoteConfig.Parse("{\"hitFixMinDownMs\": \"NaN\"}").HitFixMinDownMs);
    }

    [Fact]
    public void AWrongType_KeepsTheDefault()
    {
        Assert.Equal(
            ClickTimings.DefaultHitFixMinDownMs,
            RemoteConfig.Parse("{\"hitFixMinDownMs\": \"twelve\"}").HitFixMinDownMs);
        Assert.True(RemoteConfig.Parse("{\"recorderEnabled\": \"no\"}").RecorderEnabled);
    }

    [Fact]
    public void AKeyNobodyRecognises_ChangesNothing()
    {
        RemoteConfig c = RemoteConfig.Parse("{\"somethingElse\": 5, \"hitFixMinUpMs\": 20}");

        Assert.Equal(20.0, c.HitFixMinUpMs);
        Assert.Equal(ClickTimings.DefaultHitFixMinDownMs, c.HitFixMinDownMs);
    }

    [Fact]
    public void RecorderCanBeSwitchedOff()
    {
        Assert.False(RemoteConfig.Parse("{\"recorderEnabled\": false}").RecorderEnabled);
    }

    [Fact]
    public void ALongNotice_IsTruncatedRatherThanShownWhole()
    {
        string json = "{\"notice\": \"" + new string('x', 500) + "\"}";

        Assert.Equal(RemoteConfig.MaxNoticeLength, RemoteConfig.Parse(json).Notice.Length);
    }

    [Fact]
    public void ANoticeIsTrimmed()
    {
        Assert.Equal("hello", RemoteConfig.Parse("{\"notice\": \"  hello  \"}").Notice);
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/a/b/main/config.json", true)]
    [InlineData("http://raw.githubusercontent.com/a/b/main/config.json", false)]
    [InlineData("https://example.com/config.json", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void OnlyRawGithubOverHttpsIsTrusted(string? url, bool expected)
    {
        Assert.Equal(expected, RemoteConfig.IsTrusted(url));
    }

    [Fact]
    public void TheShippedUrlIsOneThisAppTrusts()
    {
        Assert.True(RemoteConfig.IsTrusted(RemoteConfig.Url));
        Assert.Contains("JinxyMac-Beta", RemoteConfig.Url);
    }
}
```

- [ ] **Step 2: Add the files to the test project**

In `Testing/JinxyMac.Tests.csproj`, in the first `<ItemGroup>`:

```xml
    <Compile Include="../Core/RemoteConfig.cs" />
    <Compile Include="../Core/RemoteConfig.Tests.cs" />
```

`Core/Updater.cs` and `Core/ClickTiming.cs` are already listed.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Testing/JinxyMac.Tests.csproj --filter "FullyQualifiedName~RemoteConfigTests"`
Expected: FAIL — `RemoteConfig` does not exist.

- [ ] **Step 4: Expose Owner and Repo on Updater**

In `Core/Updater.cs`, replace the private `Feed` const (line ~40) with:

```csharp
    /// <summary>
    /// The repository this build updates from, and reads its config from.
    /// </summary>
    /// <remarks>
    /// Named once. Two places spelling out the same repository is two places
    /// to get it wrong, and the failure is silent — an updater pointed at the
    /// wrong repo simply never finds a release.
    /// </remarks>
    public const string Owner = "JinxyJoshua";
    public const string Repo = "JinxyMac-Beta";

    private const string Feed =
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
```

- [ ] **Step 5: Write RemoteConfig**

Create `Core/RemoteConfig.cs`:

```csharp
using System.Net.Http;
using System.Text.Json;

namespace JinxyMac.Core;

/// <summary>
/// Settings that can be changed without shipping a new build.
/// </summary>
/// <remarks>
/// <b>What this is for.</b> Some problems are a wrong number rather than wrong
/// code — the hit-fix floor being 15 ms when it should be 12. Those can be
/// fixed by editing one file on GitHub, and every copy picks it up next time it
/// opens. That is worth more here than on Windows: this app is unsigned, so
/// replacing the bundle makes it a new app to Accessibility and the user has to
/// go and re-grant it by hand. A release is expensive for them; an edit is not.
///
/// <b>What this is not for, and cannot do.</b> It cannot fix code. A crash, a
/// wrong calculation, a broken layout — none of those are a number, and no
/// amount of remote configuration reaches them. Anyone promising otherwise is
/// describing downloading and running new code on other people's machines,
/// which is indistinguishable from what malware does.
///
/// So the rule is strict and worth stating: <b>this file can only move numbers
/// within bounds the shipped build already agreed to, and turn features off.</b>
/// Every value is clamped by the app, not by the file.
///
/// Everything fails to the shipped defaults — no network, bad JSON, a value out
/// of range, a key nobody recognises. The app must work perfectly with this
/// whole mechanism unreachable, because for anyone offline it is.
/// </remarks>
public sealed class RemoteConfig
{
    /// <summary>Where the config lives — a plain file in the public repository.</summary>
    public static string Url =>
        $"https://raw.githubusercontent.com/{Updater.Owner}/{Updater.Repo}/main/config.json";

    public static bool IsTrusted(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>What is in force. Replaced once at startup, then read-only.</summary>
    public static RemoteConfig Current { get; private set; } = new();

    /// <summary>The shortest press the hit fix will allow, in milliseconds.</summary>
    public double HitFixMinDownMs { get; init; } = ClickTimings.DefaultHitFixMinDownMs;

    /// <summary>The shortest gap between clicks the hit fix will allow.</summary>
    public double HitFixMinUpMs { get; init; } = ClickTimings.DefaultHitFixMinUpMs;

    /// <summary>Whether the recorder may be used at all.</summary>
    /// <remarks>
    /// A switch rather than a number, for the case where the encoder turns out
    /// to be broken on machines nobody could test on. ffmpeg and Screen
    /// Recording have already produced two real bugs here, and turning the
    /// feature off beats leaving people with an app that fails every time they
    /// press Record.
    /// </remarks>
    public bool RecorderEnabled { get; init; } = true;

    /// <summary>
    /// A short line shown in the app, for saying "known issue, fix coming".
    /// </summary>
    /// <remarks>
    /// Length-capped so a mistake in the file cannot produce a wall of text in
    /// the interface, and it is only ever displayed as text — never parsed,
    /// never used as an address, never run.
    /// </remarks>
    public string Notice { get; init; } = "";

    public const int MaxNoticeLength = 200;

    /// <summary>
    /// Reads a config, clamping every value into a range the app accepts.
    /// </summary>
    /// <remarks>
    /// Clamped rather than validated-and-rejected: a single silly number should
    /// not throw away the rest of the file, and a value pinned to a bound is a
    /// value the app would have accepted anyway. Anything missing keeps the
    /// shipped default, so a config naming one setting changes only that one.
    /// </remarks>
    public static RemoteConfig Parse(string json)
    {
        var fallback = new RemoteConfig();

        try
        {
            JsonElement root = JsonDocument.Parse(json).RootElement;

            if (root.ValueKind != JsonValueKind.Object) return fallback;

            return new RemoteConfig
            {
                HitFixMinDownMs = Clamped(root, "hitFixMinDownMs", fallback.HitFixMinDownMs, 1, 100),
                HitFixMinUpMs = Clamped(root, "hitFixMinUpMs", fallback.HitFixMinUpMs, 1, 100),
                RecorderEnabled = Flag(root, "recorderEnabled", fallback.RecorderEnabled),
                Notice = Text(root, "notice")
            };
        }
        catch
        {
            return fallback;
        }
    }

    private static double Clamped(JsonElement root, string name, double fallback, double low, double high)
    {
        if (!root.TryGetProperty(name, out JsonElement value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number) return fallback;
        if (!value.TryGetDouble(out double number)) return fallback;
        if (double.IsNaN(number) || double.IsInfinity(number)) return fallback;

        return Math.Clamp(number, low, high);
    }

    private static bool Flag(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static string Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)) return "";
        if (value.ValueKind != JsonValueKind.String) return "";

        string text = (value.GetString() ?? "").Trim();

        return text.Length <= MaxNoticeLength ? text : text[..MaxNoticeLength];
    }

    /// <summary>
    /// Fetches the config and puts it in force. Never throws, never blocks
    /// anything that matters.
    /// </summary>
    public static async Task LoadAsync(CancellationToken token)
    {
        try
        {
            if (!IsTrusted(Url)) return;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("JinxyMac");

            Current = Parse(await http.GetStringAsync(Url, token).ConfigureAwait(false));
        }
        catch
        {
            // Offline, rate limited, or no config published. The shipped
            // defaults are already in force and are known to work.
        }
    }
}
```

- [ ] **Step 6: Route the HitFix floors through the config**

In `Core/ClickTiming.cs`, replace `public const double HitFixMinDownMs = 15.0;` and `public const double HitFixMinUpMs = 15.0;` with:

```csharp
    /// <summary>
    /// The shipped values, and the ones used unless a config says otherwise.
    /// </summary>
    /// <remarks>
    /// Kept separate from the values actually read so the defaults survive as
    /// facts: a remote config that goes missing, or arrives with nonsense in
    /// it, falls back to exactly the numbers this build was tested with.
    /// </remarks>
    public const double DefaultHitFixMinDownMs = 15.0;
    public const double DefaultHitFixMinUpMs = 15.0;

    /// <summary>
    /// What the timing actually uses.
    /// </summary>
    /// <remarks>
    /// Properties rather than constants because these are exactly the kind of
    /// number that turns out to be slightly wrong on hardware nobody could
    /// test — which, on macOS, is all of it.
    ///
    /// Bounded at the point the config is read, never here — this only ever
    /// sees a value the app already agreed to accept.
    /// </remarks>
    public static double HitFixMinDownMs => RemoteConfig.Current.HitFixMinDownMs;

    public static double HitFixMinUpMs => RemoteConfig.Current.HitFixMinUpMs;
```

- [ ] **Step 7: Delete the disproved frame model**

Still in `Core/ClickTiming.cs`, inside `Resolve`, replace this comment:

```csharp
            // A client reads input once a frame. At 60 fps that is every ~17ms,
            // so a press shorter than a frame can begin and end between two
            // reads and never be seen.
            //
            // Both edges need a read inside them, so the gap after the press
            // gets a floor too. A press with no observed release is a held
            // button, not a click.
```

with:

```csharp
            // The floors only ever need to keep the press and the gap from
            // collapsing to nothing, which is what actually broke at a 99% duty
            // cycle.
            //
            // Not because a client samples input once a frame — that model was
            // disproved. A competing clicker holds for 15.6ms, under a frame at
            // 60fps, and its presses register; this app's own measured profile
            // has a gap of a third of a frame and wins anyway. Roblox takes
            // mouse events off the message queue, where a short press is queued
            // and read like any other.
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test Testing/JinxyMac.Tests.csproj`
Expected: PASS. `ClickTimingTests` must still pass unchanged — the properties return the same 15.0 defaults when no config has loaded.

- [ ] **Step 9: Add config.json at the repository root**

Create `config.json`:

```json
{
  "_comment": "Live settings for JinxyMac. Edit this file on GitHub and every copy of the app picks it up next time it opens - no release, nobody re-downloading. That matters more here than on Windows: this app is unsigned, so installing an update costs the user their Accessibility grant. It can only change numbers and turn features off; it cannot change how the app works. Anything missing, misspelled or out of range falls back to the value the app shipped with, so a mistake here is harmless.",

  "_hitFixMinDownMs": "How long a click is held down, at minimum. 1-100, ships as 15.",
  "hitFixMinDownMs": 15,

  "_hitFixMinUpMs": "The minimum gap between clicks. 1-100, ships as 15.",
  "hitFixMinUpMs": 15,

  "_recorderEnabled": "Set false to switch the recorder off everywhere, if ffmpeg or Screen Recording turns out to be broken.",
  "recorderEnabled": true,

  "_notice": "A short line shown in the app - for saying a problem is known and a fix is coming. Empty means nothing is shown. Max 200 characters.",
  "notice": ""
}
```

- [ ] **Step 10: Load the config at startup**

In `MainWindow.axaml.cs`, in the constructor, immediately before the existing `if (_settings.AutoCheckUpdates)` line:

```csharp
        // Config first, so anything it turns off is off before the update
        // prompt or any feature has had a chance to run. Fire and forget: the
        // shipped defaults are already in force, and nothing waits on this.
        _ = RemoteConfig.LoadAsync(CancellationToken.None);
```

- [ ] **Step 11: Build and verify no warnings**

Run: `dotnet build -c Debug -v minimal`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 12: Commit**

```bash
git add Core/RemoteConfig.cs Core/RemoteConfig.Tests.cs Core/Updater.cs Core/ClickTiming.cs config.json MainWindow.axaml.cs Testing/JinxyMac.Tests.csproj
git commit -m "$(cat <<'EOF'
Make the timing numbers fixable without shipping a build

Some problems are a wrong number rather than wrong code, and a release
costs this app's users more than it costs anyone on Windows: the bundle is
unsigned, so replacing it makes a new app to Accessibility and the grant
has to be removed and re-added by hand. Editing one file in a browser
skips all of that.

Strictly bounded. It can move numbers within ranges this build already
accepts and turn features off, and nothing else. Every value is clamped by
the app rather than the file, switches fail to enabled, and anything
unreadable falls back to what shipped - so the app works perfectly with the
whole mechanism unreachable, which for anyone offline it is.

Also deletes the frame-sampling model from ClickTiming. That explanation
was disproved: a competing clicker holds for 15.6ms, under a frame, and
registers; this app's own measured profile has a gap of a third of a frame
and wins anyway.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: ClickButton — the portable half

The enum and its naming. The platform flag tables come in Task 5.

**Files:**
- Create: `Core/ClickButton.cs`
- Create: `Core/ClickButton.Tests.cs`
- Modify: `Testing/JinxyMac.Tests.csproj`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `enum ClickButton { Left, Right, Middle }`
  - `ClickButtons.Label(ClickButton) -> string` — `"Left"`, `"Right"`, `"Wheel"`
  - `ClickButtons.Parse(string?) -> ClickButton`

- [ ] **Step 1: Write the failing tests**

Create `Core/ClickButton.Tests.cs`:

```csharp
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
```

- [ ] **Step 2: Add the files to the test project**

```xml
    <Compile Include="../Core/ClickButton.cs" />
    <Compile Include="../Core/ClickButton.Tests.cs" />
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Testing/JinxyMac.Tests.csproj --filter "FullyQualifiedName~ClickButtonTests"`
Expected: FAIL — `ClickButton` does not exist.

- [ ] **Step 4: Write the implementation**

Create `Core/ClickButton.cs`:

```csharp
namespace JinxyMac.Core;

/// <summary>Which physical button the click engine presses.</summary>
public enum ClickButton
{
    Left,
    Right,
    Middle
}

/// <summary>What each button is called where it is shown, and how it is read back.</summary>
/// <remarks>
/// The platform event codes deliberately do not live here. A press and its
/// release are sent separately — the duty cycle puts real time between them —
/// and pairing the wrong two leaves a button held down across the whole
/// desktop with nothing to release it. Each engine owns its own pairing table
/// so that mistake is impossible to make across a platform boundary.
/// </remarks>
public static class ClickButtons
{
    /// <summary>What the button is called where it is shown.</summary>
    public static string Label(ClickButton button) => button switch
    {
        ClickButton.Right => "Right",
        ClickButton.Middle => "Wheel",
        _ => "Left"
    };

    /// <summary>
    /// Reads a stored or tag value back into a button.
    /// </summary>
    /// <remarks>
    /// Anything unrecognised is the left button rather than a failure. A
    /// hand-edited settings file naming a button that does not exist should
    /// leave a working clicker, not one that presses nothing.
    /// </remarks>
    public static ClickButton Parse(string? name) =>
        Enum.TryParse(name, ignoreCase: true, out ClickButton button)
        && Enum.IsDefined(button)
            ? button
            : ClickButton.Left;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Testing/JinxyMac.Tests.csproj --filter "FullyQualifiedName~ClickButtonTests"`
Expected: PASS, 15 tests.

Note: `Enum.TryParse` accepts numeric strings, so `Parse("4")` would yield an
undefined `ClickButton`. `Enum.IsDefined` is what rejects it — if the
`AnythingUnrecognisedIsTheLeftButton` case for `"4"` fails, that guard is
missing.

- [ ] **Step 6: Commit**

```bash
git add Core/ClickButton.cs Core/ClickButton.Tests.cs Testing/JinxyMac.Tests.csproj
git commit -m "$(cat <<'EOF'
Name the three buttons the clicker can press

The enum and its display names only. The platform event codes stay in the
engines: a press and its release are sent separately, with the duty cycle
putting real time between them, and pairing the wrong two leaves a button
held down across the whole desktop with nothing to release it. One pairing
table per engine makes that mistake impossible to make across a platform
boundary.

Anything unrecognised parses as the left button. A hand-edited settings
file naming a button that does not exist should leave a working clicker,
not one that presses nothing.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Teach both engines to press any button

The interface change and both implementations, plus the loop tracking which button it actually pressed. This is the task that can leave a button stuck across the desktop, so it gets its own regression test with a fake engine.

**Files:**
- Modify: `Engine/IClickEngine.cs`
- Modify: `Engine/MacClickEngine.cs`
- Modify: `Engine/WindowsClickEngine.cs`
- Modify: `Core/Clicker.cs`
- Create: `Core/Clicker.Tests.cs`
- Modify: `Testing/JinxyMac.Tests.csproj`

**Interfaces:**
- Consumes: `ClickButton`, `ClickButtons` from Task 4
- Produces:
  - `IClickEngine.MouseDown(ClickButton button)`, `IClickEngine.MouseUp(ClickButton button)`
  - `ClickSettings(double Cps, double Duty, bool HitFix, bool Spin, ClickButton Button = ClickButton.Left)`

- [ ] **Step 1: Write the failing test**

Create `Core/Clicker.Tests.cs`:

```csharp
using JinxyMac.Engine;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Records what the loop actually sent, so the pairing can be checked without
/// a mouse.
/// </summary>
internal sealed class FakeClickEngine : IClickEngine
{
    private readonly object _gate = new();
    private readonly List<(ClickButton Button, bool Down)> _events = new();

    public bool IsAvailable => true;
    public string? Unavailable => null;

    public IReadOnlyList<(ClickButton Button, bool Down)> Events
    {
        get { lock (_gate) return _events.ToList(); }
    }

    public void MouseDown(ClickButton button)
    {
        lock (_gate) _events.Add((button, true));
    }

    public void MouseUp(ClickButton button)
    {
        lock (_gate) _events.Add((button, false));
    }

    public void MoveBy(int dx, int dy) { }
}

/// <summary>
/// The click loop's pairing guarantee.
/// </summary>
/// <remarks>
/// A press and its release are sent separately. If a stop, or a change of
/// selected button, lands between them and the release names a different
/// button, the pressed one stays down across the entire desktop with nothing
/// to release it. On macOS that is a stuck context menu.
/// </remarks>
public class ClickerTests
{
    private static void RunBriefly(Clicker clicker)
    {
        clicker.Start();
        Thread.Sleep(400);
        clicker.Stop();
        Thread.Sleep(200);
    }

    [Fact]
    public void EveryPressIsReleased()
    {
        var engine = new FakeClickEngine();
        using var clicker = new Clicker(engine);

        clicker.Apply(new ClickSettings(20, 0.5, HitFix: true, Spin: false));
        RunBriefly(clicker);

        var events = engine.Events;

        Assert.NotEmpty(events);
        Assert.Equal(events.Count(e => e.Down), events.Count(e => !e.Down));
    }

    [Fact]
    public void ThePressAndItsReleaseNameTheSameButton()
    {
        var engine = new FakeClickEngine();
        using var clicker = new Clicker(engine);

        clicker.Apply(new ClickSettings(20, 0.5, true, false, ClickButton.Right));
        RunBriefly(clicker);

        var events = engine.Events;

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal(ClickButton.Right, e.Button));
    }

    /// <summary>
    /// The case that leaves a button stuck: the selector moves while a press is
    /// open. The release must name what was pressed, not what is selected now.
    /// </summary>
    [Fact]
    public void ChangingTheButtonMidRun_StillReleasesWhatWasPressed()
    {
        var engine = new FakeClickEngine();
        using var clicker = new Clicker(engine);

        clicker.Apply(new ClickSettings(8, 0.9, true, false, ClickButton.Right));
        clicker.Start();
        Thread.Sleep(150);

        clicker.Apply(new ClickSettings(8, 0.9, true, false, ClickButton.Middle));
        Thread.Sleep(400);
        clicker.Stop();
        Thread.Sleep(200);

        // Walk the log: every down must be followed by an up of the same button
        // before any other down.
        ClickButton? held = null;

        foreach ((ClickButton button, bool down) in engine.Events)
        {
            if (down)
            {
                Assert.Null(held);
                held = button;
            }
            else
            {
                Assert.Equal(held, button);
                held = null;
            }
        }

        Assert.Null(held);
    }
}
```

- [ ] **Step 2: Add the files to the test project**

In `Testing/JinxyMac.Tests.csproj`:

```xml
    <Compile Include="../Core/Clicker.cs" />
    <Compile Include="../Core/Clicker.Tests.cs" />
    <Compile Include="../Engine/IClickEngine.cs" />
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Testing/JinxyMac.Tests.csproj --filter "FullyQualifiedName~ClickerTests"`
Expected: FAIL — `IClickEngine` has no `MouseDown(ClickButton)`, and `ClickSettings` takes four arguments.

- [ ] **Step 4: Change the interface**

In `Engine/IClickEngine.cs`, add `using JinxyMac.Core;` at the top and replace the two method declarations:

```csharp
    /// <summary>
    /// Presses a button. The caller is responsible for releasing the same one.
    /// </summary>
    /// <remarks>
    /// The button is a parameter rather than engine state so a press and its
    /// release cannot disagree: there is nothing for a selector change to
    /// mutate between them.
    /// </remarks>
    void MouseDown(ClickButton button);

    void MouseUp(ClickButton button);
```

- [ ] **Step 5: Give MacClickEngine a pairing table**

In `Engine/MacClickEngine.cs`, add `using JinxyMac.Core;`, then replace `MouseDown`/`MouseUp`:

```csharp
    public void MouseDown(ClickButton button) => Post(button, down: true, pressure: 1.0);

    public void MouseUp(ClickButton button) => Post(button, down: false, pressure: 0.0);

    /// <summary>
    /// The Quartz event type and button number for each button.
    /// </summary>
    /// <remarks>
    /// Left and right have their own event types; everything else is "other"
    /// and carries its number in the event. Kept as one table so the down and
    /// the up cannot come from different places.
    /// </remarks>
    private static (uint Down, uint Up, uint Number) Codes(ClickButton button) => button switch
    {
        ClickButton.Right => (EventRightMouseDown, EventRightMouseUp, MouseButtonRight),
        ClickButton.Middle => (EventOtherMouseDown, EventOtherMouseUp, MouseButtonCenter),
        _ => (EventLeftMouseDown, EventLeftMouseUp, MouseButtonLeft)
    };
```

Add the constants beside the existing ones:

```csharp
    private const uint EventRightMouseDown = 3;
    private const uint EventRightMouseUp = 4;
    private const uint EventOtherMouseDown = 25;
    private const uint EventOtherMouseUp = 26;
    private const uint MouseButtonRight = 1;
    private const uint MouseButtonCenter = 2;
```

Change the `Post` signature and its two call sites inside it:

```csharp
    private static void Post(ClickButton button, bool down, double pressure)
    {
        (uint downType, uint upType, uint number) = Codes(button);

        IntPtr click = IntPtr.Zero;

        try
        {
            click = CGEventCreateMouseEvent(Source, down ? downType : upType, Location(), number);
            if (click == IntPtr.Zero) return;

            CGEventSetIntegerValueField(click, EventFieldClickState, 1);
            CGEventSetDoubleValueField(click, EventFieldPressure, pressure);

            CGEventPost(HidEventTap, click);
        }
        catch
        {
            // Never bring the loop down.
        }
        finally
        {
            if (click != IntPtr.Zero) CFRelease(click);
        }
    }
```

`MoveBy` still calls `CGEventCreateMouseEvent(Source, EventMouseMoved, to, MouseButtonLeft)` — leave it exactly as it is. The click-state and pressure lines in `Post` must not change; they are what made the clicks land in Roblox.

- [ ] **Step 6: Give WindowsClickEngine the same table**

In `Engine/WindowsClickEngine.cs`, add `using JinxyMac.Core;` and replace `MouseDown`/`MouseUp`:

```csharp
    public void MouseDown(ClickButton button) => Send(DownFlag(button), 0, 0);

    public void MouseUp(ClickButton button) => Send(UpFlag(button), 0, 0);

    private static uint DownFlag(ClickButton button) => button switch
    {
        ClickButton.Right => MouseEventRightDown,
        ClickButton.Middle => MouseEventMiddleDown,
        _ => MouseEventLeftDown
    };

    private static uint UpFlag(ClickButton button) => button switch
    {
        ClickButton.Right => MouseEventRightUp,
        ClickButton.Middle => MouseEventMiddleUp,
        _ => MouseEventLeftUp
    };
```

Add beside the existing flag constants:

```csharp
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
```

- [ ] **Step 7: Carry the button through the loop**

In `Core/Clicker.cs`, extend the record:

```csharp
public sealed record ClickSettings(
    double Cps, double Duty, bool HitFix, bool Spin, ClickButton Button = ClickButton.Left)
{
    public ClickTiming Timing => ClickTimings.Resolve(Cps, Duty, HitFix);
}
```

In `Loop`, change `bool buttonDown = false;` to:

```csharp
        // The button actually pressed, which is not necessarily the one
        // selected now. A release naming a different button leaves the pressed
        // one down across the whole desktop with nothing to release it.
        ClickButton? held = null;
```

Inside the `lock (_inputGate)` block:

```csharp
                lock (_inputGate)
                {
                    ClickButton pressing = s.Button;

                    _engine.MouseDown(pressing);
                    held = pressing;
                    deadline += (long)(downMs * freq / 1000.0);

                    cancelled = !WaitUntil(deadline, s.Spin, token);

                    if (!cancelled)
                    {
                        _engine.MouseUp(pressing);
                        held = null;
                        Interlocked.Increment(ref _clicks);
                    }
                }
```

And the `finally`:

```csharp
        finally
        {
            // The one that was pressed, whatever is selected now.
            if (held is ClickButton stuck) _engine.MouseUp(stuck);
        }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test Testing/JinxyMac.Tests.csproj --filter "FullyQualifiedName~ClickerTests"`
Expected: PASS, 3 tests.

- [ ] **Step 9: Build and run the app to confirm the seam still works**

```bash
dotnet build -c Debug -v minimal
./bin/Debug/net10.0/JinxyMac.exe
```

Expected: 0 warnings; the window opens and clicking still works on the left button. Close it before continuing.

- [ ] **Step 10: Commit**

```bash
git add Engine/IClickEngine.cs Engine/MacClickEngine.cs Engine/WindowsClickEngine.cs Core/Clicker.cs Core/Clicker.Tests.cs Testing/JinxyMac.Tests.csproj
git commit -m "$(cat <<'EOF'
Let the clicker press right and wheel, not only left

The button is a parameter on the engine rather than engine state, and the
loop remembers the one it actually pressed. Those two together are the
whole point: a press and its release are sent separately, with the duty
cycle putting real time between them, so a selector change or a stop
landing in that gap could previously have released a different button than
the one held - leaving the pressed one down across the entire desktop with
nothing to release it. On macOS that is a stuck context menu.

Each engine owns its own pairing table. Quartz gives left and right their
own event types and puts everything else under "other" with a button
number; Windows has a flag per edge. Neither can borrow the other's.

Tested with a fake engine that records what was sent, including the case
where the selected button changes while a press is open.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Show the button and the verdict on the Clicker page

**Files:**
- Modify: `MainWindow.axaml` (button selector; verdict line under `MeasuredText`)
- Modify: `MainWindow.axaml.cs` (`WireClicker`, `Publish`, `UpdateMeasured`, `ApplySettings`, `Persist`)
- Modify: `Core/AppSettings.cs` (persist the button)
- Modify: `Core/Presets.cs` (the `Measured` preset)

**Interfaces:**
- Consumes: `ClickButton`, `ClickButtons` (Task 4); `ClickOutput`, `OutputState` (Task 2); `ClickSettings.Button` (Task 5)
- Produces: `AppSettings.ClickButton` (string, default `"Left"`)

- [ ] **Step 1: Add the Measured preset**

In `Core/Presets.cs`, at the top of the `Defaults()` list:

```csharp
        // Measured, not chosen. Frame by frame off a match where this exact
        // configuration landed 34 hits on a run that a 193 CPS setup landed 33
        // — the whole point being that the slower one won. Hold mode and the
        // duty cycle are as load-bearing as the rate, so it ships as all three.
        //
        // First in the list because it is the only entry here with a number
        // behind it, and because the rates below are the ones that lose.
        new ClickPreset("Measured", 41.2, 77.37, holdMode: true),
```

- [ ] **Step 2: Verify the existing preset tests still pass**

Run: `dotnet test Testing/JinxyMac.Tests.csproj --filter "FullyQualifiedName~Preset"`
Expected: PASS. If a test asserts a default count, update it to 12 and note that `PresetStore` persists the whole list, so existing users only see `Measured` after Restore.

- [ ] **Step 3: Persist the button**

In `Core/AppSettings.cs`, after the `HitFix` / `UltraAccuracy` properties:

```csharp
    /// <summary>Which button the clicker presses. Stored by name, not number.</summary>
    /// <remarks>
    /// A name so a settings file stays readable and a reordered enum cannot
    /// silently change what someone's saved configuration does.
    /// </remarks>
    public string ClickButton { get; set; } = "Left";
```

- [ ] **Step 4: Add the selector and the verdict line to the markup**

In `MainWindow.axaml`, replace the `MeasuredText` block at line ~404 with:

```xml
                                    <TextBlock Name="MeasuredText" Text="Measured — /s"
                                               Classes="hint"/>
                                    <TextBlock Name="VerdictText" TextWrapping="Wrap"
                                               Classes="hint" Margin="0,6,0,0"
                                               Text="Start clicking to measure what the game actually receives."/>
```

Then, inside the `MODE` card's `StackPanel` (the one holding the Hold/Toggle buttons, around line 526), after that `StackPanel` closes, add:

```xml
                                        <StackPanel Orientation="Horizontal" Margin="0,14,0,0" Spacing="8">
                                            <TextBlock Text="BUTTON" VerticalAlignment="Center"
                                                       FontWeight="Bold" Margin="0,0,8,0"/>
                                            <RadioButton Name="ButtonLeft" GroupName="ClickButton"
                                                         Content="Left" IsChecked="True"/>
                                            <RadioButton Name="ButtonRight" GroupName="ClickButton"
                                                         Content="Right"/>
                                            <RadioButton Name="ButtonMiddle" GroupName="ClickButton"
                                                         Content="Wheel"/>
                                        </StackPanel>
```

- [ ] **Step 5: Wire the selector**

In `MainWindow.axaml.cs`, inside `WireClicker()`, beside the existing `SpinToggle` wiring:

```csharp
        ButtonLeft.IsCheckedChanged += (_, _) => Publish();
        ButtonRight.IsCheckedChanged += (_, _) => Publish();
        ButtonMiddle.IsCheckedChanged += (_, _) => Publish();
```

Add a helper beside `Publish`:

```csharp
    /// <summary>The button the selector is on.</summary>
    private ClickButton SelectedButton =>
        ButtonRight.IsChecked == true ? ClickButton.Right
        : ButtonMiddle.IsChecked == true ? ClickButton.Middle
        : ClickButton.Left;
```

- [ ] **Step 6: Send the selected button**

In `Publish()`, replace both `ClickSettings` constructions:

```csharp
        _clicker.Apply(_building
            ? new ClickSettings(Clicker.BuildCps, Clicker.BuildDuty, false,
                                SpinToggle.IsChecked == true, SelectedButton)
            : new ClickSettings(cps, duty, hitFix,
                                SpinToggle.IsChecked == true, SelectedButton));
```

- [ ] **Step 7: Show the verdict**

In `UpdateMeasured()`, inside the `if (seconds > 0)` block, immediately after the existing `MeasuredText.Text = ...` assignment:

```csharp
            // Classified from the same one-second delta the readout shows, so
            // the sentence and the number can never disagree.
            double setCps = _building ? Clicker.BuildCps : CpsSlider.Value;
            double duty = Math.Clamp(DutySlider.Value / 100.0, 0, 1);

            OutputState state = ClickOutput.Classify(
                _clicker.IsRunning,
                setCps,
                rate,
                hitFixClamping: !_building
                                && ClickTimings.IsClamped(setCps, duty, HitFixToggle.IsChecked == true));

            VerdictText.Text = ClickOutput.Verdict(state, setCps, rate);

            VerdictText.Foreground = ClickOutput.IsWarning(state)
                ? this.FindResource("Accent") as IBrush
                : this.FindResource("TextMuted") as IBrush;
```

Add `using Avalonia.Media;` at the top of the file if it is not already there.

- [ ] **Step 8: Restore and persist the button**

In `ApplySettings()`, beside the other toggles:

```csharp
        ClickButton restored = ClickButtons.Parse(_settings.ClickButton);

        ButtonLeft.IsChecked = restored == ClickButton.Left;
        ButtonRight.IsChecked = restored == ClickButton.Right;
        ButtonMiddle.IsChecked = restored == ClickButton.Middle;
```

In `Persist()`, beside `_settings.UltraAccuracy = ...`:

```csharp
        _settings.ClickButton = SelectedButton.ToString();
```

- [ ] **Step 9: Build and drive the app**

```bash
dotnet build -c Debug -v minimal
./bin/Debug/net10.0/JinxyMac.exe
```

Check by hand, in the window:
1. The BUTTON row shows Left / Right / Wheel, with Left selected.
2. Set CPS to 193 and press START. The verdict reads that HitFix is holding it to ~33 and that the slider above ~33 changes nothing.
3. Set CPS to 20 with HitFix off. The verdict reads that it is matching.
4. Select Right, press START over a blank area, confirm right-clicks fire, press STOP. **No context menu is left stuck open.**
5. Close and reopen the app. The button selection is still Right.

- [ ] **Step 10: Run the whole suite**

Run: `dotnet test Testing/JinxyMac.Tests.csproj`
Expected: all pass, 0 warnings on build.

- [ ] **Step 11: Commit**

```bash
git add MainWindow.axaml MainWindow.axaml.cs Core/AppSettings.cs Core/Presets.cs
git commit -m "$(cat <<'EOF'
Put the button selector and the output verdict on the Clicker page

The measured rate had no explanation next to it, so a 193 CPS setting
delivering 33 a second read as the app being broken rather than as the
ordinary case. The verdict says which of those it is, and separates two
things every clicker conflates: falling short of the ask, and meeting an
ask that is past the point the server turns clicks into hits.

Classified from the same one-second delta the readout shows, so the
sentence and the number cannot disagree.

Also ships the Measured preset - 41.2 CPS at 77.37% in hold mode, read
frame by frame off the match that landed 34 hits against a 193 CPS setup's
33. It leads the list because it is the only entry with a number behind it.
Existing users will not see it until they use Restore, because the whole
preset list is persisted so deleted defaults stay deleted.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Wallpaper

**Files:**
- Create: `Core/Wallpaper.cs`
- Create: `Core/Wallpaper.Tests.cs`
- Copy in: `Assets/DefaultWallpaper.png` (from the Windows repo's `icon-source.png`, 168 KB)
- Modify: `JinxyMac.csproj` (embed the resource)
- Modify: `Core/AppSettings.cs` (wallpaper name and dimming)
- Modify: `MainWindow.axaml` / `MainWindow.axaml.cs` (Theme page picker)
- Modify: `Testing/JinxyMac.Tests.csproj`

**Interfaces:**
- Consumes: `SettingsPath.For(string)` from `Core/SettingsPath.cs`
- Produces:
  - `Wallpaper.IsSupported(string?) -> bool`
  - `Wallpaper.StoredNameFor(string) -> string`
  - `Wallpaper.Store(string) -> string?`
  - `Wallpaper.Resolve(string?) -> string?`
  - `Wallpaper.InstallDefault() -> string`
  - `Wallpaper.Clear()`
  - `Wallpaper.ClampDimming(int) -> int`, `DimmingOpacity(int) -> double`
  - `Wallpaper.MinDimming = 0`, `MaxDimming = 90`, `DefaultDimming = 45`
  - `Wallpaper.PickerFileType -> FilePickerFileType`
  - `AppSettings.WallpaperName` (string), `AppSettings.WallpaperDimming` (int)

- [ ] **Step 1: Write the failing tests**

Create `Core/Wallpaper.Tests.cs`:

```csharp
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The picture behind the window, and the rules that keep it findable. The
/// stored name is the load-bearing part: a path would stop meaning anything
/// the moment the source file moved.
/// </summary>
public class WallpaperTests
{
    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.PNG", true)]
    [InlineData("a.jpg", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.bmp", true)]
    [InlineData("a.gif", false)]
    [InlineData("a.txt", false)]
    [InlineData("a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyDecodableFormatsAreAccepted(string? path, bool expected)
    {
        Assert.Equal(expected, Wallpaper.IsSupported(path));
    }

    [Fact]
    public void TheStoredNameIsOneStemPlusTheSourceExtension()
    {
        Assert.Equal("wallpaper.png", Wallpaper.StoredNameFor(@"C:\pics\Holiday.PNG"));
        Assert.Equal("wallpaper.jpg", Wallpaper.StoredNameFor("/home/me/a.jpg"));
    }

    [Fact]
    public void AMissingSourceStoresNothing()
    {
        Assert.Null(Wallpaper.Store(Path.Combine(Path.GetTempPath(), "no-such-file.png")));
    }

    [Fact]
    public void AnUnsupportedSourceStoresNothing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wp-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "not an image");

        try
        {
            Assert.Null(Wallpaper.Store(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NothingStoredResolvesToNothing()
    {
        Assert.Null(Wallpaper.Resolve(null));
        Assert.Null(Wallpaper.Resolve(""));
        Assert.Null(Wallpaper.Resolve("   "));
    }

    /// <summary>
    /// A bare name is what gets stored. Anything carrying a directory came from
    /// a hand-edited settings file and is not followed.
    /// </summary>
    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData(@"C:\Windows\System32\config")]
    [InlineData("sub/wallpaper.png")]
    public void APathIsNotFollowed(string stored)
    {
        Assert.Null(Wallpaper.Resolve(stored));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(90, 90)]
    [InlineData(100, 90)]
    public void DimmingIsClampedShortOfPaintingItOut(int given, int expected)
    {
        Assert.Equal(expected, Wallpaper.ClampDimming(given));
    }

    [Fact]
    public void DimmingBecomesAnOpacityFraction()
    {
        Assert.Equal(0.45, Wallpaper.DimmingOpacity(45), 3);
        Assert.Equal(0.90, Wallpaper.DimmingOpacity(100), 3);
    }
}
```

- [ ] **Step 2: Add the files to the test project**

```xml
    <Compile Include="../Core/Wallpaper.cs" />
    <Compile Include="../Core/Wallpaper.Tests.cs" />
```

`Core/SettingsPath.cs` is already listed.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Testing/JinxyMac.Tests.csproj --filter "FullyQualifiedName~WallpaperTests"`
Expected: FAIL — `Wallpaper` does not exist.

- [ ] **Step 4: Port Wallpaper.cs**

Copy `C:\Users\rschi\_dev\Joshua\MyBlinkStyleClicker\MyBlinkStyleClicker\Wallpaper.cs` to `Core/Wallpaper.cs` and make exactly these changes:

1. Namespace `JinxyClicker` → `JinxyMac.Core`.
2. Drop `using System;`, `using System.IO;`, `using System.Linq;` — `ImplicitUsings` covers them. Keep `using System.Reflection;` only if the file still needs it.
3. Add `using Avalonia.Platform.Storage;`.
4. Replace the `FileFilter` property with:

```csharp
    /// <summary>What the file picker offers, built from the same list.</summary>
    /// <remarks>
    /// Avalonia takes a typed filter rather than the pipe-delimited string WPF
    /// wanted, so this is the one part of the file that could not come across
    /// unchanged.
    /// </remarks>
    public static FilePickerFileType PickerFileType => new("Images")
    {
        Patterns = Allowed.Select(e => "*" + e).ToArray()
    };
```

5. Update the `Allowed` doc comment: "Formats WPF can decode" → "Formats Avalonia's Skia decoder handles".

Everything else — `StoredStem`, `IsSupported`, `StoredNameFor`, dimming, `Store`, `Resolve`, `InstallDefault`, `Clear` — comes across verbatim.

- [ ] **Step 5: Note the .webp question, to be settled at Step 12**

The Windows `Allowed` list includes `.webp`. Avalonia decodes through SkiaSharp, which should handle it, but this is unverified (spec, confidence: moderate). There is no way to check it short of decoding one, so leave `".webp"` in `Allowed` for now and settle it in Step 12 with a real file in the running app.

If it does not draw there, **remove `".webp"` from `Allowed`** rather than ship a format that fails at load, and say so in the commit message.

- [ ] **Step 6: Add the default wallpaper asset**

```bash
mkdir -p /c/Users/rschi/_dev/Joshua/JinxyMac/Assets
cp "/c/Users/rschi/_dev/Joshua/MyBlinkStyleClicker/MyBlinkStyleClicker/icon-source.png" \
   /c/Users/rschi/_dev/Joshua/JinxyMac/Assets/DefaultWallpaper.png
ls -la /c/Users/rschi/_dev/Joshua/JinxyMac/Assets/DefaultWallpaper.png
```

Expected: ~171,773 bytes.

In `JinxyMac.csproj`, add a new `<ItemGroup>`:

```xml
  <ItemGroup>
    <!--
      The wallpaper a fresh install starts with. LogicalName pins the manifest
      name so moving this file cannot silently break Wallpaper.InstallDefault,
      which looks it up by string.
    -->
    <EmbeddedResource Include="Assets/DefaultWallpaper.png" LogicalName="DefaultWallpaper.png" />
  </ItemGroup>
```

- [ ] **Step 7: Persist the wallpaper**

In `Core/AppSettings.cs`, beside `AccentColor` and `Dark`:

```csharp
    /// <summary>Bare file name of the stored wallpaper, or empty for none.</summary>
    /// <remarks>
    /// A name rather than a path: the file is copied into the settings folder,
    /// so where it came from stops mattering the moment it is chosen.
    /// </remarks>
    public string WallpaperName { get; set; } = "";

    /// <summary>How far the wallpaper is darkened, as a percentage.</summary>
    public int WallpaperDimming { get; set; } = Wallpaper.DefaultDimming;
```

In `AppSettings.Load()`, in the branch that handles a missing file:

```csharp
            if (!System.IO.File.Exists(File))
            {
                // A missing settings file is the one reliable signal for a
                // brand new install. Someone who has settings and no wallpaper
                // cleared it on purpose, and reinstalling it under them would
                // read as the app deciding for itself.
                return new AppSettings { WallpaperName = Wallpaper.InstallDefault() };
            }
```

- [ ] **Step 8: Add the Theme page controls**

In `MainWindow.axaml`, inside `PageTheme`, before the closing `</StackPanel>` of the page:

```xml
                        <Border Classes="card" Margin="0,14,0,0">
                            <StackPanel>
                                <TextBlock Text="BACKGROUND" FontWeight="Bold"/>
                                <TextBlock Classes="hint" Margin="0,4,0,0"
                                           Text="A picture behind the window. It is copied here, so moving or deleting the original changes nothing."/>

                                <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,12,0,0">
                                    <Button Name="ChooseWallpaperButton" Content="Choose a picture"/>
                                    <Button Name="ClearWallpaperButton" Content="Remove"/>
                                </StackPanel>

                                <TextBlock Name="WallpaperStatusText" Classes="hint" Margin="0,10,0,0"
                                           Text="No picture set."/>

                                <TextBlock Text="DIMMING" FontWeight="Bold" Margin="0,16,0,0"/>
                                <Slider Name="WallpaperDimmingSlider" Minimum="0" Maximum="90"
                                        Value="45" Margin="0,6,0,0"/>
                            </StackPanel>
                        </Border>
```

- [ ] **Step 9: Wire the picker**

In `MainWindow.axaml.cs`, inside `WireTheme()`:

```csharp
        ChooseWallpaperButton.Click += async (_, _) => await ChooseWallpaperAsync();

        ClearWallpaperButton.Click += (_, _) =>
        {
            Wallpaper.Clear();
            _settings.WallpaperName = "";
            ApplyWallpaper();
            Persist();
        };

        WallpaperDimmingSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;

            _settings.WallpaperDimming = Wallpaper.ClampDimming((int)WallpaperDimmingSlider.Value);
            ApplyWallpaper();

            if (!_loading) Persist();
        };
```

Add the two methods beside `WireTheme`:

```csharp
    private async Task ChooseWallpaperAsync()
    {
        try
        {
            IReadOnlyList<IStorageFile> picked = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Choose a background",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { Wallpaper.PickerFileType }
                });

            string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            if (path == null) return;

            string? stored = Wallpaper.Store(path);
            if (stored == null)
            {
                WallpaperStatusText.Text = "That file could not be read. Try a PNG or JPEG.";
                return;
            }

            _settings.WallpaperName = stored;
            ApplyWallpaper();
            Persist();
        }
        catch
        {
            // A cancelled or failed pick leaves the previous background alone.
        }
    }

    /// <summary>Paints the stored wallpaper, or nothing when there is not one.</summary>
    private void ApplyWallpaper()
    {
        string? path = Wallpaper.Resolve(_settings.WallpaperName);

        WallpaperImage.Source = null;

        // Both layers move together. A dim border left visible with no picture
        // behind it would tint the whole window for no reason.
        if (path == null)
        {
            WallpaperStatusText.Text = "No picture set.";
            WallpaperImage.IsVisible = false;
            WallpaperDim.IsVisible = false;
            return;
        }

        try
        {
            WallpaperImage.Source = new Bitmap(path);
            WallpaperImage.IsVisible = true;
            WallpaperDim.IsVisible = true;
            WallpaperDim.Opacity = Wallpaper.DimmingOpacity(_settings.WallpaperDimming);
            WallpaperStatusText.Text = $"Using {_settings.WallpaperName}.";
        }
        catch
        {
            // A file that will not decode is the same as not having one.
            WallpaperImage.IsVisible = false;
            WallpaperDim.IsVisible = false;
            WallpaperStatusText.Text = "That picture could not be decoded, so it is not being shown.";
        }
    }
```

Add `using Avalonia.Media.Imaging;` and `using Avalonia.Platform.Storage;`.

- [ ] **Step 10: Add the background layer to the window**

The window's root child is `<Grid ColumnDefinitions="196,*">` at `MainWindow.axaml:207`. Add these as its **first two children**, immediately after that opening tag, so they paint behind everything else:

```xml
        <!--
          Behind every other child, and across both columns. Without the span
          these would sit in column 0 and tint only the sidebar.
        -->
        <Image Name="WallpaperImage" Grid.Column="0" Grid.ColumnSpan="2"
               Stretch="UniformToFill" IsVisible="False"/>
        <Border Name="WallpaperDim" Grid.Column="0" Grid.ColumnSpan="2"
                Background="{DynamicResource WindowBackground}"
                Opacity="0.45" IsVisible="False"/>
```

Avalonia paints children in declaration order, so being first is what puts them at the back.

Call `ApplyWallpaper();` at the end of `ApplySettings()`, and set `WallpaperDimmingSlider.Value = _settings.WallpaperDimming;` beside the other restored values.

- [ ] **Step 11: Run the tests**

Run: `dotnet test Testing/JinxyMac.Tests.csproj`
Expected: all pass.

- [ ] **Step 12: Drive the app**

```bash
dotnet build -c Debug -v minimal
./bin/Debug/net10.0/JinxyMac.exe
```

Check by hand:
1. Theme page shows BACKGROUND with the two buttons and the dimming slider.
2. Choose a PNG — it appears behind the window, dimmed.
3. Drag the dimming slider — the darkening changes and stops at 90%, never fully hiding the picture.
4. Press Remove — the background goes, the status reads "No picture set."
5. Reopen the app — the chosen picture and dimming are restored.
6. Try a `.webp` file. If it does not draw, remove `".webp"` from `Allowed` per Step 5.

- [ ] **Step 13: Commit**

```bash
git add Core/Wallpaper.cs Core/Wallpaper.Tests.cs Core/AppSettings.cs Assets/DefaultWallpaper.png JinxyMac.csproj MainWindow.axaml MainWindow.axaml.cs Testing/JinxyMac.Tests.csproj
git commit -m "$(cat <<'EOF'
Ship a background, and let people choose their own

The chosen file is copied into the settings folder rather than referenced
where it sits. Someone picks a screenshot out of Downloads, empties
Downloads a week later, and the background they set is gone - with a copy
it simply is not, and the settings file stores a bare name that means the
same thing on any machine instead of a path that means nothing on another.

A name carrying a directory is refused rather than followed, so a
hand-edited settings file cannot point the window at an arbitrary file.

Dimming stops at 90%. A hand-edited 100 would paint the picture out
entirely and read as the feature being broken rather than as the value
being wrong.

A fresh install lays down a shipped default, keyed off the settings file
being absent - the one reliable signal for a new install. Someone who has
settings and no wallpaper cleared it deliberately.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Bump the version and verify the whole build

**Files:**
- Modify: `Core/Updater.cs`

**Interfaces:**
- Consumes: everything above
- Produces: `Updater.Version = "1.1.0"`

- [ ] **Step 1: Bump the version**

In `Core/Updater.cs`: `public const string Version = "1.0.8";` → `"1.1.0"`.

- [ ] **Step 2: Confirm the build script reads the new number**

`Packaging/build-mac.sh:54` already reads the constant rather than carrying its own copy:

```bash
version=$(grep -oE 'Version = "[0-9.]+"' "$project/Core/Updater.cs" | grep -oE '[0-9.]+')
```

Verify it now yields the bumped value:

```bash
cd /c/Users/rschi/_dev/Joshua/JinxyMac
grep -oE 'Version = "[0-9.]+"' Core/Updater.cs | grep -oE '[0-9.]+'
```

Expected: `1.1.0`. No change to the script is needed — this step only guards against the regex and the constant drifting apart.

- [ ] **Step 3: Full test run**

Run: `dotnet test Testing/JinxyMac.Tests.csproj`
Expected: every test passes.

- [ ] **Step 4: Release build, both architectures, leak check**

```bash
cd /c/Users/rschi/_dev/Joshua/JinxyMac
dotnet publish -c Release -r osx-arm64 --self-contained false -o /tmp/jm-final-arm64
dotnet publish -c Release -r osx-x64   --self-contained false -o /tmp/jm-final-x64
grep -a -c 'rschi' /tmp/jm-final-arm64/JinxyMac.dll /tmp/jm-final-x64/JinxyMac.dll
```

Expected: `0` on both.

- [ ] **Step 5: Commit**

```bash
git add Core/Updater.cs
git commit -m "$(cat <<'EOF'
Bump to 1.1.0

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## What this plan does not do

Recorded so it is not mistaken for an oversight:

- **No `ClickDiagnostics`.** Cut on instruction. The app has no way to measure its own timing, and there is no dev build on macOS.
- **No background-throttle defence.** Cut on instruction. App Nap and Apple Silicon efficiency-core confinement both apply to this app and neither is defended against. See the spec's "Cut on instruction" section for the evidence.
- **No `GCSettings.LatencyMode`.** Dropped with the above; it is a one-line addition if wanted.
- **No macros, kit wheel, kit art, update-flow changes, telemetry, `NavIcon.cs`, or streamer mode.** Each needs its own spec.
- **No per-click allocation fix.** There is no such defect here — `MacClickEngine.Post` passes a struct and allocates nothing managed.
