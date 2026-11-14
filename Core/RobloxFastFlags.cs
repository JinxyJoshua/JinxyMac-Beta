using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JinxyMac.Core;

/// <summary>One Roblox client setting, with the plain-English reason for it.</summary>
public readonly record struct FastFlag(string Name, string Value, string Why);

/// <summary>
/// Roblox's own client settings, written where the Mac client reads them.
/// </summary>
/// <remarks>
/// The same mechanism every FPS unlocker uses: a <c>ClientAppSettings.json</c>
/// that the client reads at startup. On Windows it lives in a per-version
/// folder; on macOS it is inside the application bundle, at
/// <c>Roblox.app/Contents/MacOS/ClientSettings/</c> — the same bundle the
/// crosshair writes cursor images into, which is already known to work and to
/// survive a relaunch.
///
/// <para><b>Only Roblox's published allowlist.</b> These are the settings Roblox
/// documents as fair to change. Nothing here touches character physics, which
/// is the category that gets accounts banned and the reason the list is a list
/// rather than anything the user can type. If Roblox retires one it simply stops
/// taking effect; it does not become a violation.</para>
///
/// <para>Reversible the same way the cursors are: the client's own file is
/// copied aside before it is first touched, and Restore puts it back. Merging
/// rather than replacing, so a flag set by hand or by a bootstrapper survives.</para>
///
/// <para>A Roblox update replaces the bundle and takes the settings with it, so
/// applying again after an update is expected — the page says so.</para>
/// </remarks>
public sealed class RobloxFastFlags
{
    private const string SettingsFileName = "ClientAppSettings.json";
    private const string BackupFileName = "ClientAppSettings.jinxy-backup.json";

    private readonly IReadOnlyList<string> _searchRoots;

    /// <param name="searchRoots">
    /// Folders that may hold <c>Roblox.app</c>. Defaults to the two places a Mac
    /// install lands.
    /// </param>
    public RobloxFastFlags(IEnumerable<string>? searchRoots = null)
    {
        _searchRoots = searchRoots is null ? DefaultRoots() : new List<string>(searchRoots);
    }

    private static List<string> DefaultRoots()
    {
        var roots = new List<string> { "/Applications" };

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) roots.Add(Path.Combine(home, "Applications"));

