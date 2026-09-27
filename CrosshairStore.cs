using System;
using System.IO;
using Avalonia.Media.Imaging;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// Keeps the crosshair pictures people import, beside the other settings.
/// </summary>
/// <remarks>
/// An imported crosshair is a picture the user chose rather than one built from
/// a shape and a colour, so it has to live somewhere the app owns. It is copied
/// into a folder next to the settings — never referenced where it was picked
/// from, which would break the moment the original was moved, renamed or
/// deleted — and re-encoded to PNG on the way in, so an odd format or colour
/// profile cannot fail later, half way through replacing Roblox's cursors. The
/// stored name is a GUID, so two imports both called crosshair.png cannot
/// collide.
///
/// <para>Outside Core because decoding a picture means Avalonia, which Core does
/// not have.</para>
/// </remarks>
public static class CrosshairStore
{
    private static string Folder => Path.Combine(SettingsPath.Folder, "crosshairs");

    /// <summary>
    /// What the picker offers, and what can actually be decoded.
    /// </summary>
    /// <remarks>
    /// The formats Avalonia's Skia decoder handles, matching
    /// <see cref="Wallpaper.Allowed"/> rather than the Windows app's list: that
    /// one allows GIF, which is not decoded here, and leaves out webp, which is.
    /// Offering a format that cannot be read is a file picker that accepts a
    /// file and then says no.
    /// </remarks>
    public static readonly string[] Allowed = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

    /// <summary>
    /// Copies a picked picture into the crosshair folder as a PNG.
    /// </summary>
    /// <returns>The stored bare file name, or null if it could not be read.</returns>
    public static string? Store(string sourcePath)
    {
        try
        {
            using Bitmap? picture = CrosshairImage.TryLoadImage(sourcePath);
            if (picture == null) return null;

            Directory.CreateDirectory(Folder);

            string name = Guid.NewGuid().ToString("N") + ".png";

            using (FileStream file = File.Create(Path.Combine(Folder, name)))
                picture.Save(file);

            return name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The full path of a stored picture.</summary>
    public static string Resolve(string file) => Path.Combine(Folder, file);

    /// <summary>Removes a stored picture, if it is still there.</summary>
    public static void Delete(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return;

        try
        {
            string path = Path.Combine(Folder, file);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A leftover picture is harmless; failing to delete it is not worth
            // taking the app down for.
        }
    }
}
