using Avalonia;

namespace PdfEditor.App;

internal static class Program
{
    /// <summary>
    /// Entry point. Nothing here touches the network, and no work happens before the UI is up.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            // A failure this early cannot be shown in the UI, so record it locally and exit.
            CrashLog.Write(e);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PdfEditorApp>()
        .UsePlatformDetect()
        .LogToTrace();
}
