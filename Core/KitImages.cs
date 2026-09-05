using System;
using System.IO;
using System.Linq;

namespace JinxyMac.Core;

/// <summary>
/// Pictures for kits, looked for beside the settings and then beside the app.
/// </summary>
/// <remarks>
/// Two folders, and the order matters. The install ships a picture for every
/// kit on the roster; the folder beside the settings holds the ones a person
/// chose themselves, and is searched first so their choice beats the shipped
/// one. Clearing a kit's picture therefore returns it to the shipped picture
/// rather than to no picture at all.
///
/// Named after the kit rather than listed in a file, so dropping images into
/// the folder by hand works exactly as well as choosing them in the app. A kit
/// with no picture in either place shows its name, which is what the list did
/// before there were any.
/// </remarks>
public static class KitImages
{
    /// <summary>Formats Avalonia's Skia decoder reads without an extra codec.</summary>
    private static readonly string[] Allowed = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    public static string FileFilter =>
        "Images|" + string.Join(";", Allowed.Select(e => "*" + e)) + "|All files|*.*";

    /// <summary>
    /// The file name a kit's picture is stored under.
    /// </summary>
    /// <remarks>
    /// Kit names are typed, so they can carry anything — slashes and colons
    /// included, which would send a write somewhere else entirely. Everything
    /// that is not a letter, digit, space or dash becomes an underscore.
    /// </remarks>
    public static string FileNameFor(string kit, string extension)
    {
        string safe = new string(kit
            .Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' ? c : '_')
            .ToArray())
            .Trim();

        return safe.Length == 0 ? "kit" + extension : safe + extension;
    }

    public static bool IsSupported(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Where kit pictures live.</summary>
    private static string Folder()
    {
        // Resolved through SettingsPath so the pictures sit beside everything
        // else the app keeps, and survive reinstalling.
        string marker = SettingsPath.For("kits.marker");
        string folder = Path.Combine(Path.GetDirectoryName(marker)!, "kits");

        Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>Where a kit's picture would be written, or empty if it cannot be.</summary>
    public static string PathFor(string kit, string extension)
    {
        try
        {
            return Path.Combine(Folder(), FileNameFor(kit, extension));
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// The pictures that came with the app.
    /// </summary>
    /// <remarks>
    /// Beside the executable rather than under the settings folder, because
    /// Setup put them there and the uninstaller has to be able to take them
    /// away again. Nothing is ever written here — an install folder is not
    /// somewhere a per-user app is entitled to write, and on a machine with
    /// several accounts it is not that account's to change.
    /// </remarks>
    private static string ShippedFolder() =>
        Path.Combine(AppContext.BaseDirectory, "kits");

    /// <summary>The picture for a kit, or null if it has none.</summary>
    /// <remarks>
    /// The chosen picture is looked for before the shipped one, so replacing a
    /// kit's art is a matter of putting a file in front of it rather than
    /// overwriting anything.
    /// </remarks>
    public static string? Find(string kit)
    {
        try
        {
            return FindIn(Folder(), kit) ?? FindIn(ShippedFolder(), kit);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindIn(string folder, string kit)
    {
        try
        {
            return Allowed
                .Select(ext => Path.Combine(folder, FileNameFor(kit, ext)))
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Copies a chosen picture in as this kit's.
    /// </summary>
    /// <returns>The stored path, or null if it could not be used.</returns>
    public static string? Set(string kit, string sourcePath)
    {
        if (!IsSupported(sourcePath) || !File.Exists(sourcePath)) return null;

        try
        {
            // Any earlier picture goes first, whatever format it was in, or a
            // kit ends up with two and the one found depends on list order.
            Remove(kit);

            string target = Path.Combine(Folder(),
                FileNameFor(kit, Path.GetExtension(sourcePath).ToLowerInvariant()));

            File.Copy(sourcePath, target, overwrite: true);

            return target;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deletes a kit's picture, whatever format it was stored in.</summary>
    public static void Remove(string kit)
    {
        try
        {
            string folder = Folder();

            foreach (string ext in Allowed)
            {
                string path = Path.Combine(folder, FileNameFor(kit, ext));

                if (File.Exists(path)) File.Delete(path);
            }
        }
        catch
        {
            // A locked picture is not worth failing over — it is overwritten
            // next time one is chosen.
        }
    }
}
