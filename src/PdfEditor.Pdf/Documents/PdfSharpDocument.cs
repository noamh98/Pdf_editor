using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Core.Ocr;
using PdfEditor.Pdf.Annotations;
using PdfEditor.Pdf.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFtoImage;
using SkiaSharp;

namespace PdfEditor.Pdf.Documents;

/// <summary>
/// An open PDF document: PDFsharp supplies the object model, PDFium the raster output.
/// </summary>
/// <remarks>
/// The file's bytes are read once and held in memory, because the renderer needs them and because
/// it removes any dependency on the file staying unlocked and unchanged while the user edits.
/// Render calls are serialised: the PDFium binding is not re-entrant.
/// </remarks>
public sealed class PdfSharpDocument : IPdfDocument
{
    private readonly byte[] _bytes;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly List<PdfPageInfo> _pages;
    private PdfDocument? _structure;
    private bool _disposed;

    internal PdfSharpDocument(byte[] bytes, PdfDocument structure, string? sourcePath, string fingerprint)
    {
        _bytes = bytes;
        _structure = structure;
        SourcePath = sourcePath;
        Fingerprint = fingerprint;
        // An unencrypted document permits everything; any restriction means the file carries a
        // security handler and must be treated as read-only for structural edits.
        var security = structure.SecuritySettings;
        IsProtected = !security.PermitModifyDocument || !security.PermitAnnotations || !security.PermitPrint;

        _pages = new List<PdfPageInfo>(structure.PageCount);
        for (int i = 0; i < structure.PageCount; i++)
        {
            var page = structure.Pages[i];
            _pages.Add(new PdfPageInfo(i, page.Width.Point, page.Height.Point, NormalizeRotation(page.Rotate)));
        }
    }

    public string? SourcePath { get; }
    public string Fingerprint { get; }
    public bool IsProtected { get; }
    public int PageCount => _pages.Count;
    public IReadOnlyList<PdfPageInfo> Pages => _pages;

    /// <summary>The underlying object model. Only <c>PdfEditor.Pdf</c> may touch it.</summary>
    internal PdfDocument Structure => _structure ?? throw new ObjectDisposedException(nameof(PdfSharpDocument));

    /// <summary>A defensive copy of the bytes the document was loaded from.</summary>
    internal byte[] SourceBytes => _bytes;

    public IReadOnlyList<Annotation> LoadAnnotations()
    {
        var structure = Structure;
        var all = new List<Annotation>();
        for (int i = 0; i < structure.PageCount; i++)
            all.AddRange(AnnotationReader.Read(structure.Pages[i], i));
        return all;
    }

    public async Task<RenderedPage> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.PageIndex < 0 || request.PageIndex >= _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(request), request.PageIndex, "Page index is outside the document.");

        var page = _pages[request.PageIndex];
        var (displayWidth, displayHeight) = page.DisplaySize;
        double scale = ClampScale(request, displayWidth, displayHeight);

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = RenderBitmap(request.PageIndex, scale * 72.0, request.IncludeAnnotations);
                cancellationToken.ThrowIfCancellationRequested();
                return new RenderedPage(request.PageIndex, bitmap.Width, bitmap.Height, ToBgra(bitmap), scale);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    public async Task<byte[]> RenderToPngAsync(int pageIndex, int dpi, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pageIndex < 0 || pageIndex >= _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = RenderBitmap(pageIndex, Math.Clamp(dpi, 36, 900), withAnnotations: true);
                using var stream = new MemoryStream();
                bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);
                return stream.ToArray();
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    /// <summary>
    /// Reports whether a page carries visible content.
    /// </summary>
    /// <remarks>
    /// The page is rasterised small and every sampled pixel is compared against white. A page whose
    /// only content is white-on-white therefore counts as blank, and a faint watermark does not.
    /// This is a print-pipeline heuristic, not a semantic emptiness test.
    /// </remarks>
    public async Task<bool> IsPageBlankAsync(int pageIndex, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pageIndex < 0 || pageIndex >= _pages.Count) throw new ArgumentOutOfRangeException(nameof(pageIndex));

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = RenderBitmap(pageIndex, 36, withAnnotations: true);
                const int tolerance = 12;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        var p = bitmap.GetPixel(x, y);
                        if (p.Alpha < 250 - tolerance) continue;      // transparent counts as blank
                        if (p.Red < 255 - tolerance || p.Green < 255 - tolerance || p.Blue < 255 - tolerance)
                            return false;
                    }
                }
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    // PDFium ships native binaries for win-x64 and linux-x64, the only platforms this product
    // builds and tests on, so the platform analyser's warning is suppressed here rather than
    // propagated to every caller.
