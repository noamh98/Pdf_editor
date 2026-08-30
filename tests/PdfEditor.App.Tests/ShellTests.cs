using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using PdfEditor.App.Services;
using PdfEditor.App.ViewModels;
using PdfEditor.App.Views;
using PdfEditor.Core.Settings;
using Xunit;

namespace PdfEditor.App.Tests;

/// <summary>A services graph rooted in a temporary directory, removed when the test ends.</summary>
public sealed class ServicesFixture : IDisposable
{
    public ServicesFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "pdfeditor-ui", Guid.NewGuid().ToString("N"));
        Services = AppServices.CreateForRoot(Root);
    }

    public string Root { get; }
    public AppServices Services { get; }

    public void Dispose()
    {
        Services.Dispose();
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public class MainWindowTests
{
    private static (MainWindow Window, MainWindowViewModel ViewModel, ServicesFixture Fixture) Create()
    {
        var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        return (window, viewModel, fixture);
    }

    [AvaloniaFact]
    public void WindowConstructsAndItsXamlLoads()
    {
        var (window, _, fixture) = Create();
        using (fixture)
        {
            Assert.NotNull(window.Content);
            Assert.Contains("עורך PDF", window.Title);
        }
    }

    [AvaloniaFact]
    public void TheWholeWindowIsRightToLeft()
    {
        var (window, _, fixture) = Create();
        using (fixture)
        {
            Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
        }
    }

    [AvaloniaFact]
    public void ThumbnailsSitOnTheLeadingSideWhichIsTheRightUnderRtl()
    {
        var (window, _, fixture) = Create();
        using (fixture)
        {
            var thumbnails = window.FindControl<Border>("ThumbnailsPanel");
            var properties = window.FindControl<Border>("PropertiesPanel");
            var documentArea = window.FindControl<Panel>("DocumentArea");

            Assert.NotNull(thumbnails);
            Assert.NotNull(properties);
            Assert.NotNull(documentArea);

            // Column 0 is the leading column, which a right-to-left flow direction puts on the right,
            // and the properties panel takes the trailing column on the left.
            Assert.Equal(0, Grid.GetColumn(thumbnails!));
            Assert.Equal(1, Grid.GetColumn(documentArea!));
            Assert.Equal(2, Grid.GetColumn(properties!));
        }
    }

    [AvaloniaFact]
    public void TheEmptyStateIsShownWhenNoDocumentIsOpen()
    {
        var (window, viewModel, fixture) = Create();
        using (fixture)
        {
            Assert.True(viewModel.IsEmpty);
            Assert.False(viewModel.HasDocument);
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(texts, t => t is not null && t.Contains("גררו לכאן"));
        }
    }

    [AvaloniaFact]
    public void DocumentCommandsAreUnavailableWithoutADocument()
    {
        var (_, viewModel, fixture) = Create();
        using (fixture)
        {
            Assert.False(viewModel.SaveCommand.CanExecute(null));
            Assert.False(viewModel.SaveAsCommand.CanExecute(null));
            Assert.False(viewModel.ExportCommand.CanExecute(null));
            Assert.False(viewModel.PrintCommand.CanExecute(null));
            Assert.False(viewModel.SplitCommand.CanExecute(null));
            Assert.False(viewModel.UndoCommand.CanExecute(null));
            Assert.False(viewModel.RedoCommand.CanExecute(null));
            Assert.False(viewModel.DeleteSelectionCommand.CanExecute(null));

            // Opening and merging do not need one.
            Assert.True(viewModel.OpenCommand.CanExecute(null));
            Assert.True(viewModel.MergeCommand.CanExecute(null));
        }
    }

    [AvaloniaFact]
    public void EveryToolIsAvailableAndSelectionIsTheDefault()
    {
        var (_, viewModel, fixture) = Create();
        using (fixture)
        {
            Assert.Equal(11, viewModel.Toolbox.Tools.Count);
            Assert.Equal(EditorTool.Select, viewModel.Toolbox.ActiveTool);
            Assert.True(viewModel.Toolbox.Tools.Single(t => t.Tool == EditorTool.Select).IsSelected);
        }
    }

    [AvaloniaFact]
    public void SelectingAToolDeselectsThePreviousOne()
    {
        var (_, viewModel, fixture) = Create();
        using (fixture)
        {
            viewModel.Toolbox.Select(EditorTool.Rectangle);

            Assert.Equal(EditorTool.Rectangle, viewModel.Toolbox.ActiveTool);
            Assert.Single(viewModel.Toolbox.Tools.Where(t => t.IsSelected));
            Assert.False(viewModel.Toolbox.IsSelectionTool);
        }
    }

    [AvaloniaFact]
    public void EveryToolCarriesAnAccessibleName()
    {
        var (_, viewModel, fixture) = Create();
        using (fixture)
        {
            Assert.All(viewModel.Toolbox.Tools, tool =>
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.Label));
                Assert.False(string.IsNullOrWhiteSpace(tool.AccessibleName));
                Assert.False(string.IsNullOrWhiteSpace(tool.Glyph));
            });
        }
    }

    [AvaloniaFact]
    public void EscapeReturnsToTheSelectionTool()
    {
        var (_, viewModel, fixture) = Create();
        using (fixture)
        {
            viewModel.Toolbox.Select(EditorTool.Ink);
            viewModel.ClearSelectionCommand.Execute(null);
            Assert.Equal(EditorTool.Select, viewModel.Toolbox.ActiveTool);
        }
    }

    [AvaloniaFact]
    public void ThemeCyclesThroughSystemLightAndDark()
    {
        var (_, viewModel, fixture) = Create();
        using (fixture)
        {
            Assert.Equal(ThemePreference.System, viewModel.Theme);

            viewModel.ToggleThemeCommand.Execute(null);
            Assert.Equal(ThemePreference.Light, viewModel.Theme);

            viewModel.ToggleThemeCommand.Execute(null);
            Assert.Equal(ThemePreference.Dark, viewModel.Theme);

            viewModel.ToggleThemeCommand.Execute(null);
            Assert.Equal(ThemePreference.System, viewModel.Theme);
        }
    }

    [AvaloniaFact]
    public void ChangingTheThemeSwitchesTheApplicationVariant()
    {
        var (window, viewModel, fixture) = Create();
        using (fixture)
        {
            viewModel.Theme = ThemePreference.Dark;
            Assert.Equal(Avalonia.Styling.ThemeVariant.Dark,
                ThemeApplier.ToVariant(viewModel.Theme));

            viewModel.Theme = ThemePreference.Light;
            Assert.Equal(Avalonia.Styling.ThemeVariant.Light,
                ThemeApplier.ToVariant(viewModel.Theme));
            Assert.NotNull(window);
        }
    }

    [AvaloniaFact]
    public void ThemeTokensResolveInBothVariants()
    {
        var (window, _, fixture) = Create();
        using (fixture)
        {
            foreach (var variant in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = variant;
                foreach (var key in new[] { "TextColor", "CanvasColor", "PanelColor", "AccentColor", "FocusColor" })
                {
                    Assert.True(Application.Current!.TryGetResource(key, variant, out var value),
                        $"'{key}' is missing from the {variant} palette");
                    Assert.IsType<Color>(value);
                }
            }
        }
    }

    [AvaloniaFact]
    public void KeyBindingsCoverTheDocumentedShortcuts()
    {
        var (window, _, fixture) = Create();
        using (fixture)
        {
            var gestures = window.KeyBindings.Select(b => b.Gesture.ToString()).ToList();

            foreach (var expected in new[]
                     {
                         "Ctrl+O", "Ctrl+S", "Ctrl+Shift+S", "Ctrl+P",
                         "Ctrl+Z", "Ctrl+Y", "Ctrl+Shift+Z",
                         "Ctrl+C", "Ctrl+V", "Delete", "Escape"
                     })
                Assert.Contains(expected, gestures);
        }
    }

    [AvaloniaFact]
    public void ShortcutsAreSuppressedWhileATextInputHasFocus()
    {
        var (window, _, fixture) = Create();
        using (fixture)
        {
            var search = window.GetVisualDescendants().OfType<TextBox>().First();
            search.Focus();

            Assert.True(window.IsTextInputFocused(),
                "typing into a text field must not trigger editor shortcuts");
        }
    }

    [AvaloniaFact]
    public void StatusMessagesCanBeReportedAndCleared()
    {
        var (_, viewModel, fixture) = Create();
        using (fixture)
        {
            viewModel.ReportStatus("שגיאה לדוגמה", isError: true);
            Assert.True(viewModel.HasStatusMessage);
            Assert.True(viewModel.StatusIsError);

            viewModel.ClearStatus();
            Assert.False(viewModel.HasStatusMessage);
        }
    }

    [AvaloniaFact]
    public void OcrAvailabilityIsReportedHonestly()
    {
        var (_, viewModel, fixture) = Create();
        using (fixture)
        {
            // The Tesseract natives ship for Windows only, so this states the real situation.
            Assert.False(string.IsNullOrWhiteSpace(viewModel.OcrAvailabilityText));
            if (!OperatingSystem.IsWindows())
                Assert.False(fixture.Services.Ocr.IsAvailable);
        }
    }
}
