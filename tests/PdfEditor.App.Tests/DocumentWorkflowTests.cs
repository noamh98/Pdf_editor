using Avalonia.Headless.XUnit;
using PdfEditor.App.Services;
using PdfEditor.App.ViewModels;
using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Pdf.Fonts;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PdfEditor.App.Tests;

/// <summary>
/// Drives the shell the way a user does: open a document, place annotations, undo, save, reopen.
/// The PDF engine is the real one, so these exercise the whole stack below the window.
/// </summary>
public class DocumentWorkflowTests
{
    private static string WriteFixture(string directory, int pageCount = 3)
    {
        PdfFonts.EnsureRegistered();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fixture.pdf");

        using var document = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            var page = document.AddPage();
            page.Size = PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"עמוד {i + 1}", PdfFonts.Create(16), XBrushes.Black, new XPoint(60, 70));
        }
        document.Save(path);
        return path;
    }

    private static TextBoxAnnotation Note(int page = 0) => new()
    {
        PageIndex = page,
        Rect = new PdfRect(60, 600, 240, 60),
        Text = "הערה בעברית 42",
        FontSize = 13
    };

    [AvaloniaFact]
    public async Task OpeningADocumentPopulatesThePagesAndClearsTheEmptyState()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        var path = WriteFixture(fixture.Root, 4);

        await viewModel.OpenAsync(path);

        Assert.True(viewModel.HasDocument);
        Assert.False(viewModel.IsEmpty);
        Assert.Equal(4, viewModel.Document!.PageCount);
        Assert.Equal(4, viewModel.Document.Pages.Count);
        Assert.Equal("fixture.pdf", viewModel.Document.DisplayName);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task OpeningAFileThatIsNotAPdfReportsAHebrewMessageInsteadOfThrowing()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        var path = Path.Combine(fixture.Root, "notes.pdf");
        Directory.CreateDirectory(fixture.Root);
        await File.WriteAllTextAsync(path, "plain text");

        await viewModel.OpenAsync(path);

        Assert.False(viewModel.HasDocument);
        Assert.True(viewModel.StatusIsError);
        Assert.Contains("PDF", viewModel.StatusMessage!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task APageRendersToABitmap()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root, 1));

        var page = viewModel.Document!.Pages[0];
        await page.EnsureRenderedAsync();

        Assert.NotNull(page.Bitmap);
        Assert.True(page.Bitmap!.PixelSize.Width > 100);
        Assert.True(page.IsSharp);
    }

    [AvaloniaFact]
    public async Task APageCanBeRenderedAgainAtANewZoomAfterFinishingTheLastOne()
    {
        // EnsureRenderedAsync published its cancellation source in a field, then disposed it
        // unconditionally on the way out via a `using`, without clearing the field. A call that
        // completes successfully leaves that disposed source sitting there; the next call - here, a
        // zoom change, which is the only case that does not short-circuit on IsSharp - exchanges it
        // out and calls Cancel() on it, throwing ObjectDisposedException. Every zoom step after the
        // page had already rendered once hit exactly this.
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root, 1));

        var page = viewModel.Document!.Pages[0];
        await page.EnsureRenderedAsync();
        Assert.True(page.IsSharp);

        page.Scale *= 1.5;
        await page.EnsureRenderedAsync();

        Assert.NotNull(page.Bitmap);
        Assert.True(page.IsSharp);
    }

    [AvaloniaFact]
    public async Task ThumbnailsAreRenderedAtASmallFixedWidth()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root, 2));

        var page = viewModel.Document!.Pages[0];
        await page.EnsureThumbnailAsync();

        Assert.NotNull(page.Thumbnail);
        Assert.InRange(page.Thumbnail!.PixelSize.Width, 100, 160);
    }

    [AvaloniaFact]
    public async Task AddingAnAnnotationMarksTheDocumentDirtyAndEnablesUndo()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));

        Assert.False(viewModel.Document!.IsDirty);

        viewModel.Document.AddAnnotation(Note());

        Assert.True(viewModel.Document.IsDirty);
        Assert.True(viewModel.Document.CanUndo);
        Assert.Single(viewModel.Document.Annotations);
        Assert.Single(viewModel.Document.Pages[0].Annotations);
        Assert.Contains("•", viewModel.WindowTitle, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task UndoAndRedoMoveTheAnnotationInAndOutOfTheDocument()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        viewModel.Document!.AddAnnotation(Note());

        viewModel.Undo();
        Assert.Empty(viewModel.Document.Annotations);
        Assert.False(viewModel.Document.IsDirty);

        viewModel.Redo();
        Assert.Single(viewModel.Document.Annotations);
        Assert.True(viewModel.Document.IsDirty);
    }

    [AvaloniaFact]
    public async Task DeletingTheSelectionRemovesItAndCanBeUndone()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        var note = Note();
        viewModel.Document!.AddAnnotation(note);

        viewModel.Document.SelectedAnnotation = note;
        viewModel.DeleteSelection();
        Assert.Empty(viewModel.Document.Annotations);

        viewModel.Undo();
        Assert.Single(viewModel.Document.Annotations);
    }

    [AvaloniaFact]
    public async Task CopyAndPastePlaceASecondAnnotationOffsetFromTheFirst()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        var note = Note();
        viewModel.Document!.AddAnnotation(note);
        viewModel.Document.SelectedAnnotation = note;

        viewModel.CopySelection();
        viewModel.Paste();

        Assert.Equal(2, viewModel.Document.Annotations.Count);
        Assert.NotEqual(viewModel.Document.Annotations[0].Id, viewModel.Document.Annotations[1].Id);
        Assert.NotEqual(viewModel.Document.Annotations[0].Rect, viewModel.Document.Annotations[1].Rect);
    }

    [AvaloniaFact]
    public async Task EditingAPropertyIsRecordedAsOneUndoStep()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        var note = Note();
        viewModel.Document!.AddAnnotation(note);
        viewModel.Document.SelectedAnnotation = note;

        int before = viewModel.Document.History.UndoCount;
        viewModel.Properties!.ApplyColor(AnnotationColor.Green);

        Assert.Equal(AnnotationColor.Green, note.Color);
        Assert.Equal(before + 1, viewModel.Document.History.UndoCount);

        viewModel.Undo();
        Assert.NotEqual(AnnotationColor.Green, note.Color);
    }

    [AvaloniaFact]
    public async Task ForeignAnnotationsCannotBeEditedOrDeleted()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));

        var foreign = new ShapeAnnotation(AnnotationKind.Rectangle)
        {
            IsForeign = true,
            PageIndex = 0,
            Rect = new PdfRect(10, 10, 50, 50)
        };
        viewModel.Document!.Annotations.Add(foreign);
        viewModel.Document.SelectedAnnotation = foreign;

        Assert.True(viewModel.Document.HasSelection);
        Assert.False(viewModel.Document.IsSelectionEditable);
        Assert.False(viewModel.DeleteSelectionCommand.CanExecute(null));
        Assert.True(viewModel.Properties!.IsForeign);
    }

    [AvaloniaFact]
    public async Task SavingWritesTheAnnotationsAndClearsTheDirtyFlag()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        var source = WriteFixture(fixture.Root);
        await viewModel.OpenAsync(source);
        viewModel.Document!.AddAnnotation(Note());

        var target = Path.Combine(fixture.Root, "saved.pdf");
        await SaveTo(viewModel, target, SaveMode.Editable);

        Assert.False(viewModel.Document.IsDirty);
        Assert.True(File.Exists(target));

        await using var reopened = await fixture.Services.Loader.OpenAsync(target, CancellationToken.None);
        var restored = Assert.Single(reopened.LoadAnnotations());
        Assert.Equal("הערה בעברית 42", Assert.IsType<TextBoxAnnotation>(restored).Text);
    }

    [AvaloniaFact]
    public async Task SavingDoesNotAlterTheSourceFile()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        var source = WriteFixture(fixture.Root);
        var before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            await File.ReadAllBytesAsync(source)));

        await viewModel.OpenAsync(source);
        viewModel.Document!.AddAnnotation(Note());
        await SaveTo(viewModel, Path.Combine(fixture.Root, "copy.pdf"), SaveMode.Flattened);

        var after = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            await File.ReadAllBytesAsync(source)));
        Assert.Equal(before, after);
    }

    [AvaloniaFact]
    public async Task ZoomModesChangeTheRenderedPageSize()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        var document = viewModel.Document!;

        document.SetViewport(1000, 700);

        document.ApplyZoomMode(ZoomMode.Actual);
        Assert.Equal(1.0, document.Zoom, 3);

        document.ApplyZoomMode(ZoomMode.FitWidth);
        double fitWidth = document.Zoom;
        Assert.True(fitWidth > 1.0, $"fit width was {fitWidth}");

        document.ApplyZoomMode(ZoomMode.FitPage);
        Assert.True(document.Zoom < fitWidth);

        document.ZoomIn();
        Assert.Equal(ZoomMode.Custom, document.ZoomMode);
    }

    [AvaloniaFact]
    public async Task PageNavigationStaysInsideTheDocument()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root, 3));

        viewModel.GoToPage(2);
        Assert.Equal(2, viewModel.Document!.CurrentPageIndex);
        Assert.False(viewModel.NextPageCommand.CanExecute(null));

        viewModel.GoToPage(99);
        Assert.Equal(2, viewModel.Document.CurrentPageIndex);

        viewModel.GoToPage(-5);
        Assert.Equal(0, viewModel.Document.CurrentPageIndex);
        Assert.False(viewModel.PreviousPageCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task ThePrintPreviewInterleavesBlankPagesAndReportsTheSheetCount()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root, 3));

        await viewModel.ShowPrintPreviewAsync();
        var preview = viewModel.PrintPreview!;
        Assert.True(viewModel.IsPrintPreviewOpen);

        preview.SeparateSheetsPerContentPage = true;
        Assert.Equal(5, preview.Slots.Count);
        Assert.Equal(3, preview.Sequence.ContentPageCount);
        Assert.Equal(2, preview.Sequence.InsertedBlankCount);
        Assert.Equal(3, preview.Sequence.EstimatedSheets(duplexForced: true));
        Assert.Contains("3", preview.SummaryText, StringComparison.Ordinal);

        preview.SeparateSheetsPerContentPage = false;
        Assert.Equal(3, preview.Slots.Count);

        viewModel.ClosePrintPreview();
        Assert.False(viewModel.IsPrintPreviewOpen);
    }

    [AvaloniaFact]
    public async Task ThePrintPreviewRejectsAMalformedRangeWithAHebrewMessage()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root, 3));
        await viewModel.ShowPrintPreviewAsync();

        var preview = viewModel.PrintPreview!;
        preview.RangeText = "1-";

        Assert.True(preview.HasRangeError);
        Assert.False(string.IsNullOrWhiteSpace(preview.RangeError));

        preview.RangeText = "1-2";
        Assert.False(preview.HasRangeError);
        Assert.Equal(2, preview.Sequence.ContentPageCount);
    }

    [AvaloniaFact]
    public async Task ClosingADocumentReleasesItAndReturnsToTheEmptyState()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));

        await viewModel.CloseDocumentAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.Null(viewModel.Document);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    private static async Task SaveTo(MainWindowViewModel viewModel, string target, SaveMode mode)
    {
        // The picker is bypassed so the workflow can be driven without a window.
        await viewModel.Services.Writer.SaveAsync(viewModel.Document!.Document,
            new SaveRequest(target, mode, [.. viewModel.Document.Annotations]), null, CancellationToken.None);
        if (mode == SaveMode.Editable) viewModel.Document.MarkSaved(target);
    }
}