#pragma warning disable CA1416
    private SKBitmap RenderBitmap(int pageIndex, double dpi, bool withAnnotations) =>
        Conversion.ToImage(_bytes, page: pageIndex,
            options: new RenderOptions(Dpi: (int)Math.Round(dpi), WithAnnotations: withAnnotations, WithFormFill: true));
#pragma warning restore CA1416

    private static double ClampScale(RenderRequest request, double widthPoints, double heightPoints)
    {
        double scale = Math.Clamp(request.Scale, 0.05, 12.0);
        if (request.MaxPixelWidth is { } maxW && widthPoints * scale > maxW)
            scale = maxW / widthPoints;
        if (request.MaxPixelHeight is { } maxH && heightPoints * scale > maxH)
            scale = Math.Min(scale, maxH / heightPoints);
        return Math.Max(scale, 0.01);
    }

    private static byte[] ToBgra(SKBitmap bitmap)
    {
        using var target = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(bitmap, 0, 0);
        }
        var pixels = target.GetPixelSpan();
        return pixels.ToArray();
    }

    internal static int NormalizeRotation(int rotate)
    {
        int r = rotate % 360;
        if (r < 0) r += 360;
        return r - r % 90;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _structure?.Dispose();
        _structure = null;
        _renderGate.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Opens documents and turns every failure into a typed <see cref="PdfOpenError"/>.</summary>
public sealed class PdfDocumentLoader : IPdfDocumentLoader
{
    private static readonly byte[] Header = "%PDF-"u8.ToArray();

    public async Task<IPdfDocument> OpenAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException e)
        {
            throw new PdfOpenException(PdfOpenError.FileNotFound, "File not found.", e);
        }
        catch (DirectoryNotFoundException e)
        {
            throw new PdfOpenException(PdfOpenError.FileNotFound, "Directory not found.", e);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new PdfOpenException(PdfOpenError.AccessDenied, "Access denied.", e);
        }
        catch (IOException e)
        {
            throw new PdfOpenException(PdfOpenError.AccessDenied, "The file could not be read.", e);
        }

        return Open(bytes, path);
    }

    public async Task<IPdfDocument> OpenAsync(Stream stream, string? sourcePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Open(buffer.ToArray(), sourcePath);
    }

    private static IPdfDocument Open(byte[] bytes, string? sourcePath)
    {
        PdfFonts.EnsureRegistered();

        if (bytes.Length == 0)
            throw new PdfOpenException(PdfOpenError.NotAPdf, "The file is empty.");
        if (bytes.Length < Header.Length || !bytes.AsSpan(0, Header.Length).SequenceEqual(Header))
            throw new PdfOpenException(PdfOpenError.NotAPdf, "Missing %PDF- header.");

        PdfDocument structure;
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            structure = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        }
        catch (PdfReaderException e) when (e.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfOpenException(PdfOpenError.PasswordRequired, "The document is encrypted.", e);
        }
        catch (PdfReaderException e)
        {
            throw new PdfOpenException(PdfOpenError.Corrupted, "The document could not be parsed.", e);
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            // A PDF is untrusted input and the parser signals some malformations with a bare
            // Exception, so anything short of cancellation or memory exhaustion is a bad file.
            throw new PdfOpenException(PdfOpenError.Corrupted, "The document could not be parsed.", e);
        }

        if (structure.PageCount == 0)
        {
            structure.Dispose();
            throw new PdfOpenException(PdfOpenError.Corrupted, "The document contains no pages.");
        }

        using var fingerprintStream = new MemoryStream(bytes, writable: false);
        var fingerprint = OcrCacheKey.FingerprintStream(fingerprintStream);
        return new PdfSharpDocument(bytes, structure, sourcePath, fingerprint);
    }
}
