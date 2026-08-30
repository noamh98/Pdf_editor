using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PdfEditor.App.Services;
using PdfEditor.App.ViewModels;
using PdfEditor.App.Views;

namespace PdfEditor.App;

/// <summary>
/// The Avalonia application object.
/// </summary>
/// <remarks>
/// Named <c>PdfEditorApp</c> rather than <c>App</c> because the assembly's root namespace is
/// already <c>PdfEditor.App</c>.
/// </remarks>
public sealed class PdfEditorApp : Application
{
    /// <summary>Services for the running instance; null while a headless test is driving the app.</summary>
    public AppServices? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Services = AppServices.Create();
            var viewModel = new MainWindowViewModel(Services);
            ThemeApplier.Apply(this, Services.Settings.Theme);

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += (_, _) => Services.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
