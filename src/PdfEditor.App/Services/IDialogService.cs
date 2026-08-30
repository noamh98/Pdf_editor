namespace PdfEditor.App.Services;

public sealed record FileFilter(string Name, IReadOnlyList<string> Extensions);

public enum MessageKind { Information, Warning, Error, Question }

public sealed record MessageRequest(
    string Title,
    string Body,
    MessageKind Kind = MessageKind.Information,
    string? PrimaryLabel = null,
    string? SecondaryLabel = null,
    string? CancelLabel = null);

public enum MessageAnswer { Primary, Secondary, Cancel }

/// <summary>
/// Everything the view models need from the shell, behind an interface so they can be driven by a
/// test without a window.
/// </summary>
public interface IDialogService
{
    Task<string?> PickOpenFileAsync(string title, IReadOnlyList<FileFilter> filters);

    Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<FileFilter> filters);

    Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters);

    Task<string?> PickFolderAsync(string title);

    Task<MessageAnswer> ShowMessageAsync(MessageRequest request);
}

/// <summary>Used by headless tests and by design-time data; answers nothing and picks nothing.</summary>
public sealed class NullDialogService : IDialogService
{
    public Task<string?> PickOpenFileAsync(string title, IReadOnlyList<FileFilter> filters) =>
        Task.FromResult<string?>(null);

    public Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<FileFilter> filters) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters) =>
        Task.FromResult<string?>(null);

    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

    public Task<MessageAnswer> ShowMessageAsync(MessageRequest request) => Task.FromResult(MessageAnswer.Cancel);
}
