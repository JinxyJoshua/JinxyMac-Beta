using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// Importing a pasted set of Roblox flags.
/// </summary>
/// <remarks>
/// Two steps on purpose: check, then apply. The verdict is the valuable part —
/// since Roblox's allowlist landed, a preset passed around on a forum is usually
/// mostly inert, and somebody should see that before anything is written rather
/// than after. A one-press import would be the 1.2.8 mistake again, this time
/// handed to the user to repeat.
///
/// <para>The reading and sorting is <see cref="FastFlagImport"/>, with no screen
/// near it and tested. This shows the result and writes the part that works.</para>
/// </remarks>
public partial class MainWindow
{
    private FlagImport? _pendingImport;

    private void WireFlagImport()
    {
        ImportFlagsCheckButton.Click += (_, _) => CheckImport();

        ImportFlagsApplyButton.Click += (_, _) => ApplyImport();

        ImportFlagsClearButton.Click += (_, _) =>
        {
            ImportFlagsBox.Text = "";
            ShowImport(null);
        };

        ExportFlagsButton.Click += async (_, _) => await ExportFlagsAsync();
    }

    private void CheckImport() => ShowImport(FastFlagImport.Read(ImportFlagsBox.Text));

    /// <summary>Puts the verdict on screen, or clears it.</summary>
    private void ShowImport(FlagImport? result)
    {
        _pendingImport = result;

        if (result == null)
        {
            ImportFlagsResult.IsVisible = false;
            return;
        }

        ImportFlagsResult.IsVisible = true;
        ImportFlagsSummary.Text = result.Summary;

        // Red only for a paste that gave nothing at all. A mixed result is the
        // ordinary outcome, not an error.
        ImportFlagsSummary.Foreground = result.AnythingToApply
            ? Themed("Text", Colors.White)
            : Themed("Accent", Colors.OrangeRed);

        ImportFlagsDetail.Children.Clear();

        if (result.Supported.Count > 0)
            AddImportLine("Roblox will use", result.Supported.Select(f => f.Name), "Accent");

        if (result.Ignored.Count > 0)
            AddImportLine(
                "Roblox ignores these — not on its approved list, so they do nothing",
                result.Ignored, "TextMuted");

        if (result.Risky.Count > 0)
            AddImportLine(
                "Not written: these touch how your character moves, which is the kind "
                + "that gets accounts banned",
                result.Risky, "Accent");

        if (result.Unreadable.Count > 0)
            AddImportLine("Not flags", result.Unreadable, "TextMuted");

        ImportFlagsApplyButton.IsEnabled = result.AnythingToApply;

        ImportFlagsApplyButton.Content = result.AnythingToApply
            ? $"Apply {result.Supported.Count}"
            : "Nothing to apply";
    }

    private void AddImportLine(string heading, IEnumerable<string> names, string colourKey)
    {
        ImportFlagsDetail.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Themed(colourKey, Colors.Gray),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 620,
            Margin = new Avalonia.Thickness(0, 10, 0, 2)
        });

        ImportFlagsDetail.Children.Add(new TextBlock
        {
            Text = string.Join(", ", names),
            FontSize = 12,
            Foreground = Themed("TextSoft", Colors.Gray),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 620
        });
    }

    private void ApplyImport()
    {
        if (_pendingImport is not { AnythingToApply: true } result) return;

        if (!_flags.Apply(result.Supported))
        {
            TweaksStatus.Text = "Could not write Roblox's settings. Close Roblox and try again.";
            return;
        }

        // Remembered so Restore can take them out again. Without this, Restore
        // would clear the settings this app offers and leave an imported preset
        // behind, which is not what "put it back" means.
        foreach (FastFlag flag in result.Supported)
            if (!_settings.ImportedFlags.Contains(flag.Name))
                _settings.ImportedFlags.Add(flag.Name);

        _settings.Save();

        int applied = result.Supported.Count;

        ImportFlagsBox.Text = "";
        ShowImport(null);

        RefreshTweaks();

        TweaksStatus.Text =
            $"Imported {applied} setting{(applied == 1 ? "" : "s")}. "
            + "Quit Roblox and open it again.";
    }

    /// <summary>Puts Roblox's current settings on the clipboard.</summary>
    /// <remarks>
    /// The other half of importing: it is how somebody shares what they are
    /// running, or keeps a copy before trying somebody else's preset.
    /// </remarks>
    private async Task ExportFlagsAsync()
    {
        if (Clipboard is not { } clipboard) return;

        Dictionary<string, string> current = _flags.Read();

        if (current.Count == 0)
        {
            TweaksStatus.Text = "Roblox's settings file is empty, so there is nothing to copy.";
            return;
        }

        string json = JsonSerializer.Serialize(
            current, new JsonSerializerOptions { WriteIndented = true });

        try
        {
            await clipboard.SetTextAsync(json);

            TweaksStatus.Text =
                $"Copied {current.Count} setting{(current.Count == 1 ? "" : "s")} to the clipboard.";
        }
        catch (Exception)
        {
            // The clipboard can be held by another app. Not worth more than a line.
            TweaksStatus.Text = "Could not reach the clipboard. Try again in a moment.";
        }
    }
}
