using Avalonia.Headless.XUnit;
using PdfEditor.App.Services;
using PdfEditor.App.ViewModels;
using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Core.Storage;
using PdfEditor.Pdf.Fonts;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PdfEditor.App.Tests;

/// <summary>A dialog service that answers with a fixed choice and counts what it was asked.</summary>
internal sealed class AnsweringDialogService(MessageAnswer answer, string? saveTarget = null) : IDialogService
{
    public int MessagesShown { get; private set; }
    public MessageRequest? LastRequest { get; private set; }

    public Task<string?> PickOpenFileAsync(string title, IReadOnlyList<FileFilter> filters) =>
        Task.FromResult<string?>(null);

    public Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<FileFilter> filters) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters) =>
        Task.FromResult(saveTarget);

    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

    public Task<MessageAnswer> ShowMessageAsync(MessageRequest request)
    {
        MessagesShown++;
        LastRequest = request;
        return Task.FromResult(answer);
    }
}

/// <summary>
/// Unsaved annotations must not be able to leave the application silently. These cover the two ways
/// that used to happen: closing the window, and a run that never came back.
/// </summary>
public class UnsavedWorkTests
{
    private static string WriteFixture(string directory)
    {
        PdfFonts.EnsureRegistered();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fixture.pdf");

        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Size = PageSize.A4;
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("עמוד", PdfFonts.Create(16), XBrushes.Black, new XPoint(60, 70));
        document.Save(path);
        return path;
    }

    private static TextBoxAnnotation Note(string text = "הערה") => new()
    {
        PageIndex = 0,
        Rect = new PdfRect(60, 600, 240, 60),
        Text = text,
        FontSize = 13
    };

