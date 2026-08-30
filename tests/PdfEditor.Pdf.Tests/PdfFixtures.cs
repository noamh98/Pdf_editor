using PdfEditor.Core.Text;
using PdfEditor.Pdf.Fonts;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PdfEditor.Pdf.Tests;

/// <summary>
/// Builds the PDF files the tests run against.
/// </summary>
/// <remarks>
/// Every fixture is generated in code. No real document is ever committed to the repository, so
/// the suite carries no third-party content and no personal data.
/// </remarks>
public static class PdfFixtures
{
    /// <summary>PDFsharp's A4, which uses whole points rather than the 595.276 x 841.89 ISO value.</summary>
    public const double A4Width = 595;
    public const double A4Height = 842;

    public static readonly string[] MixedLanguageLines =
    [
        "מסמך בדיקה לזיהוי תווים אופטי",
        "מספרים: 12345 ותאריך 26/08/2026",
        "This line is written in English.",
        "שורה מעורבת: הקובץ file.pdf נשמר בהצלחה"
    ];

    public static byte[] TextDocument(int pageCount = 3, bool hebrew = true, bool landscapeSecondPage = false)
    {
        PdfFonts.EnsureRegistered();
        using var doc = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            var page = doc.AddPage();
            page.Size = PageSize.A4;
            if (landscapeSecondPage && i == 1) page.Orientation = PageOrientation.Landscape;

            using var gfx = XGraphics.FromPdfPage(page);
            var font = PdfFonts.Create(16);
            gfx.DrawString($"Page {i + 1}", font, XBrushes.Black, new XPoint(50, 60));
            if (hebrew)
            {
                double y = 110;
                foreach (var line in MixedLanguageLines)
                {
                    var analysis = BidiAlgorithm.Analyze(line);
                    var visual = BidiAlgorithm.ToVisual(analysis);
                    double width = gfx.MeasureString(visual, font).Width;
                    double x = analysis.IsRightToLeftParagraph ? page.Width.Point - 50 - width : 50;
                    gfx.DrawString(visual, font, XBrushes.Black, new XPoint(x, y));
                    y += 34;
                }
            }
        }
        return Save(doc);
    }

    /// <summary>A document whose second page is deliberately empty, for the print pipeline tests.</summary>
    public static byte[] WithBlankPages()
    {
        PdfFonts.EnsureRegistered();
        using var doc = new PdfDocument();
        foreach (bool hasContent in new[] { true, false, true, false })
        {
            var page = doc.AddPage();
            page.Size = PageSize.A4;
            if (!hasContent) continue;
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString("content", PdfFonts.Create(20), XBrushes.Black, new XPoint(60, 80));
        }
        return Save(doc);
    }

    public static byte[] MixedPageSizes()
    {
        PdfFonts.EnsureRegistered();
        using var doc = new PdfDocument();

        var a4 = doc.AddPage();
        a4.Size = PageSize.A4;

        var letter = doc.AddPage();
        letter.Width = XUnit.FromPoint(612);
        letter.Height = XUnit.FromPoint(792);

        var landscape = doc.AddPage();
        landscape.Size = PageSize.A4;
        landscape.Orientation = PageOrientation.Landscape;

        var rotated = doc.AddPage();
        rotated.Size = PageSize.A4;
        rotated.Rotate = 90;

        foreach (var page in new[] { a4, letter, landscape, rotated })
        {
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString("x", PdfFonts.Create(24), XBrushes.Black, new XPoint(40, 60));
        }
        return Save(doc);
    }

    public static byte[] Large(int pageCount = 200)
    {
        PdfFonts.EnsureRegistered();
        using var doc = new PdfDocument();
        var font = PdfFonts.Create(12);
        for (int i = 0; i < pageCount; i++)
        {
            var page = doc.AddPage();
            page.Size = PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"{i + 1}", font, XBrushes.Black, new XPoint(40, 50));
        }
        return Save(doc);
    }

    /// <summary>Starts with a valid header and then degenerates, so the parser must reject it.</summary>
    public static byte[] Malformed()
    {
        var header = "%PDF-1.7\n"u8.ToArray();
        var junk = new byte[2048];
        new Random(20260826).NextBytes(junk);
        return [.. header, .. junk];
    }

    public static byte[] NotAPdf() => "This is a plain text file, not a PDF."u8.ToArray();

    private static byte[] Save(PdfDocument doc)
    {
        using var buffer = new MemoryStream();
        doc.Save(buffer, closeStream: false);
        return buffer.ToArray();
    }
}

/// <summary>A directory that deletes itself when the test finishes.</summary>
public sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "pdfeditor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string name) => System.IO.Path.Combine(Root, name);

    public string Write(string name, byte[] content)
    {
        var path = File(name);
        System.IO.File.WriteAllBytes(path, content);
        return path;
    }

    public static string Sha256(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