        return roots;
    }

    /// <summary>
    /// The frame-rate target. Roblox on a Mac renders at 60 unless this raises it.
    /// </summary>
    /// <remarks>
    /// The single most-asked-for setting on the Mac build, and the one that does
    /// not depend on any permission: it is a file write, so it works even while
    /// Accessibility is refusing the clicker.
    /// </remarks>
    public const string FpsCapFlagName = "DFIntTaskSchedulerTargetFps";

    /// <summary>Above any display refresh, for an effectively uncapped target.</summary>
    public const int UnlimitedFps = 9999;

    /// <summary>
    /// The FPS-boost set: less for the machine to draw, so the frames it can
    /// draw come faster.
    /// </summary>
    /// <remarks>
    /// Ported unchanged from the Windows build apart from the graphics-backend
    /// flags, which are Direct3D, Vulkan and OpenGL — none of which macOS uses.
    /// There is no Metal equivalent on the allowlist, so that choice is simply
    /// absent here rather than faked.
    /// </remarks>
    public static IReadOnlyList<FastFlag> FpsBoost { get; } = new[]
    {
        new FastFlag("DFFlagTextureQualityOverrideEnabled", "True",
            "Lets the texture quality below be applied at all."),
        new FastFlag("DFIntTextureQualityOverride", "0",
            "Lowest texture detail. The largest single saving on a weak GPU."),
        new FastFlag("DFIntDebugFRMQualityLevelOverride", "1",
            "Pins the overall render quality to its lowest level."),
        new FastFlag("FIntDebugForceMSAASamples", "0",
            "Turns off anti-aliasing."),
        new FastFlag("FIntFRMMaxGrassDistance", "0",
            "Stops grass rendering at distance."),
        new FastFlag("FIntFRMMinGrassDistance", "0",
            "Stops grass rendering up close."),
        new FastFlag("FIntGrassMovementReducedMotionFactor", "0",
            "Removes grass animation."),
        new FastFlag("DFFlagDisableDPIScale", "True",
            "Stops the client scaling for display DPI. A real saving on a Retina screen."),
        new FastFlag("DFFlagDebugPauseVoxelizer", "True",
            "Freezes voxel lighting. Lighting stops updating, which is the cost.")
    };

    /// <summary>
    /// The grey sky, offered on its own as well as inside the boost bundle.
    /// </summary>
    public const string SkyGrayFlag = "FFlagDebugSkyGray";

    /// <summary>The folder the Mac client reads its settings from, if Roblox is installed.</summary>
    public string? FindClientSettingsFolder()
    {
        foreach (string root in _searchRoots)
        {
            string bundle = Path.Combine(root, "Roblox.app");
            if (!Directory.Exists(bundle)) continue;

            return Path.Combine(bundle, "Contents", "MacOS", "ClientSettings");
        }

        return null;
    }

    /// <summary>Whether a Roblox install was found to write to at all.</summary>
    public bool RobloxFound => FindClientSettingsFolder() != null;

    private string? SettingsPath()
    {
        string? folder = FindClientSettingsFolder();
        return folder == null ? null : Path.Combine(folder, SettingsFileName);
    }

    /// <summary>Everything currently in the client's settings file.</summary>
    public Dictionary<string, string> Read()
    {
        try
        {
            string? path = SettingsPath();
            if (path == null || !File.Exists(path)) return new Dictionary<string, string>();

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            // A hand-edited file that will not parse reads as empty rather than
            // taking the page down. Applying over it rewrites it cleanly.
            return new Dictionary<string, string>();
        }
    }

    public bool IsApplied(IReadOnlyList<FastFlag> flags)
    {
        Dictionary<string, string> current = Read();

        return flags.Count > 0
               && flags.All(f => current.TryGetValue(f.Name, out string? value) && value == f.Value);
    }

    /// <summary>
    /// Merges flags into the existing file, after copying the original aside.
    /// </summary>
    /// <returns>False if Roblox could not be found or the file could not be written.</returns>
    public bool Apply(IReadOnlyList<FastFlag> flags)
    {
        string? folder = FindClientSettingsFolder();
        if (folder == null) return false;

        try
        {
            Directory.CreateDirectory(folder);
            Backup();

            Dictionary<string, string> settings = Read();
            foreach (FastFlag flag in flags) settings[flag.Name] = flag.Value;

            Write(settings);
            return true;
        }
        catch
        {
            // Roblox running, or a bundle this user cannot write to.
            return false;
        }
    }

    /// <summary>Removes the named flags, leaving anything else in the file alone.</summary>
    public bool Clear(IEnumerable<string> flagNames)
    {
        if (FindClientSettingsFolder() == null) return false;

        try
        {
            Dictionary<string, string> settings = Read();
            foreach (string name in flagNames) settings.Remove(name);

            Write(settings);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Sets the frame-rate target, or clears it for the client's own 60.</summary>
    /// <remarks>
    /// 60 or below is the default and is stored as no flag at all rather than as
    /// the number 60 — writing the default would leave a setting behind that
    /// looks deliberate and outlives the reason for it.
    /// </remarks>
    public bool SetFpsCap(int? fps)
    {
        if (fps is null or <= 60) return Clear(new[] { FpsCapFlagName });

        return Apply(new[]
        {
            new FastFlag(FpsCapFlagName,
                fps.Value.ToString(CultureInfo.InvariantCulture),
                "Frame-rate target. Roblox renders up to this many FPS.")
        });
    }

    /// <summary>The target in the file, or null when the client's 60 applies.</summary>
    public int? CurrentFpsCap() =>
        Read().TryGetValue(FpsCapFlagName, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps)
            ? fps
            : null;

    /// <summary>
    /// Copies the client's own settings aside, once.
    /// </summary>
    /// <remarks>
    /// Only if no backup exists, so it captures the file as it was before this
    /// app first touched it. Backing up on every apply would quickly leave a
    /// "backup" of this app's own output — the same mistake the cursor backups
    /// are written to avoid.
    /// </remarks>
    public void Backup()
    {
        try
        {
            string? path = SettingsPath();
            if (path == null || !File.Exists(path)) return;

            string backup = Path.Combine(Path.GetDirectoryName(path)!, BackupFileName);
            if (!File.Exists(backup)) File.Copy(path, backup);
        }
        catch
        {
            // A missing backup must not stop the flags being applied; Restore
            // falls back to removing the flags this app knows about.
        }
    }

    /// <summary>Whether anything this app writes is currently in the file.</summary>
    public bool AnythingApplied()
    {
        Dictionary<string, string> current = Read();

        return current.ContainsKey(FpsCapFlagName)
               || current.ContainsKey(SkyGrayFlag)
               || FpsBoost.Any(f => current.ContainsKey(f.Name));
    }

    /// <summary>
    /// Puts the client's settings back as they were.
    /// </summary>
    /// <remarks>
    /// The backup when there is one, since that also undoes anything this app
    /// overwrote. Otherwise every flag this app knows how to write is removed,
    /// which leaves a file the user built elsewhere intact rather than deleting
    /// settings that were never ours.
    /// </remarks>
    public bool Restore()
    {
        string? path = SettingsPath();
        if (path == null) return false;

        try
        {
            string backup = Path.Combine(Path.GetDirectoryName(path)!, BackupFileName);

            if (File.Exists(backup))
            {
                File.Copy(backup, path, overwrite: true);
                File.Delete(backup);
                return true;
            }

            var ours = new List<string> { FpsCapFlagName, SkyGrayFlag };
            ours.AddRange(FpsBoost.Select(f => f.Name));

            return Clear(ours);
        }
        catch
        {
            return false;
        }
    }

    private void Write(Dictionary<string, string> settings)
    {
        string? path = SettingsPath();
        if (path == null) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllText(path,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
