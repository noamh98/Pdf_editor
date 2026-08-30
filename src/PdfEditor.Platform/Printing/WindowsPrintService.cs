using System.Drawing;
using System.Runtime.InteropServices;
using System.Drawing.Printing;
using System.Runtime.Versioning;
using PdfEditor.Core.Printing;

namespace PdfEditor.Platform.Printing;

/// <summary>
/// Sends a prepared print job to a Windows printer.
/// </summary>
/// <remarks>
/// Rasterising the job is not this class's responsibility: the caller supplies a delegate that
/// renders one page of the prepared document to PNG. That keeps the printing code independent of
/// the PDF engine and testable without one.
///
/// On any platform other than Windows the service reports <see cref="IsSupported"/> as false and
/// returns a failed result rather than throwing, so the rest of the application can degrade
/// gracefully and the suite still runs on Linux.
/// </remarks>
public sealed class WindowsPrintService : IPrintService
{
    /// <summary>Renders page <c>index</c> of the prepared job at <c>dpi</c> and returns PNG bytes.</summary>
    public delegate Task<byte[]> RenderPageAsync(int pageIndex, int dpi, CancellationToken cancellationToken);

    private readonly RenderPageAsync _renderPage;

    public WindowsPrintService(RenderPageAsync renderPage)
    {
        _renderPage = renderPage ?? throw new ArgumentNullException(nameof(renderPage));
    }

    /// <summary>Resolution the job is rasterised at before being sent to the printer.</summary>
    public int RenderDpi { get; init; } = 200;

    public bool IsSupported => OperatingSystem.IsWindows();

    public Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported) return Task.FromResult<IReadOnlyList<PrinterInfo>>([]);
        // The guard is repeated inside the lambda so the platform analyser can see it too.
        return Task.Run<IReadOnlyList<PrinterInfo>>(
            () => OperatingSystem.IsWindows() ? EnumeratePrinters() : [], cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<PrinterInfo> EnumeratePrinters()
    {
        var defaultName = new PrinterSettings().PrinterName;
        var printers = new List<PrinterInfo>();

        foreach (string? name in PrinterSettings.InstalledPrinters)
        {
            if (string.IsNullOrEmpty(name)) continue;
            try
            {
                var settings = new PrinterSettings { PrinterName = name };
                if (!settings.IsValid) continue;
                printers.Add(new PrinterInfo(
                    name,
                    string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase),
                    settings.CanDuplex,
                    settings.SupportsColor));
            }
            catch (InvalidPrinterException) { }
        }
        return printers;
    }

    public async Task<PrintJobResult> PrintAsync(PrintJobRequest request, IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsSupported)
            return new PrintJobResult(false, 0, "הדפסה נתמכת במערכת Windows בלבד.");
        if (request.Sequence.Slots.Count == 0)
            return new PrintJobResult(false, 0, "אין עמודים להדפסה.");

        // Every page is rasterised up front so the synchronous PrintPage callback never blocks on
        // asynchronous work, which is a classic source of deadlocks in printing code.
        var rendered = new List<byte[]>(request.Sequence.Slots.Count);
        for (int i = 0; i < request.Sequence.Slots.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rendered.Add(await _renderPage(i, RenderDpi, cancellationToken).ConfigureAwait(false));
            progress?.Report((double)(i + 1) / request.Sequence.Slots.Count * 0.5);
        }

        return await Task.Run(() => OperatingSystem.IsWindows()
                ? Print(request, rendered, progress, cancellationToken)
                : new PrintJobResult(false, 0, "הדפסה נתמכת במערכת Windows בלבד."),
            cancellationToken).ConfigureAwait(false);
    }

    [SupportedOSPlatform("windows")]
    private static PrintJobResult Print(PrintJobRequest request, List<byte[]> rendered,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var images = new List<Image>(rendered.Count);
        try
        {
            foreach (var png in rendered)
            {
                using var stream = new MemoryStream(png, writable: false);
                images.Add(Image.FromStream(stream));
            }

            using var document = new PrintDocument();
            document.DocumentName = Path.GetFileName(request.DocumentPath);
            document.PrinterSettings.PrinterName = request.PrinterName;
            document.PrinterSettings.Copies = (short)Math.Clamp(request.Copies, 1, short.MaxValue);
            document.OriginAtMargins = false;

            if (!document.PrinterSettings.IsValid)
                return new PrintJobResult(false, 0, "המדפסת שנבחרה אינה זמינה.");

            int index = 0;
            Exception? failure = null;

            document.QueryPageSettings += (_, e) =>
            {
                if (index >= request.Sequence.Slots.Count) return;
                var slot = request.Sequence.Slots[index];
                bool swapped = slot.Rotation is 90 or 270;
                double width = swapped ? slot.HeightPoints : slot.WidthPoints;
                double height = swapped ? slot.WidthPoints : slot.HeightPoints;
                e.PageSettings.Landscape = width > height;
            };

            document.PrintPage += (_, e) =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (e.Graphics is not null && index < images.Count)
                        DrawFitted(e.Graphics, images[index], e.PageBounds, request.ScaleToFit);

                    index++;
                    progress?.Report(0.5 + (double)index / images.Count * 0.5);
                    e.HasMorePages = index < images.Count;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    e.Cancel = true;
                    e.HasMorePages = false;
                }
            };

            document.Print();

            if (failure is OperationCanceledException) throw failure;
            if (failure is not null) return new PrintJobResult(false, index, failure.Message);
            return new PrintJobResult(true, index, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidPrinterException e)
        {
            return new PrintJobResult(false, 0, "המדפסת שנבחרה אינה זמינה. " + e.Message);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ExternalException)
        {
            return new PrintJobResult(false, 0, "ההדפסה נכשלה. " + e.Message);
        }
        finally
        {
            foreach (var image in images) image.Dispose();
        }
    }

    /// <summary>Centres the page image inside the sheet, preserving its aspect ratio.</summary>
    [SupportedOSPlatform("windows")]
    private static void DrawFitted(Graphics graphics, Image image, Rectangle bounds, bool scaleToFit)
    {
        double scale = scaleToFit
            ? Math.Min((double)bounds.Width / image.Width, (double)bounds.Height / image.Height)
            : 1.0;

        int width = (int)Math.Round(image.Width * scale);
        int height = (int)Math.Round(image.Height * scale);
        int x = bounds.X + (bounds.Width - width) / 2;
        int y = bounds.Y + (bounds.Height - height) / 2;

        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(image, new Rectangle(x, y, width, height));
    }
}
