using PdfEditor.Core.Printing;
using PdfEditor.Platform.Files;
using PdfEditor.Platform.Printing;
using PdfEditor.Platform.Signatures;
using SkiaSharp;
using Xunit;

namespace PdfEditor.Ocr.Tests;

internal sealed class TempRoot : IDisposable
{
    public TempRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pdfeditor-platform", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public class SignatureImageProcessorTests
{
    /// <summary>A bitmap with a solid block of ink at a known position and empty margins.</summary>
    private static byte[] BitmapWithInk(int width, int height, SKRectI ink, SKColor background)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(background);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            canvas.DrawRect(ink, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void CropsTransparentMarginsDownToTheInk()
    {
        var input = BitmapWithInk(200, 100, new SKRectI(50, 30, 90, 60), SKColors.Transparent);

        var output = new SignatureImageProcessor { Margin = 0 }
            .Normalize(input, removeWhiteBackground: false, out int width, out int height);

        Assert.Equal(40, width);
        Assert.Equal(30, height);
        Assert.NotEmpty(output);
    }

    [Fact]
    public void KeepsTheConfiguredMarginAroundTheInk()
    {
        var input = BitmapWithInk(200, 100, new SKRectI(50, 30, 90, 60), SKColors.Transparent);

        new SignatureImageProcessor { Margin = 3 }
            .Normalize(input, removeWhiteBackground: false, out int width, out int height);

        Assert.Equal(46, width);
        Assert.Equal(36, height);
    }

    [Fact]
    public void RemovesANearWhiteBackgroundSoTheSignatureIsTransparent()
    {
        var input = BitmapWithInk(120, 80, new SKRectI(40, 20, 80, 60), new SKColor(252, 252, 250));

        var output = new SignatureImageProcessor { Margin = 0 }
            .Normalize(input, removeWhiteBackground: true, out int width, out int height);

        Assert.Equal(40, width);
        Assert.Equal(40, height);

        using var decoded = SKBitmap.Decode(output);
        Assert.Equal(SKColors.Black.Red, decoded.GetPixel(20, 20).Red);
        Assert.True(decoded.Info.AlphaType != SKAlphaType.Opaque);
    }

    [Fact]
    public void KeepsTheWhiteBackgroundWhenRemovalIsNotRequested()
    {
        var input = BitmapWithInk(120, 80, new SKRectI(40, 20, 80, 60), SKColors.White);

        new SignatureImageProcessor { Margin = 0 }
            .Normalize(input, removeWhiteBackground: false, out int width, out int height);

        // Every pixel is opaque, so nothing is cropped away.
        Assert.Equal(120, width);
        Assert.Equal(80, height);
    }

    [Fact]
    public void RejectsAnImageWithNoVisibleInk()
    {
        var blank = BitmapWithInk(50, 50, SKRectI.Empty, SKColors.Transparent);
        var processor = new SignatureImageProcessor();

        Assert.Throws<ArgumentException>(() => processor.Normalize(blank, false, out _, out _));
    }

    [Fact]
    public void RejectsDataThatIsNotAnImage()
    {
        var processor = new SignatureImageProcessor();
        Assert.Throws<ArgumentException>(() =>
            processor.Normalize("not an image"u8.ToArray(), false, out _, out _));
        Assert.Throws<ArgumentException>(() => processor.Normalize([], false, out _, out _));
    }
}

public class SignatureLibraryTests
{
    private static CancellationToken Ct => TestCancellation.Token;

    private static byte[] SamplePng(int width = 60, int height = 24)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.DarkBlue };
            canvas.DrawRect(new SKRect(2, 2, width - 2, height - 2), paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task AddsListsAndReadsBackASignature()
    {
        using var root = new TempRoot();
        var library = new SignatureLibrary(root.Path);
        var png = SamplePng();

        var entry = await library.AddAsync("החתימה שלי", png, Ct);

        Assert.Equal("החתימה שלי", entry.DisplayName);
        Assert.Equal(60, entry.PixelWidth);
        Assert.Equal(24, entry.PixelHeight);
        Assert.Equal(2.5, entry.AspectRatio, 3);

        var listed = Assert.Single(await library.ListAsync(Ct));
        Assert.Equal(entry.Id, listed.Id);
        Assert.Equal(png, await library.GetImageAsync(entry.Id, Ct));
    }

    [Fact]
    public async Task ReportsProtectionHonestlyForThisPlatform()
    {
        using var root = new TempRoot();
        var entry = await new SignatureLibrary(root.Path).AddAsync("x", SamplePng(), Ct);

        Assert.Equal(SignatureLibrary.ProtectionAvailable, entry.IsProtected);
        Assert.Equal(OperatingSystem.IsWindows(), SignatureLibrary.ProtectionAvailable);
    }

    [Fact]
    public async Task RenamesWithoutTouchingTheImage()
    {
        using var root = new TempRoot();
        var library = new SignatureLibrary(root.Path);
        var png = SamplePng();
        var entry = await library.AddAsync("ישן", png, Ct);

        await library.RenameAsync(entry.Id, "חדש", Ct);

        Assert.Equal("חדש", Assert.Single(await library.ListAsync(Ct)).DisplayName);
        Assert.Equal(png, await library.GetImageAsync(entry.Id, Ct));
    }

    [Fact]
    public async Task DeleteRemovesBothTheMetadataAndThePayload()
    {
        using var root = new TempRoot();
        var library = new SignatureLibrary(root.Path);
        var entry = await library.AddAsync("x", SamplePng(), Ct);

        Assert.True(await library.DeleteAsync(entry.Id, Ct));

        Assert.Empty(await library.ListAsync(Ct));
        Assert.Null(await library.GetImageAsync(entry.Id, Ct));
        Assert.Empty(Directory.GetFiles(root.Path));
    }

    [Fact]
    public async Task DeleteAllEmptiesTheLibrary()
    {
        using var root = new TempRoot();
        var library = new SignatureLibrary(root.Path);
        for (int i = 0; i < 3; i++) await library.AddAsync($"s{i}", SamplePng(), Ct);

        Assert.Equal(3, await library.DeleteAllAsync(Ct));
        Assert.Empty(await library.ListAsync(Ct));
        Assert.Empty(Directory.GetFiles(root.Path));
    }

    [Fact]
    public async Task DeletingSomethingThatIsNotThereReportsFalse()
    {
        using var root = new TempRoot();
        Assert.False(await new SignatureLibrary(root.Path).DeleteAsync("nope", Ct));
    }

    [Fact]
    public async Task ListingAnEmptyLibraryReturnsNothing()
    {
        using var root = new TempRoot();
        Assert.Empty(await new SignatureLibrary(root.Path).ListAsync(Ct));
    }

    [Fact]
    public async Task RejectsAnEmptyImage()
    {
        using var root = new TempRoot();
        await Assert.ThrowsAsync<ArgumentException>(
            () => new SignatureLibrary(root.Path).AddAsync("x", [], Ct));
    }

    [Fact]
    public async Task AnIdentifierCannotEscapeTheSignatureDirectory()
    {
        using var root = new TempRoot();
        var library = new SignatureLibrary(root.Path);
        await library.AddAsync("x", SamplePng(), Ct);

        // A traversal attempt must not reach a file outside the directory.
        Assert.Null(await library.GetImageAsync("../../secret", Ct));
        Assert.False(await library.DeleteAsync("../../secret", Ct));
        Assert.Single(await library.ListAsync(Ct));
    }

    [Fact]
    public void ReadsPngDimensionsFromTheHeader()
    {
        var (width, height) = SignatureLibrary.ReadPngSize(SamplePng(123, 45));
        Assert.Equal(123, width);
        Assert.Equal(45, height);
    }

    [Fact]
    public void ReportsZeroDimensionsForSomethingThatIsNotAPng()
    {
        Assert.Equal((0, 0), SignatureLibrary.ReadPngSize("nope"u8.ToArray()));
    }
}

public class TempFileJanitorTests
{
    [Fact]
    public void TracksAndDeletesFilesInsideItsRoot()
    {
        using var root = new TempRoot();
        var janitor = new TempFileJanitor(root.Path);
        var file = Path.Combine(root.Path, "job.pdf");
        File.WriteAllText(file, "x");

        janitor.Track(file);
        Assert.Equal(1, janitor.ReleaseAll());
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void RefusesToTrackAPathOutsideItsRoot()
    {
        using var root = new TempRoot();
        using var other = new TempRoot();
        var janitor = new TempFileJanitor(root.Path);
        var outside = Path.Combine(other.Path, "elsewhere.pdf");
        File.WriteAllText(outside, "x");

        Assert.Throws<InvalidOperationException>(() => janitor.Track(outside));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void ReleaseIgnoresAPathOutsideItsRoot()
    {
        using var root = new TempRoot();
        using var other = new TempRoot();
        var janitor = new TempFileJanitor(root.Path);
        var outside = Path.Combine(other.Path, "elsewhere.pdf");
        File.WriteAllText(outside, "x");

        Assert.False(janitor.Release(outside));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void DisposingDeletesEverythingItTracked()
    {
        using var root = new TempRoot();
        var files = new List<string>();
        using (var janitor = new TempFileJanitor(root.Path))
        {
            for (int i = 0; i < 3; i++)
            {
                var file = Path.Combine(root.Path, $"job{i}.pdf");
                File.WriteAllText(file, "x");
                janitor.Track(file);
                files.Add(file);
            }
        }
        Assert.All(files, f => Assert.False(File.Exists(f)));
    }

    [Fact]
    public void CleanupRemovesOnlyLeftoversOlderThanTheLimit()
    {
        using var root = new TempRoot();
        var stale = Path.Combine(root.Path, "stale.pdf");
        var fresh = Path.Combine(root.Path, "fresh.pdf");
        File.WriteAllText(stale, "x");
        File.WriteAllText(fresh, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-5));

        var janitor = new TempFileJanitor(root.Path);
        Assert.Equal(1, janitor.CleanupOrphans(TimeSpan.FromHours(1)));

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void CleanupOnAMissingDirectoryIsHarmless()
    {
        var janitor = new TempFileJanitor(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Equal(0, janitor.CleanupOrphans(TimeSpan.Zero));
    }
}

public class WindowsPrintServiceTests
{
    private static CancellationToken Ct => TestCancellation.Token;

    private static WindowsPrintService Service() =>
        new((_, _, _) => Task.FromResult<byte[]>([1, 2, 3]));

    [Fact]
    public void ReportsSupportOnlyOnWindows()
    {
        Assert.Equal(OperatingSystem.IsWindows(), Service().IsSupported);
    }

    [Fact]
    public async Task ListingPrintersOnAnUnsupportedPlatformReturnsNothing()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Empty(await Service().GetPrintersAsync(Ct));
    }

    [Fact]
    public async Task PrintingOnAnUnsupportedPlatformFailsGracefullyInHebrew()
    {
        if (OperatingSystem.IsWindows()) return;

        var sequence = PrintSequenceBuilder.Build(
            [new PrintPageInfo(0, 595, 842, 0, false)],
            new PrintSequenceOptions { SeparateSheetsPerContentPage = true });

        var result = await Service().PrintAsync(
            new PrintJobRequest("Any printer", "job.pdf", sequence), null, Ct);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.PagesSent);
        Assert.Contains("Windows", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintingAnEmptySequenceIsRejected()
    {
        var empty = PrintSequenceBuilder.Build([], new PrintSequenceOptions());

        var result = await Service().PrintAsync(
            new PrintJobRequest("Any printer", "job.pdf", empty), null, Ct);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void RequiresARenderDelegate()
    {
        Assert.Throws<ArgumentNullException>(() => new WindowsPrintService(null!));
    }
}
