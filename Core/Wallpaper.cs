using System.Reflection;

namespace JinxyMac.Core;

/// <summary>
/// A picture used as the window's background, kept where the app can find it.
/// </summary>
/// <remarks>
/// The chosen file is copied into the settings folder rather than referenced
/// where it sits. Someone picks a screenshot out of Downloads, empties
/// Downloads a week later, and the background they set is gone — with a copy it
/// simply is not, and the settings file stores a bare name that means the same
/// thing on any machine instead of a path that means nothing on another.
/// </remarks>
public static class Wallpaper
{
    /// <summary>Formats Avalonia's Skia decoder handles.</summary>
    public static readonly string[] Allowed = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

    /// <summary>The stored copy's name, minus the extension carried from the source.</summary>
    private const string StoredStem = "wallpaper";

    public static bool IsSupported(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string extension = Path.GetExtension(path);

        return Allowed.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The name a copy of this file is stored under.</summary>
    /// <remarks>
    /// One stem for every wallpaper, so choosing a new one replaces the old
    /// rather than leaving the settings folder to fill up with pictures nobody
    /// can see any more. The extension is kept because the decoder is picked
    /// from it.
    /// </remarks>
    public static string StoredNameFor(string sourcePath) =>
        StoredStem + Path.GetExtension(sourcePath).ToLowerInvariant();

    /// <summary>
    /// How far the picture is darkened, as a percentage.
    /// </summary>
    /// <remarks>
    /// Never fully opaque: a hand-edited 100 would paint the wallpaper out
    /// entirely and read as the feature being broken rather than as the value
    /// being wrong.
    /// </remarks>
    public const int MinDimming = 0;
    public const int MaxDimming = 90;
    public const int DefaultDimming = 45;

    public static int ClampDimming(int percent) =>
        Math.Clamp(percent, MinDimming, MaxDimming);

    /// <summary>The dimming overlay's opacity, from the stored percentage.</summary>
    public static double DimmingOpacity(int percent) => ClampDimming(percent) / 100.0;

    /// <summary>
    /// Copies the picked file next to the settings and returns its stored name.
    /// </summary>
    /// <returns>The bare file name to save, or null if it could not be copied.</returns>
    public static string? Store(string sourcePath)
    {
        if (!IsSupported(sourcePath) || !File.Exists(sourcePath)) return null;

        try
        {
            string name = StoredNameFor(sourcePath);
            string target = SettingsPath.For(name);

            // Copying a file over itself throws. Re-picking the stored copy is
            // a no-op, not a failure.
            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(target),
                    StringComparison.OrdinalIgnoreCase))
            {
                Clear();
                File.Copy(sourcePath, target, overwrite: true);
            }

            return name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Full path of a stored wallpaper, or null if there is not one.</summary>
    public static string? Resolve(string? storedName)
    {
        if (string.IsNullOrWhiteSpace(storedName)) return null;

        // A bare name is what gets stored. Anything carrying a directory came
        // from a hand-edited settings file and is not followed.
        //
        // The forward-slash and backslash checks are written out by hand
        // instead of leaning on Path.GetFileName alone because that method's
        // idea of a separator is platform-dependent: on Windows a backslash
        // splits a directory from a name, but on macOS — the platform this
        // app ships to — it is just another character, so a value like
        // C:\Windows\System32\config or a UNC share sails through unchanged.
        // A guard that only holds on the platform it was written on, and not
        // the one it runs on, is not a guard. Path.IsPathRooted and the
        // GetFileName comparison stay too, to catch whatever a rooted path
        // on either platform's own convention slips past the two literal
        // checks.
        if (storedName.Contains('/') ||
            storedName.Contains('\\') ||
            Path.IsPathRooted(storedName) ||
            storedName != Path.GetFileName(storedName))
        {
            return null;
        }

        try
        {
            string path = SettingsPath.For(storedName);

            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Manifest name of the wallpaper bundled inside the assembly.</summary>
    /// <remarks>
    /// Pinned by the csproj's LogicalName so moving the source file cannot
    /// silently break this lookup. Kept as PNG deliberately — every extension
    /// listed in <see cref="Allowed"/> would need decoder-side treatment, and
    /// the shipped one only has to be a single format.
    /// </remarks>
    private const string DefaultResourceName = "DefaultWallpaper.png";

    /// <summary>Extension the default is stored under. Matches the resource.</summary>
    private const string DefaultStoredExtension = ".png";

    /// <summary>
    /// Lays the shipped wallpaper down in the settings folder the first time,
    /// so a fresh install has a background without the user picking one.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="AppSettings.Load"/> when there is no settings file
    /// yet, which is the one reliable signal for "brand new install" — someone
    /// who has settings and no wallpaper cleared it on purpose, and reinstalling
    /// it under them would read as the app deciding for itself.
    ///
    /// The resource is copied out rather than referenced in place because the
    /// window's binding is a file path, not a stream, and the rest of the app
    /// already assumes the picture lives beside the settings.
    /// </remarks>
    /// <returns>The stored name, or empty if the resource could not be laid down.</returns>
    public static string InstallDefault()
    {
        try
        {
            string storedName = StoredStem + DefaultStoredExtension;
            string target = SettingsPath.For(storedName);

            // Never overwrite. A settings folder that already carries this file
            // belongs to an earlier install path — respecting it makes the
            // default install a first-run-only act rather than a self-repair.
            if (File.Exists(target)) return storedName;

            using Stream? source = typeof(Wallpaper).Assembly.GetManifestResourceStream(DefaultResourceName);
            if (source == null) return "";

            using var destination = File.Create(target);
            source.CopyTo(destination);

            return storedName;
        }
        catch
        {
            // A failed install of the default is not a failure worth crashing
            // over — an empty return produces no wallpaper, which is what the
            // app already handles for every other missing-file case.
            return "";
        }
    }

    /// <summary>Deletes any stored copy, whatever extension it was saved under.</summary>
    public static void Clear()
    {
        foreach (string extension in Allowed)
        {
            try
            {
                string path = SettingsPath.For(StoredStem + extension);

                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // A locked file is not worth failing the whole removal over —
                // the setting is cleared either way and it is overwritten next
                // time one is chosen.
            }
        }
    }
}
