using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using PdfEditor.App.Services;
using PdfEditor.App.ViewModels;
using PdfEditor.Pdf.Documents;
using PdfEditor.Pdf.Fonts;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PdfEditor.App.Tests;

/// <summary>A dialog service that answers with paths the test chose in advance.</summary>
internal sealed class ScriptedDialogService(string? saveTarget = null) : IDialogService
{
    public Task<string?> PickOpenFileAsync(string title, IReadOnlyList<FileFilter> filters) =>
        Task.FromResult<string?>(null);

    public Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<FileFilter> filters) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters) =>
        Task.FromResult(saveTarget);

    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

    public Task<MessageAnswer> ShowMessageAsync(MessageRequest request) =>
        Task.FromResult(MessageAnswer.Primary);
}

/// <summary>
/// The page operations run end to end against the real PDF engine. Every case asserts that the
/// source file is byte for byte what it was: these operations are only safe because they write a
/// new file, and that is worth proving rather than trusting.
/// </summary>
public class PageOperationWorkflowTests
{
    private static string WriteFixture(string directory, int pageCount)
    {
        PdfFonts.EnsureRegistered();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "pages.pdf");

        using var document = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            var page = document.AddPage();
            page.Size = PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"{i + 1}", PdfFonts.Create(24), XBrushes.Black, new XPoint(80, 90));
        }
        document.Save(path);
        return path;
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<(MainWindowViewModel ViewModel, string Source, string Target)> OpenAsync(
        ServicesFixture fixture, int pageCount)
    {
        var source = WriteFixture(fixture.Root, pageCount);
        var target = Path.Combine(fixture.Root, "result.pdf");
        var viewModel = new MainWindowViewModel(fixture.Services, new ScriptedDialogService(target));
        await viewModel.OpenAsync(source);
        return (viewModel, source, target);
    }

    [AvaloniaFact]
    public async Task ExtractingWritesANewFileAndLeavesTheSourceUntouched()
    {
        using var fixture = new ServicesFixture();
        var (viewModel, source, target) = await OpenAsync(fixture, 6);
        var before = Hash(source);

        viewModel.ShowPageOperations();
        viewModel.PageOperations!.RangeText = "2-3";
        viewModel.PageOperations.Operation = PageOperation.Extract;
        await viewModel.ApplyPageOperationAsync();

        Assert.True(File.Exists(target));
        Assert.True(new FileInfo(target).Length > 0);
        Assert.Equal(before, Hash(source));

        await using var written = await new PdfDocumentLoader().OpenAsync(target, CancellationToken.None);
        Assert.Equal(2, written.PageCount);

        // The dialog closes on success and the status bar says where the file went.
        Assert.False(viewModel.IsPageOperationsOpen);
        Assert.True(viewModel.HasStatusMessage);
        Assert.False(viewModel.StatusIsError);
    }

    [AvaloniaFact]
    public async Task DeletingPagesProducesTheRemainingOnes()
    {
        using var fixture = new ServicesFixture();
        var (viewModel, source, target) = await OpenAsync(fixture, 5);
        var before = Hash(source);

        viewModel.ShowPageOperations();
        viewModel.PageOperations!.RangeText = "1,5";
        viewModel.PageOperations.Operation = PageOperation.Delete;
        await viewModel.ApplyPageOperationAsync();

        await using var written = await new PdfDocumentLoader().OpenAsync(target, CancellationToken.None);
        Assert.Equal(3, written.PageCount);
        Assert.Equal(before, Hash(source));
    }

    [AvaloniaFact]
    public async Task RotatingKeepsEveryPageAndTurnsOnlyTheSelectedOnes()
    {
        using var fixture = new ServicesFixture();
        var (viewModel, source, target) = await OpenAsync(fixture, 4);
        var before = Hash(source);

        viewModel.ShowPageOperations();
        viewModel.PageOperations!.RangeText = "2";
        viewModel.PageOperations.Operation = PageOperation.RotateRight;
        await viewModel.ApplyPageOperationAsync();

        await using var written = await new PdfDocumentLoader().OpenAsync(target, CancellationToken.None);
        Assert.Equal(4, written.PageCount);
        Assert.Equal(90, written.Pages[1].Rotation);
        Assert.Equal(0, written.Pages[0].Rotation);
        Assert.Equal(before, Hash(source));
    }

    [AvaloniaFact]
    public async Task NothingIsWrittenWhenTheSaveDialogIsCancelled()
    {
        using var fixture = new ServicesFixture();
        var source = WriteFixture(fixture.Root, 3);
        var viewModel = new MainWindowViewModel(fixture.Services, new ScriptedDialogService(saveTarget: null));
        await viewModel.OpenAsync(source);
        var before = Hash(source);

        viewModel.ShowPageOperations();
        viewModel.PageOperations!.Operation = PageOperation.Delete;
        await viewModel.ApplyPageOperationAsync();

        Assert.Equal(before, Hash(source));
        Assert.True(viewModel.IsPageOperationsOpen);
        Assert.Empty(Directory.GetFiles(fixture.Root, "result*.pdf"));
    }

    [AvaloniaFact]
    public async Task AnInvalidRangeNeverReachesTheWriter()
    {
        using var fixture = new ServicesFixture();
        var (viewModel, source, target) = await OpenAsync(fixture, 4);
        var before = Hash(source);

        viewModel.ShowPageOperations();
        viewModel.PageOperations!.RangeText = "9-12";

        Assert.False(viewModel.ApplyPageOperationCommand.CanExecute(null));
        await viewModel.ApplyPageOperationAsync();

        Assert.False(File.Exists(target));
        Assert.Equal(before, Hash(source));
    }
}
