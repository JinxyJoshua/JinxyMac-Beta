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
}
