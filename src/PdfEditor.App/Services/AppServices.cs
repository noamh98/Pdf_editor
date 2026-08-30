using PdfEditor.Core.Documents;
using PdfEditor.Core.Ocr;
using PdfEditor.Core.Printing;
using PdfEditor.Core.Settings;
using PdfEditor.Core.Signatures;
using PdfEditor.Core.Storage;
using PdfEditor.Ocr;
using PdfEditor.Pdf.Documents;
using PdfEditor.Pdf.Fonts;
using PdfEditor.Platform.Files;
using PdfEditor.Platform.Printing;
using PdfEditor.Platform.Signatures;

namespace PdfEditor.App.Services;

/// <summary>
/// The services the application runs on, wired once at startup.
/// </summary>
/// <remarks>
/// A small hand-written composition root rather than a container: the graph is shallow, and keeping
/// it explicit makes it obvious that nothing here opens a network connection.
/// </remarks>
public sealed class AppServices : IDisposable
{
    private AppServices(AppPaths paths, AppSettings settings)
    {
        Paths = paths;
        Settings = settings;

        PdfFonts.EnsureRegistered();

        Loader = new PdfDocumentLoader();
        Writer = new PdfDocumentWriter();
        Assembler = new DocumentAssembler();
        PrintJobBuilder = new PrintJobBuilder();

        OcrCache = new FileSystemOcrCache(paths);
        OcrEngine = CreateOcrEngine();
        Ocr = new OcrService(OcrEngine, OcrCache);

        Signatures = new SignatureLibrary(paths);
        SignatureProcessor = new SignatureImageProcessor();
        Janitor = new TempFileJanitor(paths);
    }

    /// <summary>Builds the graph rooted at an arbitrary directory. Used by tests.</summary>
    public static AppServices CreateForRoot(string root)
    {
        var paths = AppPaths.ForRoot(root);
        paths.EnsureCreated();
        return new AppServices(paths, AppSettings.Load(paths.Settings));
    }

    public static AppServices Create()
    {
        var paths = AppPaths.ForCurrentUser();
        paths.EnsureCreated();
        var settings = AppSettings.Load(paths.Settings);
        var services = new AppServices(paths, settings);

        // Anything a previous run left behind is removed before the user does anything else.
        services.Janitor.CleanupOrphans(TimeSpan.FromHours(2));
        return services;
    }

    public AppPaths Paths { get; }
    public AppSettings Settings { get; }
    public IPdfDocumentLoader Loader { get; }
    public IPdfDocumentWriter Writer { get; }
    public IDocumentAssembler Assembler { get; }
    public IPrintJobBuilder PrintJobBuilder { get; }
    public IOcrEngine OcrEngine { get; }
    public IOcrCache OcrCache { get; }
    public OcrService Ocr { get; }
    public ISignatureLibrary Signatures { get; }
    public ISignatureImageProcessor SignatureProcessor { get; }
    public TempFileJanitor Janitor { get; }

    /// <summary>
    /// Builds a print service bound to a specific prepared job document.
    /// </summary>
    public IPrintService CreatePrintService(IPdfDocument jobDocument) =>
        new WindowsPrintService((pageIndex, dpi, token) => jobDocument.RenderToPngAsync(pageIndex, dpi, token));

    private static IOcrEngine CreateOcrEngine()
    {
        var engine = new TesseractOcrEngine();
        // Falling back keeps the interface bindable and lets the UI explain the situation.
        return engine.IsAvailable ? engine : new NullOcrEngine { UnavailableReason = engine.UnavailableReason! };
    }

    public Task SaveSettingsAsync(CancellationToken cancellationToken = default) =>
        Settings.SaveAsync(Paths.Settings, cancellationToken);

    public void Dispose()
    {
        Janitor.Dispose();
        OcrEngine.Dispose();
    }
}
