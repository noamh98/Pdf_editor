using PdfSharp.Fonts;

namespace PdfEditor.Pdf.Fonts;

/// <summary>
/// Supplies font data to PDFsharp from files shipped with the application.
/// </summary>
/// <remarks>
/// The application must work with no network and no assumption about installed fonts, so every
/// font it embeds into a PDF comes from the <c>fonts</c> folder next to the executable. A small set
/// of well-known system paths is probed only as a development and test fallback; a release package
/// always carries its own font.
/// </remarks>
public sealed class BundledFontResolver : IFontResolver
{
    public const string RegularFace = "app-regular";
    public const string BoldFace = "app-bold";
    public const string ItalicFace = "app-italic";
    public const string BoldItalicFace = "app-bold-italic";

    private static readonly string[] FallbackProbePaths =
    [
        // Linux development machines and CI.
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/freefont/FreeSans.ttf",
        // Windows, when the bundled folder is missing during development.
        @"C:\Windows\Fonts\arial.ttf",
        @"C:\Windows\Fonts\segoeui.ttf"
    ];

    private static readonly string[] BoldProbePaths =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        @"C:\Windows\Fonts\arialbd.ttf",
        @"C:\Windows\Fonts\segoeuib.ttf"
    ];

    private readonly Dictionary<string, byte[]> _faces = new(StringComparer.Ordinal);

    /// <summary>Directory the resolver loaded its fonts from, or null when a fallback was used.</summary>
    public string? FontDirectory { get; }

    /// <summary>True when at least one face resolved to real font data.</summary>
    public bool HasFontData => _faces.Count > 0;

    public BundledFontResolver(string? fontDirectory = null)
    {
        fontDirectory ??= Path.Combine(AppContext.BaseDirectory, "fonts");
        if (Directory.Exists(fontDirectory))
        {
            FontDirectory = fontDirectory;
            LoadFromDirectory(fontDirectory);
        }

        if (!_faces.ContainsKey(RegularFace)) TryProbe(RegularFace, FallbackProbePaths);
        if (!_faces.ContainsKey(BoldFace)) TryProbe(BoldFace, BoldProbePaths);

        // Synthesise the remaining faces from what is available so nothing resolves to null.
        if (_faces.TryGetValue(RegularFace, out var regular))
        {
            _faces.TryAdd(BoldFace, regular);
            _faces.TryAdd(ItalicFace, regular);
            _faces.TryAdd(BoldItalicFace, _faces[BoldFace]);
        }
    }

    private void LoadFromDirectory(string directory)
    {
        // Any .ttf/.otf in the folder is a candidate; the file name decides which face it fills.
        foreach (var path in Directory.EnumerateFiles(directory)
                     .Where(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                              || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            bool bold = name.Contains("bold", StringComparison.OrdinalIgnoreCase);
            bool italic = name.Contains("italic", StringComparison.OrdinalIgnoreCase)
                       || name.Contains("oblique", StringComparison.OrdinalIgnoreCase);
            var face = FaceKey(bold, italic);
            if (_faces.ContainsKey(face)) continue;
            try { _faces[face] = File.ReadAllBytes(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void TryProbe(string face, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                _faces[face] = File.ReadAllBytes(path);
                return;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string FaceKey(bool bold, bool italic) => (bold, italic) switch
    {
        (true, true) => BoldItalicFace,
        (true, false) => BoldFace,
        (false, true) => ItalicFace,
        _ => RegularFace
    };

    public byte[]? GetFont(string faceName) => _faces.GetValueOrDefault(faceName);

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var face = FaceKey(bold, italic);
        return _faces.ContainsKey(face) ? new FontResolverInfo(face) : null;
    }
}
