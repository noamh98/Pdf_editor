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
/// Filling a form usually ends in signing it. These cover the flow from placing the signature tool
/// to the image landing on the page — and, just as importantly, what happens when the user changes
/// their mind.
/// </summary>
public class SignatureFlowTests
{
    /// <summary>A 4×2 opaque PNG, so the aspect ratio it implies is unambiguous.</summary>
    private static byte[] SamplePng()
    {
        using var bitmap = new SkiaSharp.SKBitmap(4, 2);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
            canvas.Clear(new SkiaSharp.SKColor(0, 0, 0, 255));
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string WriteFixture(string directory)
    {
        PdfFonts.EnsureRegistered();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "form.pdf");
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Size = PageSize.A4;
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("form", PdfFonts.Create(14), XBrushes.Black, new XPoint(60, 60));
        document.Save(path);
        return path;
    }

    private static SignatureAnnotation Placed() => new()
    {
        PageIndex = 0,
        Rect = new PdfRect(60, 500, 140, 40)
    };

    [AvaloniaFact]
    public async Task TheLibraryStartsEmptyAndSaysSo()
    {
        using var fixture = new ServicesFixture();
        var viewModel = new MainWindowViewModel(fixture.Services, new AnsweringDialogService(MessageAnswer.Cancel));
        await viewModel.OpenAsync(WriteFixture(fixture.Root));

        await viewModel.ChooseSignatureForAsync(Placed());

        Assert.True(viewModel.IsSignaturePickerOpen);
        Assert.True(viewModel.SignaturePicker!.IsEmpty);
        Assert.False(viewModel.SignaturePicker.CanConfirm);
    }

    [AvaloniaFact]
    public async Task AStoredSignatureIsOfferedAndLandsOnThePage()
    {
        using var fixture = new ServicesFixture();
        await fixture.Services.Signatures.AddAsync("חתימה", SamplePng());

        var viewModel = new MainWindowViewModel(fixture.Services, new AnsweringDialogService(MessageAnswer.Primary));
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        var annotation = Placed();

        await viewModel.ChooseSignatureForAsync(annotation);
        Assert.Single(viewModel.SignaturePicker!.Signatures);
        Assert.True(viewModel.SignaturePicker.CanConfirm);

        await viewModel.ConfirmSignatureAsync();

        var placed = Assert.IsType<SignatureAnnotation>(Assert.Single(viewModel.Document!.Annotations));
        Assert.NotEmpty(placed.ImagePng);
        Assert.NotEmpty(placed.SignatureId);
        Assert.False(viewModel.IsSignaturePickerOpen);
    }

    [AvaloniaFact]
    public async Task ThePlacedSignatureTakesTheImagesProportions()
    {
        using var fixture = new ServicesFixture();
        await fixture.Services.Signatures.AddAsync("חתימה", SamplePng());

        var viewModel = new MainWindowViewModel(fixture.Services, new AnsweringDialogService(MessageAnswer.Primary));
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        var annotation = Placed();   // 140 × 40, which does not match the image

        await viewModel.ChooseSignatureForAsync(annotation);
        await viewModel.ConfirmSignatureAsync();

        // The image is 4×2, so a 140-wide signature must be 70 tall rather than the 40 drawn.
        var placed = Assert.IsType<SignatureAnnotation>(Assert.Single(viewModel.Document!.Annotations));
        Assert.Equal(140, placed.Rect.Width, 1);
        Assert.Equal(70, placed.Rect.Height, 1);
    }

    [AvaloniaFact]
    public async Task CancellingLeavesNothingOnThePage()
    {
        using var fixture = new ServicesFixture();
        await fixture.Services.Signatures.AddAsync("חתימה", SamplePng());

        var viewModel = new MainWindowViewModel(fixture.Services, new AnsweringDialogService(MessageAnswer.Cancel));
        await viewModel.OpenAsync(WriteFixture(fixture.Root));

        await viewModel.ChooseSignatureForAsync(Placed());
        viewModel.CancelSignature();

        // The annotation is only added on confirm, so there is nothing invisible left behind.
        Assert.Empty(viewModel.Document!.Annotations);
        Assert.False(viewModel.IsSignaturePickerOpen);
        Assert.False(viewModel.Document.IsDirty);
    }

    [AvaloniaFact]
    public async Task PlacingASignatureIsOneUndoStep()
    {
        using var fixture = new ServicesFixture();
        await fixture.Services.Signatures.AddAsync("חתימה", SamplePng());

        var viewModel = new MainWindowViewModel(fixture.Services, new AnsweringDialogService(MessageAnswer.Primary));
        await viewModel.OpenAsync(WriteFixture(fixture.Root));

        await viewModel.ChooseSignatureForAsync(Placed());
        await viewModel.ConfirmSignatureAsync();
        Assert.Single(viewModel.Document!.Annotations);

        viewModel.Undo();

        Assert.Empty(viewModel.Document.Annotations);
    }

    [AvaloniaFact]
    public async Task ADeletedSignatureLeavesTheLibrary()
    {
        using var fixture = new ServicesFixture();
        var entry = await fixture.Services.Signatures.AddAsync("חתימה", SamplePng());

        var viewModel = new MainWindowViewModel(fixture.Services, new AnsweringDialogService(MessageAnswer.Primary));
        await viewModel.OpenAsync(WriteFixture(fixture.Root));
        await viewModel.ChooseSignatureForAsync(Placed());

        Assert.Single(viewModel.SignaturePicker!.Signatures);
        await fixture.Services.Signatures.DeleteAsync(entry.Id);
        await viewModel.SignaturePicker.LoadAsync();

        Assert.True(viewModel.SignaturePicker.IsEmpty);
        Assert.False(viewModel.SignaturePicker.CanConfirm);
    }
}
