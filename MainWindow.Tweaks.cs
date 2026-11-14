using System.Collections.Generic;
using Avalonia.Controls;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// The Performance page: Roblox's own client settings, written where the Mac
/// client reads them.
/// </summary>
/// <remarks>
/// Worth doing before the Accessibility problem is solved rather than after:
/// this is a file write, not a permission, so it works for the people whose
/// clicker currently says BLOCKED.
///
/// <para>Only flags on Roblox's published allowlist, and nothing touching
/// character physics — see <see cref="RobloxFastFlags"/>. The page explains each
/// setting in the words it costs, because turning the sky grey is a real trade
/// and nobody should find out by looking at it in a match.</para>
///
/// <para>There is no frame-rate setting here any more. 1.2.8 had one, and it
/// never worked: the flag it wrote has not been on Roblox's allowlist since
/// September 2025, so the client read the file, ignored the entry and said
/// nothing. The page now explains that instead of offering it.</para>
/// </remarks>
public partial class MainWindow
{
    private readonly RobloxFastFlags _flags = new();

    private void WireTweaks()
    {
        FpsBoostButton.Click += (_, _) =>
            ReportTweaks(_flags.Apply(RobloxFastFlags.FpsBoost),
                "Lower graphics applied — textures, grass, anti-aliasing, lighting and part detail.");

        GraySkyButton.Click += (_, _) => ToggleGraySky();

        RestoreTweaksButton.Click += (_, _) =>
            ReportTweaks(_flags.Restore(), "Roblox's own settings are back.");

        ClearDeadFlagsButton.Click += (_, _) =>
            ReportTweaks(_flags.ClearDeadFlags(), "The old frame-rate entry has been taken out.");
    }

    private void ToggleGraySky()
    {
        bool on = GraySkyOn();

        bool worked = on
            ? _flags.Clear(new[] { RobloxFastFlags.SkyGrayFlag })
            : _flags.Apply(new[]
            {
                new FastFlag(RobloxFastFlags.SkyGrayFlag, "True",
                    "Replaces the skybox with flat grey.")
            });

        ReportTweaks(worked, on ? "The sky is back to normal." : "The sky is now flat grey.");
    }

    private bool GraySkyOn() =>
        _flags.Read().TryGetValue(RobloxFastFlags.SkyGrayFlag, out string? v) && v == "True";

    /// <summary>Puts the page in step with what is actually in Roblox's file.</summary>
    private void RefreshTweaks()
    {
        if (!_flags.RobloxFound)
        {
            TweaksStatus.Text = "No Roblox install found. Install Roblox, then come back.";
            SetTweaksEnabled(false);
            DeadFlagNotice.IsVisible = false;
            return;
        }

        SetTweaksEnabled(true);

        GraySkyButton.Content = GraySkyOn() ? "Sky: flat grey" : "Sky: normal";

        FpsBoostButton.Content = _flags.IsApplied(RobloxFastFlags.FpsBoost)
            ? "Lower graphics: on"
            : "Lower graphics";

        // Shown only to the people it happened to: anyone who set a frame-rate
        // target in 1.2.8 still has the entry, and is owed the news rather than
        // left believing it is doing something.
        IReadOnlyList<string> dead = _flags.DeadFlagsPresent();

        DeadFlagNotice.IsVisible = dead.Count > 0;

        TweaksStatus.Text = _flags.AnythingApplied()
            ? "Settings applied. Quit Roblox and open it again for them to take effect."
            : "Nothing applied — Roblox is running on its own settings.";
    }

    private void SetTweaksEnabled(bool on)
    {
        FpsBoostButton.IsEnabled = on;
        GraySkyButton.IsEnabled = on;
        RestoreTweaksButton.IsEnabled = on;
        ClearDeadFlagsButton.IsEnabled = on;
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
