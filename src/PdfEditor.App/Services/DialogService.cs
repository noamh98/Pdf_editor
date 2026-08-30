using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PdfEditor.Core.Localization;

namespace PdfEditor.App.Services;

/// <summary>
/// The window-backed implementation of <see cref="IDialogService"/>.
/// </summary>
/// <remarks>
/// Message dialogs are composed here rather than taken from a dialog library, so their layout,
/// button order and focus behaviour follow the application's own right-to-left conventions.
/// </remarks>
public sealed class DialogService(Window owner) : IDialogService
{
    private readonly Window _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public async Task<string?> PickOpenFileAsync(string title, IReadOnlyList<FileFilter> filters)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ToFileTypes(filters)
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<FileFilter> filters)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = ToFileTypes(filters)
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName,
        IReadOnlyList<FileFilter> filters)
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "pdf",
            ShowOverwritePrompt = true,
            FileTypeChoices = ToFileTypes(filters)
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static List<FilePickerFileType> ToFileTypes(IReadOnlyList<FileFilter> filters) =>
        filters.Select(f => new FilePickerFileType(f.Name)
        {
            Patterns = f.Extensions.Select(e => "*." + e).ToList()
        }).ToList();

    public async Task<MessageAnswer> ShowMessageAsync(MessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = MessageAnswer.Cancel;

        var dialog = new Window
        {
            Title = request.Title,
            FlowDirection = FlowDirection.RightToLeft,
            SizeToContent = SizeToContent.Height,
            Width = 460,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(0, 20, 0, 0)
        };

        void AddButton(string label, MessageAnswer result, bool primary)
        {
            var button = new Button { Content = label, MinWidth = 96, MinHeight = 32 };
            button.Classes.Add(primary ? "primary" : "secondary");
            button.Click += (_, _) => { answer = result; dialog.Close(); };
            if (primary) button.IsDefault = true;
            if (result == MessageAnswer.Cancel) button.IsCancel = true;
            buttons.Children.Add(button);
        }

        if (request.PrimaryLabel is { } primaryLabel) AddButton(primaryLabel, MessageAnswer.Primary, true);
        if (request.SecondaryLabel is { } secondaryLabel) AddButton(secondaryLabel, MessageAnswer.Secondary, false);
        AddButton(request.CancelLabel ?? Strings.Cancel, MessageAnswer.Cancel, false);

        var content = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(20) };
        content.Children.Add(new TextBlock
        {
            Text = request.Title,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock { Text = request.Body, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(buttons);
        dialog.Content = content;

        await dialog.ShowDialog(_owner);
        return answer;
    }
}
