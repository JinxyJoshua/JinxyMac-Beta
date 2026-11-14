using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// The Performance page: Roblox's own client settings, written where the Mac
/// client reads them.
/// </summary>
/// <remarks>
/// Asked for by a Mac player, and worth doing before the Accessibility problem
/// is solved rather than after: this is a file write, not a permission, so it
/// works for the people whose clicker currently says BLOCKED.
///
/// Only Roblox's published allowlist is offered, and nothing touching character
/// physics — see <see cref="RobloxFastFlags"/>. The page explains each setting
/// in the words it costs, because turning the sky grey is a real trade and
/// nobody should find out by looking at it in a match.
/// </remarks>
public partial class MainWindow
{
    private readonly RobloxFastFlags _flags = new();

    /// <summary>The frame-rate targets offered, in the order shown.</summary>
    /// <remarks>
    /// 60 is the client's own behaviour and is stored as no setting at all.
    /// "Unlimited" is a number above any display's refresh, because the client
    /// has no true infinity.
    /// </remarks>
    private static readonly (string Label, int? Fps)[] FpsChoices =
    {
        ("60 (default)", 60),
        ("120", 120),
        ("144", 144),
        ("240", 240),
        ("Unlimited", RobloxFastFlags.UnlimitedFps)
    };

    private void WireTweaks()
    {
        foreach (RadioButton choice in FpsChoicePanel.Children.OfType<RadioButton>())
        {
            choice.IsCheckedChanged += (sender, _) =>
            {
                if (_loading) return;
                if (sender is not RadioButton { IsChecked: true, Tag: string tag }) return;
                if (!int.TryParse(tag, out int fps)) return;

                ReportTweaks(_flags.SetFpsCap(fps), $"Frame rate set to {FpsLabel(fps)}.");
            };
        }

        FpsBoostButton.Click += (_, _) =>
            ReportTweaks(_flags.Apply(RobloxFastFlags.FpsBoost),
                "Lower graphics applied — textures, grass, anti-aliasing and lighting detail.");

        GraySkyButton.Click += (_, _) => ToggleGraySky();

        RestoreTweaksButton.Click += (_, _) =>
            ReportTweaks(_flags.Restore(), "Roblox's own settings are back.");
    }

    private static string FpsLabel(int fps) =>
        fps >= RobloxFastFlags.UnlimitedFps ? "unlimited" : fps.ToString();

    private void ToggleGraySky()
    {
        bool on = _flags.Read().TryGetValue(RobloxFastFlags.SkyGrayFlag, out string? v) && v == "True";

        bool worked = on
            ? _flags.Clear(new[] { RobloxFastFlags.SkyGrayFlag })
            : _flags.Apply(new[]
            {
                new FastFlag(RobloxFastFlags.SkyGrayFlag, "True",
                    "Replaces the skybox with flat grey.")
            });

        ReportTweaks(worked, on ? "The sky is back to normal." : "The sky is now flat grey.");
    }

    /// <summary>Puts the page in step with what is actually in Roblox's file.</summary>
    private void RefreshTweaks()
    {
        if (!_flags.RobloxFound)
        {
            TweaksStatus.Text = "No Roblox install found. Install Roblox, then come back.";
            SetTweaksEnabled(false);
            return;
        }

        SetTweaksEnabled(true);

        _loading = true;
        try
        {
            int current = _flags.CurrentFpsCap() ?? 60;

            foreach (RadioButton choice in FpsChoicePanel.Children.OfType<RadioButton>())
            {
                choice.IsChecked = choice.Tag is string tag
                                   && int.TryParse(tag, out int fps)
                                   && fps == NearestChoice(current);
            }
        }
        finally
        {
            _loading = false;
        }

        bool graySky = _flags.Read().TryGetValue(RobloxFastFlags.SkyGrayFlag, out string? v) && v == "True";

        GraySkyButton.Content = graySky ? "Sky: flat grey" : "Sky: normal";
        FpsBoostButton.Content = _flags.IsApplied(RobloxFastFlags.FpsBoost)
            ? "Lower graphics: on"
            : "Lower graphics";

        TweaksStatus.Text = _flags.AnythingApplied()
            ? "Settings applied. Quit Roblox and open it again for them to take effect."
            : "Nothing applied — Roblox is running on its own settings.";
    }

    /// <summary>
    /// The offered choice closest to what the file holds.
    /// </summary>
    /// <remarks>
    /// Someone may have a target set by a bootstrapper that is not one of these
    /// five. Showing the nearest beats showing none selected, which would read
    /// as "no limit set" when there is one.
    /// </remarks>
    private static int NearestChoice(int fps)
    {
        IEnumerable<int> offered = FpsChoices.Select(c => c.Fps ?? 60);

        return offered.OrderBy(o => Math.Abs(o - fps)).First();
    }

    private void SetTweaksEnabled(bool on)
    {
        FpsChoicePanel.IsEnabled = on;
        FpsBoostButton.IsEnabled = on;
        GraySkyButton.IsEnabled = on;
        RestoreTweaksButton.IsEnabled = on;
    }

    private void ReportTweaks(bool worked, string done)
    {
        if (!worked)
        {
            TweaksStatus.Text = "Could not write Roblox's settings. Close Roblox and try again.";
            return;
        }

        RefreshTweaks();
        TweaksStatus.Text = done + " Quit Roblox and open it again.";
    }
}
