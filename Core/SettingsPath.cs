namespace JinxyMac.Core;

/// <summary>
/// Where settings live, which is not the same place on each platform.
/// </summary>
/// <remarks>
/// ApplicationData resolves to ~/Library/Application Support on macOS and
/// %APPDATA% on Windows, so one call covers both and neither ends up writing
/// beside the executable — which on macOS would be inside the .app bundle.
/// </remarks>
public static class SettingsPath
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JinxyMac");

    public static string For(string fileName)
    {
        try
        {
            Directory.CreateDirectory(Folder);
        }
        catch
        {
            // A settings file that cannot be placed is not worth a crash;
            // callers already treat a missing file as "use the defaults".
        }

        return Path.Combine(Folder, fileName);
    }

    /// <summary>
    /// Writes text to a settings file the way <c>File.WriteAllText</c> won't:
    /// without ever leaving the file half-written.
    /// </summary>
    /// <remarks>
    /// <c>File.WriteAllText</c> truncates the target the instant it opens it,
    /// before a single byte of the new content lands. Every store under this
    /// path is written that way, and every one of them is exposed to the same
    /// moment: force-quitting an unresponsive app — which is exactly what
    /// people do to an autoclicker that has locked up — or a power cut, right
    /// inside that window. What is left on disk is zero bytes or half a JSON
    /// object, and every loader in this app treats a file it cannot parse as
    /// "use the defaults" — so the very next save writes those defaults back
    /// over whatever was actually there, for good. Hotkeys, presets, saved
    /// wheels, macros, lifetime click history: all of it is one bad-timed kill
    /// away from being replaced with nothing, silently.
    ///
    /// Writing the new content to a temp file first and only then moving it
    /// over the real path (same trick <see cref="Wallpaper.Store"/> uses for a
    /// picture copy, and for the identical reason) closes that window. The
    /// move is a single atomic rename on both platforms this app ships to, so
    /// a reader sees either the whole of the old file or the whole of the new
    /// one — never a mix, and never neither. A kill mid-write costs at worst
    /// the temp file; the real one is untouched because it was never opened
    /// for writing at all.
    /// </remarks>
    public static void WriteAtomic(string path, string contents)
    {
        string temp = path + ".tmp";

        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // Best effort, same as Wallpaper.Store: a stray temp file is
                // harmless and is overwritten the next time this runs.
            }

            // Rethrown rather than swallowed here: every caller already
            // wraps its own save in a try/catch that decides what a failed
            // save means for it (usually: nothing, the in-memory state is
            // still fine). This helper's job is only to make sure that
            // failure never costs the file that was already on disk.
            throw;
        }
    }
}
