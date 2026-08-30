using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using PdfEditor.App;
using PdfEditor.App.Services;
using PdfEditor.App.ViewModels;
using PdfEditor.App.Views;
using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Core.Text;
using PdfEditor.Pdf.Fonts;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PdfEditor.Shots;

/// <summary>
/// Renders the shell headlessly at a set of widths and both theme variants, so the layout can be
/// looked at rather than assumed. The images under docs/images are produced by this.
/// </summary>
internal static class Program
{
    private sealed record Shot(string Name, int Width, int Height, ThemeVariant Theme, bool WithDocument);

    private static void Main(string[] args)
    {
        var outputDirectory = args.Length > 0 ? args[0] : "artifacts/shots";
        Directory.CreateDirectory(outputDirectory);

        AppBuilder.Configure<PdfEditorApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .SetupWithoutStarting();

        var root = Path.Combine(Path.GetTempPath(), "pdfeditor-shots-" + Guid.NewGuid().ToString("N"));
        var services = AppServices.CreateForRoot(root);
        var fixture = WriteFixture(root);

        Shot[] shots =
        [
            new("shell-wide-light",   1360, 860, ThemeVariant.Light, true),
            new("shell-wide-dark",    1360, 860, ThemeVariant.Dark,  true),
            new("shell-medium-light", 1040, 780, ThemeVariant.Light, true),
            new("shell-medium-dark",  1040, 780, ThemeVariant.Dark,  true),
            new("shell-compact-light", 820, 740, ThemeVariant.Light, true),
            new("shell-compact-dark",  820, 740, ThemeVariant.Dark,  true),
            new("shell-minimum-light", 680, 700, ThemeVariant.Light, true),
            new("start-light",        1360, 860, ThemeVariant.Light, false),
            new("start-dark",         1360, 860, ThemeVariant.Dark,  false)
        ];

        foreach (var shot in shots)
        {
            Capture(services, fixture, shot, outputDirectory);
            Console.WriteLine($"  {shot.Name}.png  {shot.Width}x{shot.Height}");
        }

        services.Dispose();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private static void Capture(AppServices services, string fixture, Shot shot, string outputDirectory)
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = shot.Theme;

        var viewModel = new MainWindowViewModel(services);
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = shot.Width,
            Height = shot.Height
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        if (shot.WithDocument)
        {
            Await(viewModel.OpenAsync(fixture));
            Drain();

            // A filled-in form field and a couple of marks, so the shot shows the editor doing its
            // actual job rather than an empty page.
            var document = viewModel.Document!;
            document.AddAnnotation(new TextBoxAnnotation
            {
                PageIndex = 0,
                Rect = new PdfRect(232, 604, 210, 22),
                Text = "ישראל ישראלי",
                FontSize = 12,
                TextColor = AnnotationColor.Black
            });
            document.AddAnnotation(new TextBoxAnnotation
            {
                PageIndex = 0,
                Rect = new PdfRect(232, 566, 210, 22),
                Text = "039876543",
                FontSize = 12,
                TextColor = AnnotationColor.Black
            });
            document.AddAnnotation(new MarkAnnotation(AnnotationKind.CheckMark)
            {
                PageIndex = 0,
                Rect = new PdfRect(300, 500, 24, 24),
                Color = AnnotationColor.Green
            });
            document.SelectedAnnotation = document.Annotations[0];
            Drain();
        }

        window.Width = shot.Width;
        window.Height = shot.Height;
        Drain();

        var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(outputDirectory, shot.Name + ".png"));
        window.Close();
        Drain();
    }

    /// <summary>
    /// Waits for work that resumes on the UI thread. Blocking on it here would deadlock: this is
    /// the UI thread, and the continuation cannot run until it is given back.
    /// </summary>
    private static void Await(Task task)
    {
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }

    /// <summary>Lets queued layout, rendering and background thumbnail work settle before capture.</summary>
    private static void Drain()
    {
        for (int i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static string WriteFixture(string directory)
    {
        PdfFonts.EnsureRegistered();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "form.pdf");

        using var document = new PdfDocument();
        string[] lines =
        [
            "1. שם מלא:",
            "2. מספר תעודת זהות:",
            "3. תאריך:",
            "4. כתובת למשלוח דואר:",
            "5. סכום ההתקשרות הכולל: 128,500 ש\"ח לפני מע\"מ.",
            "6. מסמכי הפרויקט יימסרו בקובץ project-plan.pdf."
        ];

        for (int p = 0; p < 6; p++)
        {
            var page = document.AddPage();
            page.Size = PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);
            // PdfSharp draws the characters in the order it is given them; it applies no
            // bidirectional reordering. Handing it a Hebrew string directly puts every word on the
            // page backwards. The application's own UAX#9 implementation produces the visual order
            // the page actually needs — which is exactly what it does for annotation text too.
            gfx.DrawString(BidiAlgorithm.ToVisual("טופס הצטרפות — טיוטה לבדיקה"),
                PdfFonts.Create(16, bold: true),
                XBrushes.Black, new XRect(0, 60, page.Width.Point, 24), XStringFormats.TopCenter);
            for (int i = 0; i < lines.Length; i++)
            {
                gfx.DrawString(BidiAlgorithm.ToVisual(lines[i]), PdfFonts.Create(11), XBrushes.Black,
                    new XRect(60, 150 + i * 38, page.Width.Point - 120, 20), XStringFormats.TopRight);
            }
        }
        document.Save(path);
        return path;
    }
}
