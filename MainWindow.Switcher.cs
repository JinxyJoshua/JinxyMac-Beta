using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// The auto switcher page: swap between two hotbar slots on a timer.
/// </summary>
/// <remarks>
/// Not a second engine — see <see cref="SwitcherMacro"/>'s remarks. This
/// file is the page around it, the same division <c>MainWindow.Macros.cs</c>
/// already draws for the macros it builds by hand, and its own conventions —
/// the HOTKEYS card, the fixed <c>Bind</c> helper, the checkbox as the single
/// source of "is it running" — are followed here rather than duplicated.
///
/// The three places this file still has to touch the main one: <c>Fire</c>
/// needs to dispatch the switcher's own toggle hotkey, <c>Bindings</c> and
/// <c>ArmHotkeys</c> need to know about it so it collides symmetrically with
/// every other bound key, and <c>StopClicker</c> is where every one of the
/// clicker's own stop paths clears it — see that method's remarks for why
/// that is the one place this needed adding.
/// </remarks>
public partial class MainWindow
{
    /// <summary>
    /// The switcher's own switch, independent of the master hotkeys toggle —
    /// mirrors a macro's own <see cref="KeyMacro.Enabled"/>, which the
    /// switcher does not have one of its own because it is never stored as a
    /// <see cref="KeyMacro"/> at rest (see <see cref="SwitcherMacro"/>).
    /// </summary>
    private bool _switcherDisabled;

