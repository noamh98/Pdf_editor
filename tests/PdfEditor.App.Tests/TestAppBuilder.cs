using Avalonia;
using Avalonia.Headless;
using PdfEditor.App;

[assembly: AvaloniaTestApplication(typeof(PdfEditor.App.Tests.TestAppBuilder))]

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
