using System.Text.Json;
using System.Text.Json.Serialization;
using PdfEditor.Core.Annotations;
using PdfEditor.Core.Text;

namespace PdfEditor.Pdf.Annotations;

/// <summary>
/// Round-trips the application's annotation model through a compact JSON payload.
/// </summary>
/// <remarks>
/// Standard PDF annotation entries do not carry everything this editor needs — text alignment,
/// base direction, font size, per-stroke ink geometry in our own coordinates. The payload is stored
/// under a private key in the annotation dictionary, which the PDF specification permits and other
/// viewers ignore, so an annotation reopens exactly as it was authored while still rendering
/// correctly everywhere through its appearance stream.
/// </remarks>
public static class AnnotationSerializer
{
    /// <summary>Private annotation dictionary key holding the payload.</summary>
    public const string PrivateKey = "/PdfEditorData";

    /// <summary>Payload schema version, so a future change can migrate rather than misread.</summary>
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        var dto = new AnnotationDto
        {
            V = SchemaVersion,
            Kind = annotation.Kind,
            Id = annotation.Id,
            Page = annotation.PageIndex,
            Rect = [annotation.Rect.X, annotation.Rect.Y, annotation.Rect.Width, annotation.Rect.Height],
            Color = annotation.Color.ToHex(),
            LineWidth = annotation.LineWidth,
            Opacity = annotation.Opacity,
            Rotation = annotation.Rotation,
            Created = annotation.CreatedUtc,
            Modified = annotation.ModifiedUtc
        };

        switch (annotation)
        {
            case TextBoxAnnotation t:
                dto.Text = t.Text;
                dto.FontSize = t.FontSize;
                dto.Bold = t.Bold;
                dto.Italic = t.Italic;
                dto.TextColor = t.TextColor.ToHex();
                dto.Background = t.BackgroundColor?.ToHex();
                dto.Border = t.BorderColor?.ToHex();
                dto.Alignment = t.Alignment;
                dto.Padding = t.Padding;
                dto.Direction = t.Direction;
                break;
            case ShapeAnnotation s:
                dto.Fill = s.FillColor?.ToHex();
                dto.Start = [s.Start.X, s.Start.Y];
                dto.End = [s.End.X, s.End.Y];
                break;
            case InkAnnotation i:
                dto.Strokes = i.Strokes
                    .Select(stroke => stroke.SelectMany(p => new[] { p.X, p.Y }).ToArray())
                    .ToList();
                break;
            case SignatureAnnotation g:
                dto.SignatureId = g.SignatureId;
                break;
        }
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Returns null when the payload is absent, malformed or from an unknown version.</summary>
    public static Annotation? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        AnnotationDto? dto;
        try { dto = JsonSerializer.Deserialize<AnnotationDto>(json, Options); }
        catch (JsonException) { return null; }
        if (dto is null || dto.V > SchemaVersion || dto.Rect is not { Length: 4 }) return null;

        Annotation annotation;
        switch (dto.Kind)
        {
            case AnnotationKind.TextBox:
                annotation = new TextBoxAnnotation
                {
                    Text = dto.Text ?? string.Empty,
                    FontSize = dto.FontSize ?? 14,
                    Bold = dto.Bold ?? false,
                    Italic = dto.Italic ?? false,
                    TextColor = ParseColor(dto.TextColor) ?? AnnotationColor.Black,
                    BackgroundColor = ParseColor(dto.Background),
                    BorderColor = ParseColor(dto.Border),
                    Alignment = dto.Alignment ?? TextAlignment.Start,
                    Padding = dto.Padding ?? 4,
                    Direction = dto.Direction ?? BidiParagraphDirection.Auto
                };
                break;

            case AnnotationKind.Rectangle:
            case AnnotationKind.Ellipse:
            case AnnotationKind.Line:
            case AnnotationKind.Arrow:
            case AnnotationKind.Highlight:
                annotation = new ShapeAnnotation(dto.Kind)
                {
                    FillColor = ParseColor(dto.Fill),
                    Start = ParsePoint(dto.Start),
                    End = ParsePoint(dto.End)
                };
                break;

            case AnnotationKind.Ink:
                var ink = new InkAnnotation();
                foreach (var flat in dto.Strokes ?? [])
                {
                    var stroke = new List<PdfPoint>(flat.Length / 2);
                    for (int i = 0; i + 1 < flat.Length; i += 2) stroke.Add(new PdfPoint(flat[i], flat[i + 1]));
                    ink.Strokes.Add(stroke);
                }
                annotation = ink;
                break;

            case AnnotationKind.CheckMark:
            case AnnotationKind.CrossMark:
                annotation = new MarkAnnotation(dto.Kind);
                break;

            case AnnotationKind.Signature:
                annotation = new SignatureAnnotation { SignatureId = dto.SignatureId ?? string.Empty };
                break;

            default:
                return null;
        }

        annotation.Id = string.IsNullOrEmpty(dto.Id) ? annotation.Id : dto.Id;
        annotation.PageIndex = dto.Page;
        annotation.Rect = new PdfRect(dto.Rect[0], dto.Rect[1], dto.Rect[2], dto.Rect[3]);
        annotation.Color = ParseColor(dto.Color) ?? AnnotationColor.Red;
        annotation.LineWidth = dto.LineWidth;
        annotation.Opacity = dto.Opacity;
        annotation.Rotation = dto.Rotation;
        annotation.CreatedUtc = dto.Created;
        annotation.ModifiedUtc = dto.Modified;
        return annotation;
    }

    private static AnnotationColor? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return AnnotationColor.FromHex(hex); }
        catch (FormatException) { return null; }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static PdfPoint ParsePoint(double[]? values) =>
        values is { Length: 2 } ? new PdfPoint(values[0], values[1]) : default;

    private sealed class AnnotationDto
    {
        public int V { get; set; }
        public AnnotationKind Kind { get; set; }
        public string? Id { get; set; }
        public int Page { get; set; }
        public double[]? Rect { get; set; }
        public string? Color { get; set; }
        public double LineWidth { get; set; }
        public double Opacity { get; set; } = 1;
        public double Rotation { get; set; }
        public DateTimeOffset Created { get; set; }
        public DateTimeOffset Modified { get; set; }

        public string? Text { get; set; }
        public double? FontSize { get; set; }
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public string? TextColor { get; set; }
        public string? Background { get; set; }
        public string? Border { get; set; }
        public TextAlignment? Alignment { get; set; }
        public double? Padding { get; set; }
        public BidiParagraphDirection? Direction { get; set; }

        public string? Fill { get; set; }
        public double[]? Start { get; set; }
        public double[]? End { get; set; }

        public List<double[]>? Strokes { get; set; }
        public string? SignatureId { get; set; }
    }
}
