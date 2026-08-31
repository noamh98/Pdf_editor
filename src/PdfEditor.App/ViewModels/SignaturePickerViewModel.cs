using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using PdfEditor.App.Services;
using PdfEditor.Core.Localization;
using PdfEditor.Core.Signatures;

namespace PdfEditor.App.ViewModels;

/// <summary>One stored signature, with the preview the picker shows for it.</summary>
public sealed class SignatureItemViewModel(SignatureEntry entry, Bitmap? preview)
{
    public SignatureEntry Entry { get; } = entry;
    public Bitmap? Preview { get; } = preview;
    public string DisplayName => Entry.DisplayName;
    public string CreatedText => Entry.CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy");
}

/// <summary>
/// The signature library, as the user meets it: pick one to place, import a new one from an image,
/// or delete one for good.
/// </summary>
/// <remarks>
/// Filling a form usually ends in signing it, so this is reachable from placing the signature tool
/// rather than buried in settings. The library underneath already stores images under the current
/// user's local application data, protected where the platform allows it; nothing here changes
/// that, and nothing here sends an image anywhere.
/// </remarks>
public sealed class SignaturePickerViewModel : ViewModelBase
{
    private readonly ISignatureLibrary _library;
    private readonly ISignatureImageProcessor _processor;
    private readonly IDialogService _dialogs;
    private SignatureItemViewModel? _selected;
    private string? _error;
    private bool _isBusy;
    private bool _removeWhiteBackground = true;

    public SignaturePickerViewModel(
        ISignatureLibrary library,
        ISignatureImageProcessor processor,
        IDialogService dialogs)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy);
        DeleteCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => Selected is not null && !IsBusy);
    }

    public ObservableCollection<SignatureItemViewModel> Signatures { get; } = [];

    public AsyncRelayCommand ImportCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }

    public SignatureItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                RaisePropertyChanged(nameof(CanConfirm));
                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ImportCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Whether to drop a near-white background. A signature photographed or scanned from paper
    /// arrives on a white sheet, which would otherwise be stamped onto the page as a white box.
    /// </summary>
    public bool RemoveWhiteBackground
    {
        get => _removeWhiteBackground;
        set => SetProperty(ref _removeWhiteBackground, value);
    }

    public bool IsEmpty => Signatures.Count == 0;

    public bool CanConfirm => Selected is not null;

    public static string Disclaimer => Strings.SignatureDisclaimer;
    public static string StorageNotice => Strings.SignatureStorageNotice;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var entries = await _library.ListAsync(cancellationToken).ConfigureAwait(true);
            Signatures.Clear();
            foreach (var entry in entries)
            {
                var bytes = await _library.GetImageAsync(entry.Id, cancellationToken).ConfigureAwait(true);
                Signatures.Add(new SignatureItemViewModel(entry, ToBitmap(bytes)));
            }
            Selected = Signatures.FirstOrDefault();
            RaisePropertyChanged(nameof(IsEmpty));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = Strings.ErrorTargetReadOnly;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Returns the PNG bytes of the chosen signature, or null when nothing is chosen.</summary>
    public Task<byte[]?> ResolveSelectedImageAsync(CancellationToken cancellationToken = default) =>
        Selected is null
            ? Task.FromResult<byte[]?>(null)
            : _library.GetImageAsync(Selected.Entry.Id, cancellationToken);

    private async Task ImportAsync()
    {
        var path = await _dialogs.PickOpenFileAsync(
            Strings.ImportSignature,
            [new FileFilter("PNG, JPEG", ["png", "jpg", "jpeg"])]).ConfigureAwait(true);
        if (path is null) return;

        IsBusy = true;
        Error = null;
        try
        {
            var raw = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
            // Cropping and background removal happen before anything is stored, so the library only
            // ever holds the signature itself rather than the sheet it was photographed on.
            var normalized = _processor.Normalize(raw, RemoveWhiteBackground, out _, out _);
            var name = Path.GetFileNameWithoutExtension(path);
            await _library.AddAsync(string.IsNullOrWhiteSpace(name) ? Strings.ToolSignature : name,
                normalized).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            Error = Strings.ErrorUnsupportedImage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (Selected is not { } item) return;

        var answer = await _dialogs.ShowMessageAsync(new MessageRequest(
            Strings.Signatures, Strings.DeleteSignatureConfirm, MessageKind.Warning,
            PrimaryLabel: Strings.Delete, CancelLabel: Strings.Cancel)).ConfigureAwait(true);
        if (answer != MessageAnswer.Primary) return;

        IsBusy = true;
        try
        {
            await _library.DeleteAsync(item.Entry.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = Strings.ErrorTargetReadOnly;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Bitmap? ToBitmap(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            return new Bitmap(stream);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            // A stored signature that will not decode is shown as a name with no preview rather
            // than taking the whole picker down.
            return null;
        }
    }
}
