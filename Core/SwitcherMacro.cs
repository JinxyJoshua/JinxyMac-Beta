using System;

namespace JinxyMac.Core;

/// <summary>
/// Turns the auto switcher's settings into the <see cref="KeyMacro"/> that
/// actually runs it.
/// </summary>
/// <remarks>
/// The switcher is not a second engine — it is a <see cref="KeyMacro"/> under
/// a reserved name, handed to the same <see cref="MacroRunner"/> every other
/// macro answers to. What makes it a switcher rather than an ordinary macro is
/// entirely in the numbers: two keys, and an asymmetric hold on each — sword
/// held for a second or two, crossbow dipped to for a sixth of one.
///
/// Those numbers cannot live in <c>macros.json</c>: <see cref="MacroStore"/>
/// does not round-trip <see cref="KeyMacro.HoldsMs"/>,
/// <see cref="KeyMacro.ClicksWanted"/> or <see cref="KeyMacro.EquipMs"/>, so a
/// switcher saved there would come back on the next launch with symmetric
/// holds and no click-gated dwell — a macro that looks like the switcher and
/// behaves like an ordinary rotation. <see cref="AppSettings"/> is where the
/// five settings actually live; this type is the one place that turns them
/// back into a macro, called fresh every time the switcher (re)starts rather
/// than once at load, so an edited slot or hold takes effect on the next
/// press rather than the next restart.
/// </remarks>
public static class SwitcherMacro
{
    /// <summary>
    /// Reserved macro name. The leading space keeps it out of reach of
    /// anything a user could type: a macro name is trimmed before it is
    /// saved (see <c>MainWindow.Macros.cs</c>'s <c>SaveMacro</c>), so no
    /// macro the user creates can ever collide with this one, and it cannot
    /// appear alongside them on the macros page's own list.
    /// </summary>
    public const string Name = " AutoSwitcher";

    /// <summary>
    /// Clicks the first slot must actually receive before the cycle moves on.
    /// </summary>
    /// <remarks>
    /// Two, not one — see <see cref="KeyMacro.MinimumDwellMs"/>'s remarks for
    /// why a click landing on the exact instant the equip completes cannot be
    /// the only one counted.
    /// </remarks>
    public const int ClicksWanted = 2;

    /// <summary>
    /// The result of trying to build the switcher: either a macro ready to
    /// hand to <see cref="MacroRunner.Start"/>, or an explanation of why one
    /// could not be built yet.
    /// </summary>
    /// <param name="Macro">The macro to run, or null when the settings do not yet describe one.</param>
    /// <param name="Error">Why <paramref name="Macro"/> is null, or null when it isn't.</param>
    /// <param name="FirstHoldMs">
    /// The first hold actually used — see <see cref="Raised"/> for why this
    /// can differ from what was typed.
    /// </param>
    /// <param name="Raised">
    /// Whether <paramref name="FirstHoldMs"/> is higher than the typed hold
    /// because the typed value was too short to guarantee a shot.
    /// </param>
    public sealed record Result(KeyMacro? Macro, string? Error, int FirstHoldMs, bool Raised)
    {
        public static Result Failed(string error) => new(null, error, 0, false);
    }

    /// <summary>
    /// Builds the switcher's macro from its five settings, or says why it
    /// can't yet.
    /// </summary>
    /// <remarks>
    /// The first hold is raised to whatever <see cref="KeyMacro.MinimumDwellMs"/>
    /// says actually guarantees the two clicks the dip is waiting for, rather
    /// than trusting the typed number outright — <paramref name="clickPeriodMs"/>
    /// is a property of the clicker, not of the switcher, so a CPS change
    /// changes the floor without anyone having to notice they needed to
    /// retype a hold.
    /// </remarks>
    public static Result Build(
        string slotA, string slotB, int holdFirstMs, int holdSecondMs, int equipMs, double clickPeriodMs)
    {
        (int[] Keys, string Text)? keys =
            MacroStore.ParseKeys((slotA ?? "").Trim() + "," + (slotB ?? "").Trim());

        if (keys == null || keys.Value.Keys.Length != 2)
            return Result.Failed("Both slots need one letter or digit.");

        if (holdFirstMs is < KeyMacro.MinIntervalMs or > KeyMacro.MaxIntervalMs
            || holdSecondMs is < KeyMacro.MinIntervalMs or > KeyMacro.MaxIntervalMs)
        {
            return Result.Failed(
                $"Both hold times must be between {KeyMacro.MinIntervalMs} and {KeyMacro.MaxIntervalMs} ms.");
        }

        int floor = KeyMacro.MinimumDwellMs(clickPeriodMs, equipMs, ClicksWanted);
        int firstHold = Math.Max(holdFirstMs, floor);
        bool raised = firstHold > holdFirstMs;

        var macro = new KeyMacro(
            Name, keys.Value.Keys, keys.Value.Text, firstHold,
            holdsMs: new[] { firstHold, holdSecondMs },
            clicksWanted: ClicksWanted,
            equipMs: equipMs);

        return new Result(macro, null, firstHold, raised);
    }

    /// <summary>
    /// The key codes the two slots would send, parsed from whatever is
    /// currently typed — independent of whether the hold times or equip
    /// delay are valid, since this exists only to answer "would the switcher
    /// ever send this key".
    /// </summary>
    /// <remarks>
    /// Used from the fixed hotkeys' own rebind check
    /// (<c>MainWindow.axaml.cs</c>'s <c>Bind</c>), which has to refuse a code
    /// these slots already send — the same reason it refuses a code a macro
    /// sends (see <see cref="MacroStore.FindByKey"/>) — even before the
    /// switcher has ever been started: its slots are just two text boxes,
    /// not a <see cref="KeyMacro"/> at rest (see this type's own remarks),
    /// so nothing stores them anywhere <c>Bind</c> could otherwise have
    /// looked.
    ///
    /// A slot that doesn't parse (empty, or more than one letter or digit)
    /// contributes nothing rather than failing — it isn't a key yet, so it
    /// can't collide with one.
    /// </remarks>
    public static IEnumerable<int> SlotKeys(string? slotA, string? slotB)
    {
        if (MacroStore.ParseKeys(slotA) is (int[] keysA, _))
            foreach (int key in keysA) yield return key;

        if (MacroStore.ParseKeys(slotB) is (int[] keysB, _))
            foreach (int key in keysB) yield return key;
    }
}