    private void WireSwitcher()
    {
        SwitcherHotkeyKill.Click += (_, _) =>
            HotkeysEnabled.IsChecked = HotkeysEnabled.IsChecked != true;

        SwitcherDisableButton.Click += (_, _) => ToggleSwitcherDisabled();

        SwitcherEnabled.IsCheckedChanged += (_, _) => RefreshSwitcher();

        ClearSwitcherHotkeyButton.Click += (_, _) => ClearSwitcherHotkey();

        // Committed on Enter or on leaving the box, the same convention the
        // sliders' own typed readouts use (see BindReadout) — not on every
        // keystroke, which would rebuild and restart the macro mid-type and
        // fight over the very field being edited.
        foreach (TextBox box in new[]
                 { SlotABox, SlotBBox, SwitcherIntervalBox, SwitcherEquipBox, SwitcherIntervalBBox })
        {
            box.LostFocus += (_, _) => RefreshSwitcher();

            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                RefreshSwitcher();
                e.Handled = true;
            };
        }
    }

    /// <summary>
    /// Restores the five settings onto the page, but never as running.
    /// </summary>
    /// <remarks>
    /// A switcher that starts pressing number keys into whatever happens to
    /// be focused the moment the app opens — before the game is even the
    /// front window — is a nasty surprise nobody asked for, so the checkbox
    /// always comes back unchecked regardless of how the last session ended.
    /// </remarks>
    private void ApplySwitcherSettings()
    {
        _switcherDisabled = _settings.SwitcherDisabled;

        SlotABox.Text = _settings.SwitcherSlotA;
        SlotBBox.Text = _settings.SwitcherSlotB;
        SwitcherIntervalBox.Text = _settings.SwitcherIntervalMs.ToString(CultureInfo.CurrentCulture);
        SwitcherEquipBox.Text = _settings.SwitcherEquipMs.ToString(CultureInfo.CurrentCulture);
        SwitcherIntervalBBox.Text = _settings.SwitcherIntervalBMs.ToString(CultureInfo.CurrentCulture);

        SwitcherHotkeyButton.Content = _settings.SwitcherHotkeyName;

        SwitcherEnabled.IsChecked = false;
        SwitcherStatusText.Text = "Off.";
    }

    /// <summary>Flips the switcher from its own hotkey.</summary>
    /// <remarks>
    /// Goes through the checkbox rather than starting the macro directly —
    /// same reason <see cref="StopClicker"/> does — so the page and the
    /// runner cannot disagree about whether it is running.
    /// </remarks>
    private void ToggleSwitcherHotkey()
    {
        // A disabled switcher does not answer its key: otherwise the button
        // would say Enable while the hotkey still started it.
        if (_switcherDisabled) return;

        // Same guard the fixed hotkeys and macro toggles carry: bound to a
        // digit, this would otherwise fire while the slot boxes on this very
        // page are being typed into.
        if (IsActive && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;

        SwitcherEnabled.IsChecked = SwitcherEnabled.IsChecked != true;
    }

    private void ToggleSwitcherDisabled()
    {
        _switcherDisabled = !_switcherDisabled;

        // Stopped on the way out. Leaving it running while its own switch is
        // greyed out is the trap this button exists to avoid.
        if (_switcherDisabled) SwitcherEnabled.IsChecked = false;

        _settings.SwitcherDisabled = _switcherDisabled;
        _settings.Save();

        RefreshSwitcherCard();
    }

    private void ClearSwitcherHotkey()
    {
        if (_rebinding) return;

        _settings.SwitcherHotkeyCode = -1;
        _settings.SwitcherHotkeyName = HotkeyBinding.Unbound.Name;
        SwitcherHotkeyButton.Content = HotkeyBinding.Unbound.Name;

        _settings.Save();
        ArmHotkeys();
    }

    /// <summary>
    /// Rebuilds the switcher from its five fields and starts or stops it.
    /// </summary>
    /// <remarks>
    /// Always stopped first. The keys and holds can change while it runs, and
    /// a running thread holds the values it started with — <see cref="KeyMacro"/>
    /// is immutable and <see cref="MacroRunner"/> has no notion of editing one
    /// in place, so the only way to apply an edit is to replace the macro.
    /// </remarks>
    private void RefreshSwitcher()
    {
        _macros.Stop(SwitcherMacro.Name);

        string slotA = (SlotABox.Text ?? "").Trim();
        string slotB = (SlotBBox.Text ?? "").Trim();

        int? holdFirst = MacroStore.ParseInterval(SwitcherIntervalBox.Text);
        int? holdSecond = MacroStore.ParseInterval(SwitcherIntervalBBox.Text);
        int? equip = MacroStore.ParseInterval(SwitcherEquipBox.Text);

        // Persisted as typed, valid or not, so nothing typed is lost across a
        // restart — the same rule the sliders' readouts follow once they
        // commit. Only a value that actually parsed overwrites the setting;
        // an in-range default is far better to keep than to clobber with
        // whatever an unparsable box currently holds.
        _settings.SwitcherSlotA = slotA;
        _settings.SwitcherSlotB = slotB;
        if (holdFirst is int hf) _settings.SwitcherIntervalMs = hf;
        if (holdSecond is int hs) _settings.SwitcherIntervalBMs = hs;
        if (equip is int eq) _settings.SwitcherEquipMs = eq;
        _settings.Save();

        if (SwitcherEnabled.IsChecked != true)
        {
            SwitcherStatusText.Text = "Off.";
            RefreshSwitcherCard();
            return;
        }

        if (_switcherDisabled || HotkeysEnabled.IsChecked != true)
        {
            // The checkbox can still read checked here — disabling, or
            // turning hotkeys off, does not by itself uncheck it (see
            // ToggleSwitcherDisabled and ArmHotkeys) — so this still has to
            // say Off rather than an error about fields that are actually
            // fine.
            SwitcherStatusText.Text = "Off.";
            RefreshSwitcherCard();
            return;
        }

        if (holdFirst == null || holdSecond == null || equip == null)
        {
            SwitcherStatusText.Text =
                $"Both hold times and the equip delay must be between {KeyMacro.MinIntervalMs} "
                + $"and {KeyMacro.MaxIntervalMs} ms.";
            RefreshSwitcherCard();
            return;
        }

        // The click period comes from the clicker's own live sliders, not
        // from a snapshot — so changing CPS changes the floor the first hold
        // is raised to without anyone having to notice they needed to.
        double clickPeriodMs = new ClickSettings(
            CpsSlider.Value, Math.Clamp(DutySlider.Value / 100.0, 0, 1),
            HitFixToggle.IsChecked == true, SpinToggle.IsChecked == true, SelectedButton
        ).Timing.PeriodMs;

        SwitcherMacro.Result result =
            SwitcherMacro.Build(slotA, slotB, holdFirst.Value, holdSecond.Value, equip.Value, clickPeriodMs);

        if (result.Macro == null)
        {
            SwitcherStatusText.Text = result.Error ?? "Could not build the switcher.";
            RefreshSwitcherCard();
            return;
        }

        _macros.Start(result.Macro);

        SwitcherStatusText.Text =
            $"On — {slotA} for {result.FirstHoldMs} ms, then {slotB} for {holdSecond.Value} ms."
            + (result.Raised
                ? $" Raised from {holdFirst.Value} to {result.FirstHoldMs} ms: any shorter draws the "
                  + "weapon and swaps away before it fires."
                : "")
            + " It goes to whatever window is focused, so switch to the game now.";

        RefreshSwitcherCard();
    }

    /// <summary>Repaints the HOTKEYS note and the DISABLED/HOTKEYS OFF stamp.</summary>
    /// <remarks>
    /// Split from <see cref="RefreshSwitcher"/> so a hotkey rebind elsewhere —
    /// which calls <see cref="ArmHotkeys"/>, not this — can repaint the stamp
    /// without also stopping and restarting a switcher that was not what
    /// changed.
    /// </remarks>
    private void RefreshSwitcherCard()
    {
        bool hotkeysOn = HotkeysEnabled?.IsChecked != false;

        SwitcherHotkeyKill.Content = hotkeysOn ? "DISABLE HOTKEYS" : "HOTKEYS ARE OFF";
        SwitcherHotkeyKill.Foreground = hotkeysOn
            ? this.FindResource("Text") as IBrush
            : this.FindResource("Accent") as IBrush;

        SwitcherHotkeyNote.Text = hotkeysOn
            ? "Every bound key is live. Turn them off while you edit."
            : "All hotkeys are off. Nothing you press will trigger anything.";

        bool live = hotkeysOn && !_switcherDisabled;

        SwitcherDisableButton.Content = _switcherDisabled ? "Enable" : "Disable";
        SwitcherEnabled.IsEnabled = live;

        SwitcherCardBody.Opacity = live ? 1.0 : 0.45;
        SwitcherDisabledStamp.IsVisible = !live;
        SwitcherStampText.Text = _switcherDisabled ? "DISABLED" : "HOTKEYS OFF";
    }
}
