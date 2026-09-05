using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(JinxyMac.Tests.TestAppBuilder))]

namespace JinxyMac.Tests;

/// <summary>
/// Boots Avalonia's headless platform for the tests that need one.
/// </summary>
/// <remarks>
/// <c>UseHeadlessDrawing = false</c> is the whole point: the default headless
/// platform is a no-op renderer built for UI tests that only need something
/// standing in for a window. Turned off, decoding and drawing run through the
/// real Skia backend instead — the same one the shipped app uses — so a test
/// that reads a <see cref="Avalonia.Media.Imaging.Bitmap"/> is exercising the
/// actual codec, not a stub.
///
/// Every test that touches <c>Bitmap</c> or the types <c>KitArtImage</c>
/// builds from it needs this: the platform is set up lazily, the first time a
/// test decorated <c>[AvaloniaFact]</c> runs, rather than eagerly for the
/// whole assembly — a plain <c>[Fact]</c> gets no renderer at all and a
/// decode either returns null or throws looking for one. Some of those
/// types — <c>CroppedBitmap</c> among them — also derive from
/// <c>AvaloniaObject</c> and enforce being touched only from the UI thread,
/// which a plain xunit test does not run on either.
/// <c>Avalonia.Headless.XUnit</c>'s <c>[AvaloniaFact]</c> is what sets up
/// both: the renderer, and the thread.
/// </remarks>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
