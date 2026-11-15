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
        {
            // Imported names go with it: they are in no fixed list here, so
            // without them "put it back" would leave a pasted preset behind.
            bool worked = _flags.Restore(_settings.ImportedFlags);

            if (worked && _settings.ImportedFlags.Count > 0)
            {
                _settings.ImportedFlags.Clear();
                _settings.Save();
            }

            ReportTweaks(worked, "Roblox's own settings are back.");
        };

        ClearDeadFlagsButton.Click += (_, _) =>
            ReportTweaks(_flags.ClearDeadFlags(), "The old frame-rate entry has been taken out.");

        FlagAccessSettingsButton.Click += (_, _) =>
            Open("x-apple.systempreferences:com.apple.preference.security?Privacy_AppManagement");

        FlagAccessCopyButton.Click += async (_, _) =>
        {
            if (Clipboard == null) return;

            await Clipboard.SetTextAsync(FlagAccessCommand.Text ?? "");
            FlagAccessCopyButton.Content = "Copied";
        };

        FlagAccessRecheckButton.Click += (_, _) =>
        {
            RefreshTweaks();

            if (_flags.Probe().CanWrite)
                TweaksStatus.Text = "That worked — Jinxy can change Roblox's settings now.";
        };
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
        // Asked before anything is offered, by actually attempting a write.
        // macOS blocks changes to another app's bundle unless it has been given
        // App Management, and Roblox's settings live inside Roblox — so on a
        // Mac without that permission every button on this page fails. Finding
        // that out on arrival beats finding it out by pressing one.
        FlagAccessReport access = _flags.Probe();

        ShowFlagAccess(access);

        if (!access.CanWrite)
        {
            TweaksStatus.Text = access.Explain;
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

        TweaksStatus.Text = _flags.AnythingApplied(_settings.ImportedFlags)
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

    /// <summary>
    /// Shows the permission notice, or hides it.
    /// </summary>
    /// <remarks>
    /// The Terminal command is offered because macOS does not always put an app
    /// in the App Management list at all — an app signed the way this one is may
    /// never appear there, and then the switch the notice asks for does not
    /// exist. The command creates the folder as root and hands it to the user,
    /// after which Jinxy writes it with no permission at all.
    ///
    /// <para>It is never run by the app. It needs an administrator password, and
    /// that is the user's to type rather than something to ask for on their
    /// behalf.</para>
    /// </remarks>
    private void ShowFlagAccess(FlagAccessReport access)
    {
        bool blocked = access.State == FlagAccess.Blocked;

        FlagAccessNotice.IsVisible = blocked;

        if (!blocked) return;

        FlagAccessText.Text = access.Explain;
        FlagAccessFixText.Text = access.Fix ?? "";
        FlagAccessCommand.Text = access.TerminalFix ?? "";
    }

    private void ReportTweaks(bool worked, string done)
    {
        if (!worked)
        {
            // The real reason, rather than one guess for every cause. The old
            // line said "close Roblox and try again" even when macOS was
            // refusing the write, which closing Roblox does nothing about.
            FlagAccessReport why =
                _flags.LastFailure ?? new FlagAccessReport(FlagAccess.Unknown);

            ShowFlagAccess(why);
            TweaksStatus.Text = why.Explain;
            return;
        }

        RefreshTweaks();
        TweaksStatus.Text = done + " Quit Roblox and open it again.";
    }
}
