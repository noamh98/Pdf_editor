using Avalonia;
using Avalonia.Headless;
using PdfEditor.App;

[assembly: AvaloniaTestApplication(typeof(PdfEditor.App.Tests.TestAppBuilder))]

// Every headless test is dispatched onto one session thread, and that session pumps each test with
// a nested dispatcher frame. xUnit's default is to run collections concurrently, so several tests
// ask the same dispatcher for a frame at once — which on this platform surfaces as an intermittent
// PlatformNotSupportedException from Dispatcher.PushFrame, on whichever test happened to be next.
// The tests are serialised on that thread whatever this setting says, so disabling parallelisation
// costs nothing and removes the race.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace PdfEditor.App.Tests;

/// <summary>
/// Boots the real application object without a display, so the window, its XAML and its styles are
/// exercised for real rather than mocked.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PdfEditorApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