    [AvaloniaFact]
    public async Task ClosingWithACleanDocumentAsksNothing()
    {
        using var fixture = new ServicesFixture();
        var dialogs = new AnsweringDialogService(MessageAnswer.Primary);
        var viewModel = new MainWindowViewModel(fixture.Services, dialogs);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));

        Assert.True(await viewModel.ConfirmCloseAsync());
        Assert.Equal(0, dialogs.MessagesShown);
    }

    [AvaloniaFact]
    public async Task ClosingWithUnsavedWorkAsksBeforeLettingGo()
    {
        using var fixture = new ServicesFixture();
        var dialogs = new AnsweringDialogService(MessageAnswer.Cancel);
        var viewModel = new MainWindowViewModel(fixture.Services, dialogs);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        viewModel.Document!.AddAnnotation(Note());

        Assert.False(await viewModel.ConfirmCloseAsync());
        Assert.Equal(1, dialogs.MessagesShown);
    }

    [AvaloniaFact]
    public async Task DecliningToSaveStillLetsTheWindowClose()
    {
        using var fixture = new ServicesFixture();
        var dialogs = new AnsweringDialogService(MessageAnswer.Secondary);
        var viewModel = new MainWindowViewModel(fixture.Services, dialogs);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        viewModel.Document!.AddAnnotation(Note());

        Assert.True(await viewModel.ConfirmCloseAsync());
    }

    [AvaloniaFact]
    public async Task UnsavedAnnotationsReachTheRecoverySidecar()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services, new AnsweringDialogService(MessageAnswer.Cancel));
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        viewModel.Document!.AddAnnotation(Note("שוחזר"));

        await viewModel.AutosaveNowAsync();

        var manifest = RecoveryManifest.Load(Path.Combine(fixture.Services.Paths.Recovery, "manifest.json"));
        var session = Assert.Single(manifest.Sessions);
        Assert.Equal(1, session.AnnotationCount);

        var restored = await fixture.Services.Autosave.RestoreAsync(session.SessionId);
        Assert.Equal("שוחזר", Assert.IsType<TextBoxAnnotation>(Assert.Single(restored)).Text);
    }

    [AvaloniaFact]
    public async Task ACleanDocumentWritesNoSidecar()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services, new AnsweringDialogService(MessageAnswer.Cancel));
        await viewModel.OpenAsync(WriteFixture(fixture.Root));

        await viewModel.AutosaveNowAsync();

        Assert.Empty(RecoveryManifest.Load(
            Path.Combine(fixture.Services.Paths.Recovery, "manifest.json")).Sessions);
    }

    [AvaloniaFact]
    public async Task AConfirmedCloseLeavesNothingToRecover()
    {
        using var fixture = new ServicesFixture();
        var dialogs = new AnsweringDialogService(MessageAnswer.Secondary);
        var viewModel = new MainWindowViewModel(fixture.Services, dialogs);
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        viewModel.Document!.AddAnnotation(Note());
        await viewModel.AutosaveNowAsync();

        Assert.True(await viewModel.ConfirmCloseAsync());

        Assert.Empty(await fixture.Services.Autosave.FindRecoverableSessionsAsync());
        Assert.Empty(RecoveryManifest.Load(
            Path.Combine(fixture.Services.Paths.Recovery, "manifest.json")).Sessions);
    }

    [AvaloniaFact]
    public async Task SavingClearsTheSidecarItNoLongerNeeds()
    {
        using var fixture = new ServicesFixture();
        var source = WriteFixture(fixture.Root);
        var viewModel = new MainWindowViewModel(fixture.Services, new AnsweringDialogService(MessageAnswer.Primary));
        await viewModel.OpenAsync(source);
        viewModel.Document!.AddAnnotation(Note());
        await viewModel.AutosaveNowAsync();

        await viewModel.SaveAsync();

        Assert.Empty(RecoveryManifest.Load(
            Path.Combine(fixture.Services.Paths.Recovery, "manifest.json")).Sessions);
    }

    [AvaloniaFact]
    public async Task StrandedWorkIsOfferedBackAndReapplied()
    {
        using var fixture = new ServicesFixture();
        var source = WriteFixture(fixture.Root);

        // A previous run that placed an annotation and never came back.
        var sessionId = fixture.Services.Autosave.BeginSession(source, SourceFingerprint.For(source));
        await fixture.Services.Autosave.SaveAsync(sessionId, [Note("מלפני הקריסה")]);

        var manifestPath = Path.Combine(fixture.Services.Paths.Recovery, "manifest.json");
        var manifest = RecoveryManifest.Load(manifestPath);
        manifest.Sessions[0] = manifest.Sessions[0] with { ProcessId = -1 };
        await manifest.SaveAsync(manifestPath);

        var dialogs = new AnsweringDialogService(MessageAnswer.Primary);
        var viewModel = new MainWindowViewModel(fixture.Services, dialogs);

        await viewModel.OfferRecoveryAsync();

        Assert.Equal(1, dialogs.MessagesShown);
        Assert.NotNull(viewModel.Document);
        var restored = Assert.Single(viewModel.Document!.Annotations);
        Assert.Equal("מלפני הקריסה", Assert.IsType<TextBoxAnnotation>(restored).Text);

        // The recovery is an offer: it arrives unsaved so the user decides whether to keep it.
        Assert.True(viewModel.Document.IsDirty);
        Assert.Empty(await fixture.Services.Autosave.FindRecoverableSessionsAsync());
    }

    [AvaloniaFact]
    public async Task DiscardingAnOfferThrowsTheStrandedWorkAway()
    {
        using var fixture = new ServicesFixture();
        var source = WriteFixture(fixture.Root);

        var sessionId = fixture.Services.Autosave.BeginSession(source, SourceFingerprint.For(source));
        await fixture.Services.Autosave.SaveAsync(sessionId, [Note()]);

        var manifestPath = Path.Combine(fixture.Services.Paths.Recovery, "manifest.json");
        var manifest = RecoveryManifest.Load(manifestPath);
        manifest.Sessions[0] = manifest.Sessions[0] with { ProcessId = -1 };
        await manifest.SaveAsync(manifestPath);

        var viewModel = new MainWindowViewModel(
            fixture.Services, new AnsweringDialogService(MessageAnswer.Secondary));

        await viewModel.OfferRecoveryAsync();

        Assert.Null(viewModel.Document);
        Assert.Empty(RecoveryManifest.Load(manifestPath).Sessions);
    }

    [AvaloniaFact]
    public async Task NothingStrandedMeansNoPrompt()
    {
        using var fixture = new ServicesFixture();
        var dialogs = new AnsweringDialogService(MessageAnswer.Primary);
        var viewModel = new MainWindowViewModel(fixture.Services, dialogs);

        await viewModel.OfferRecoveryAsync();

        Assert.Equal(0, dialogs.MessagesShown);
        Assert.Null(viewModel.Document);
    }
}
