using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;

namespace PdfEditor.App.ViewModels;

/// <summary>
/// One page in the document view.
/// </summary>
/// <remarks>
/// The bitmap is produced on demand and released when the page scrolls far out of view, which is
/// what keeps a 500-page document inside a sensible memory budget. Rendering runs on the thread
/// pool; only the assignment of the finished bitmap returns to the UI thread.
/// </remarks>
public sealed class PageViewModel : ViewModelBase, IDisposable
{
    private readonly IPdfDocument _document;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private CancellationTokenSource? _pending;
    private Bitmap? _bitmap;
    private Bitmap? _thumbnail;
    private double _renderedScale;
    private Task? _thumbnailTask;
    private double _scale = 1.0;
    private bool _isSelected;
    private bool _disposed;

    public PageViewModel(IPdfDocument document, PdfPageInfo info)
    {
        _document = document;
        Info = info;
    }

    public PdfPageInfo Info { get; }

    public int PageIndex => Info.Index;

    public int PageNumber => Info.Index + 1;

    /// <summary>Annotations placed on this page, in the order they were added.</summary>
    public List<Annotation> Annotations { get; } = [];

    public double Scale
    {
        get => _scale;
        set
        {
            if (!SetProperty(ref _scale, Math.Clamp(value, 0.05, 12.0))) return;
            RaiseAll(nameof(DisplayWidth), nameof(DisplayHeight));
        }
    }

    public double DisplayWidth => Info.DisplaySize.Width * _scale;

    public double DisplayHeight => Info.DisplaySize.Height * _scale;

    public Bitmap? Bitmap
    {
        get => _bitmap;
        private set => SetProperty(ref _bitmap, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// A small, fixed-size render for the page list. Kept separate from the page bitmap so the
    /// thumbnail survives while the full-resolution image is released, and so a 500-page document
    /// never holds 500 full-size bitmaps.
    /// </summary>
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    /// <summary>Width of the thumbnail render, in pixels.</summary>
    public const int ThumbnailWidth = 130;

    /// <summary>
    /// Renders the thumbnail once. Concurrent callers await the same work rather than starting a
    /// second render or returning before the first one has produced anything.
    /// </summary>
    public Task EnsureThumbnailAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _thumbnail is not null) return Task.CompletedTask;
        return _thumbnailTask ??= RenderThumbnailAsync(cancellationToken);
    }

    private async Task RenderThumbnailAsync(CancellationToken cancellationToken)
    {
        try
        {
            double scale = ThumbnailWidth / Math.Max(1, Info.DisplaySize.Width);
            var rendered = await _document
                .RenderAsync(new RenderRequest(PageIndex, scale, IncludeAnnotations: false), cancellationToken)
                .ConfigureAwait(false);
            var bitmap = ToBitmap(rendered);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) { bitmap.Dispose(); return; }
                Thumbnail = bitmap;
            });
        }
        catch (OperationCanceledException)
        {
            _thumbnailTask = null;
        }
    }

    /// <summary>Discards the cached bitmaps so the next request re-renders, after an edit.</summary>
    public void Invalidate()
    {
        _renderedScale = 0;
        _thumbnailTask = null;
        var old = Thumbnail;
        Thumbnail = null;
        old?.Dispose();
        RaisePropertyChanged(nameof(IsSharp));
    }

    /// <summary>True when the current bitmap was produced at the current scale.</summary>
    public bool IsSharp => _bitmap is not null && Math.Abs(_renderedScale - _scale) < 0.01;

    /// <summary>
    /// Renders the page if it is not already available at this scale. Repeated calls while a render
    /// is in flight cancel the earlier one, so fast scrolling does not queue stale work.
    /// </summary>
    public async Task EnsureRenderedAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || IsSharp) return;

        var previous = Interlocked.Exchange(ref _pending, null);
        previous?.Cancel();
        previous?.Dispose();

        // Not a `using`: a call that supersedes this one disposes it via the exchange above, and
        // this call must not also dispose it while that supersedes it in flight, nor dispose a
        // "current" token some later call has already taken over. The exchange in the finally
        // block below only disposes it if it is still the one this call published.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pending = cts;

        await _renderGate.WaitAsync(cts.Token).ConfigureAwait(false);
        try
        {
            if (_disposed || IsSharp) return;
            double scale = _scale;
            var rendered = await _document
                .RenderAsync(new RenderRequest(PageIndex, scale, IncludeAnnotations: false), cts.Token)
                .ConfigureAwait(false);

            var bitmap = ToBitmap(rendered);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) { bitmap.Dispose(); return; }
                var old = Bitmap;
                Bitmap = bitmap;
                _renderedScale = scale;
                old?.Dispose();
                RaisePropertyChanged(nameof(IsSharp));
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request or the page scrolled away.
        }
        finally
        {
            _renderGate.Release();
            if (Interlocked.CompareExchange(ref _pending, null, cts) == cts) cts.Dispose();
        }
    }

    /// <summary>Frees the bitmap for a page that is far outside the viewport.</summary>
    public void ReleaseBitmap()
    {
        var old = Bitmap;
        if (old is null) return;
        Bitmap = null;
        _renderedScale = 0;
        old.Dispose();
        RaisePropertyChanged(nameof(IsSharp));
    }

    private static Bitmap ToBitmap(RenderedPage page)
    {
        var bitmap = new WriteableBitmap(
            new Avalonia.PixelSize(page.PixelWidth, page.PixelHeight),
            new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var buffer = bitmap.Lock();
        System.Runtime.InteropServices.Marshal.Copy(
            page.BgraPixels, 0, buffer.Address, page.BgraPixels.Length);
        return bitmap;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pending?.Cancel();
        _pending?.Dispose();
        _bitmap?.Dispose();
        _bitmap = null;
        _thumbnail?.Dispose();
        _thumbnail = null;
        _renderGate.Dispose();
    }
}
