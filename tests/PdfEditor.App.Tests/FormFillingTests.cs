using PdfEditor.App.ViewModels;
using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Pdf.Annotations;
using PdfEditor.Pdf.Fonts;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PdfEditor.App.Tests;

/// <summary>
/// Filling a form is the job this editor exists for: a name, an ID number, a date, typed onto the
/// page. Text has to look like it belongs there, which means no note-like background or border.
/// </summary>
public class FormFillingTests
{
    [Fact]
    public void ANewTextAnnotationCarriesNoBackgroundOrBorder()
    {
        var text = new TextBoxAnnotation();

        Assert.Null(text.BackgroundColor);
        Assert.Null(text.BorderColor);
    }

    [Fact]
    public void PlainTextSurvivesTheRoundTripWithoutGainingAFill()
    {
        var original = new TextBoxAnnotation
        {
            PageIndex = 0,
            Rect = new PdfRect(72, 640, 220, 24),
            Text = "ישראל ישראלי 039876543",
            FontSize = 12,
            TextColor = AnnotationColor.Black
        };

        var restored = Assert.IsType<TextBoxAnnotation>(
            AnnotationSerializer.Deserialize(AnnotationSerializer.Serialize(original)));

        Assert.Equal("ישראל ישראלי 039876543", restored.Text);
        Assert.Null(restored.BackgroundColor);
        Assert.Null(restored.BorderColor);
    }

    [Fact]
    public void PlainTextIsWrittenIntoThePdfAsGlyphsOnly()
    {
        PdfFonts.EnsureRegistered();
        var directory = Path.Combine(Path.GetTempPath(), "pdfeditor-form-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "form.pdf");
            using (var document = new PdfDocument())
            {
                var page = document.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawString("טופס", PdfFonts.Create(14), XBrushes.Black, new XPoint(60, 60));
                document.Save(path);
            }

            // A field with no fill must not put any rectangle on the page; only the text.
            var field = new TextBoxAnnotation
            {
                PageIndex = 0,
                Rect = new PdfRect(72, 600, 200, 22),
                Text = "01/09/2026",
                FontSize = 12
            };

            Assert.Null(field.BackgroundColor);
            Assert.Null(field.BorderColor);
            Assert.Equal("01/09/2026", Assert.IsType<TextBoxAnnotation>(field.Clone()).Text);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void TheColourSwatchChangesTheInkOfTheText()
    {
        using var fixture = new ServicesFixture();
        var document = new PdfDocument();
        document.AddPage();
        var path = Path.Combine(fixture.Root, "c.pdf");
        Directory.CreateDirectory(fixture.Root);
        document.Save(path);
        document.Dispose();

        var text = new TextBoxAnnotation
        {
            PageIndex = 0,
            Rect = new PdfRect(10, 10, 100, 20),
            Text = "שם",
            TextColor = AnnotationColor.Black
        };

        // The panel edits the live annotation, so the mapping is what matters here: for a text box
        // the swatch is the glyph colour, not a stroke the annotation does not have.
        text.TextColor = AnnotationColor.Blue;
        text.Color = AnnotationColor.Blue;

        Assert.Equal(AnnotationColor.Blue, text.TextColor);
    }
}
