using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Every flag Roblox still honours from a local settings file.
    /// </summary>
    /// <remarks>
    /// Roblox restricted local client configuration to an allowlist on
    /// 29 September 2025. A flag outside it is <b>ignored in silence</b> — the
    /// file is still read, the entry is still there, and nothing happens. No
    /// error, no warning, nothing to notice.
    ///
    /// <para>That silence is exactly how this app shipped a frame-rate setting
    /// that never worked: it wrote the flag, read it back, found it, and
    /// reported success — because reading back your own write only proves that
    /// you wrote it. The list is kept here so that "will Roblox act on this?"
    /// has an answer in code, and <c>RobloxFastFlagsTests</c> fails the build if
    /// anything this app writes is not on it.</para>
    ///
    /// <para>Roblox says the list may change without warning, so this is a
    /// snapshot of the published one rather than a guarantee. Flags on it that
    /// macOS cannot use — the Direct3D, Vulkan and OpenGL backend preferences,
    /// and the alt-enter fullscreen handling — are listed for completeness and
    /// are not written by this app.</para>
    /// </remarks>
    public static IReadOnlySet<string> Allowlist { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // Geometry
            "DFIntCSGLevelOfDetailSwitchingDistance",
            "DFIntCSGLevelOfDetailSwitchingDistanceL12",
            "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34",

            // Rendering
            "FFlagHandleAltEnterFullscreenManually",
            "DFFlagTextureQualityOverrideEnabled",
            "DFIntTextureQualityOverride",
            "FIntDebugForceMSAASamples",
            "DFFlagDisableDPIScale",
            "FFlagDebugGraphicsPreferD3D11",
            "FFlagDebugSkyGray",
            "DFFlagDebugPauseVoxelizer",
            "DFIntDebugFRMQualityLevelOverride",
            "FIntFRMMaxGrassDistance",
            "FIntFRMMinGrassDistance",
            "FFlagDebugGraphicsPreferVulkan",
            "FFlagDebugGraphicsPreferOpenGL",

            // User interface
            "FIntGrassMovementReducedMotionFactor"
        };

    /// <summary>
    /// Flags this app used to write that Roblox does not act on.
    /// </summary>
    /// <remarks>
    /// <c>DFIntTaskSchedulerTargetFps</c> was the frame-rate target, and the
    /// headline of this page in 1.2.8. It is not on the allowlist and has done
    /// nothing since September 2025; a request to have it added is unanswered.
    ///
    /// <para>Named here rather than simply deleted, for two reasons. Anyone who
    /// used that version has the entry sitting in their settings file and should
    /// be told it is inert rather than left believing in it. And naming it stops
    /// it being reintroduced by somebody reading one of the many guides that
    /// still recommend it.</para>
    /// </remarks>
    public static IReadOnlyList<string> DeadFlags { get; } = new[]
    {
        "DFIntTaskSchedulerTargetFps"
    };

    /// <summary>
    /// The FPS-boost set: less for the machine to draw, so the frames it can
    /// draw come faster.
    /// </summary>
    /// <remarks>
    /// Ported from the Windows build apart from the graphics-backend flags,
    /// which are Direct3D, Vulkan and OpenGL — none of which macOS uses. There
    /// is no Metal equivalent on the allowlist, so that choice is simply absent
    /// here rather than faked.
    ///
    /// <para>Every flag in here is on <see cref="Allowlist"/>, which is the only
    /// reason any of it does anything. This is the half of the page that works:
    /// the frame-rate target that used to sit above it did not.</para>
    /// </remarks>
    public static IReadOnlyList<FastFlag> FpsBoost { get; } = new[]
    {
        new FastFlag("DFIntCSGLevelOfDetailSwitchingDistance", "1",
            "Unioned parts drop to simpler shapes immediately rather than at distance."),
        new FastFlag("DFIntCSGLevelOfDetailSwitchingDistanceL12", "1",
            "The same, for the next level of detail down."),
        new FastFlag("DFIntCSGLevelOfDetailSwitchingDistanceL23", "1",
            "And the next."),
        new FastFlag("DFIntCSGLevelOfDetailSwitchingDistanceL34", "1",
            "And the last, so the simplest shape is used throughout."),
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

    /// <summary>
    /// Flags left in the file that Roblox ignores.
    /// </summary>
    /// <remarks>
    /// Only ever written by an older version of this app. Reported rather than
    /// quietly deleted, because somebody who set a frame-rate target in 1.2.8 is
    /// owed the news that it never did anything.
    /// </remarks>
    public IReadOnlyList<string> DeadFlagsPresent()
    {
        Dictionary<string, string> current = Read();

        return DeadFlags.Where(current.ContainsKey).ToList();
    }

    /// <summary>Removes the flags Roblox ignores, if any are there.</summary>
    public bool ClearDeadFlags() =>
        DeadFlagsPresent().Count > 0 && Clear(DeadFlags);

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

    /// <summary>Whether anything this app put there is currently in the file.</summary>
    /// <param name="alsoCount">
    /// Names imported from a pasted preset, which the caller records. Without
    /// them the page would report nothing applied while an imported preset was
    /// sitting in the file.
    /// </param>
    public bool AnythingApplied(IEnumerable<string>? alsoCount = null)
    {
        Dictionary<string, string> current = Read();

        return current.ContainsKey(SkyGrayFlag)
               || FpsBoost.Any(f => current.ContainsKey(f.Name))
               || DeadFlags.Any(current.ContainsKey)
               || (alsoCount?.Any(current.ContainsKey) ?? false);
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
    /// <param name="alsoClear">
    /// Names imported from a pasted preset. They are not in any fixed list here,
    /// so the caller passes what it recorded — otherwise "put it back" would
    /// clear the settings this app offers and leave an imported preset behind.
    /// </param>
    public bool Restore(IEnumerable<string>? alsoClear = null)
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

            var ours = new List<string> { SkyGrayFlag };
            ours.AddRange(FpsBoost.Select(f => f.Name));

            // The dead ones too: a flag this app wrote is this app's to tidy up,
            // whether or not Roblox was ever going to read it.
            ours.AddRange(DeadFlags);

            if (alsoClear != null) ours.AddRange(alsoClear);

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
