using Avalonia;

namespace JinxyMac;

internal static class Program
{
    // Avalonia needs this before any synchronisation context exists, so it is
    // kept to the framework's own boilerplate and nothing else.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
