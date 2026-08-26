using System.Text;

namespace PdfEditor.Core.Files;

/// <summary>
/// Builds output file names that are safe on Windows and cannot escape their target directory.
/// </summary>
/// <remarks>
/// PDF files are untrusted input: any string taken from a document (title, embedded file name,
/// bookmark) must pass through here before it is used as part of a path.
/// </remarks>
public static class SafeFileName
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars()
        .Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).Distinct().ToArray();

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public const int MaxComponentLength = 120;

    /// <summary>Strips path separators, control and reserved characters. Never returns empty.</summary>
    public static string Sanitize(string? candidate, string fallback = "document")
    {
        if (string.IsNullOrWhiteSpace(candidate)) return fallback;

        var sb = new StringBuilder(candidate.Length);
        foreach (char c in candidate)
        {
            if (char.IsControl(c)) continue;
            // Strip bidi control characters: they are invisible and can disguise an extension.
            if (c is '‎' or '‏' or '‪' or '‫' or '‬' or '‭' or '‮'
                or '⁦' or '⁧' or '⁨' or '⁩') continue;
            sb.Append(Array.IndexOf(Invalid, c) >= 0 ? '_' : c);
        }

        var name = sb.ToString().Trim().Trim('.');
        if (name.Length == 0) return fallback;
        if (name.Length > MaxComponentLength) name = name[..MaxComponentLength].Trim();
        if (name.Length == 0) return fallback;

        var withoutExt = Path.GetFileNameWithoutExtension(name);
        if (ReservedDeviceNames.Contains(withoutExt)) name = "_" + name;
        return name;
    }

    /// <summary>
    /// Combines a directory and a candidate file name, guaranteeing the result stays inside the
    /// directory. Throws when the candidate tries to traverse out of it.
    /// </summary>
    public static string CombineWithin(string directory, string candidateFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var safe = Sanitize(candidateFileName);
        var root = Path.GetFullPath(directory);
        var combined = Path.GetFullPath(Path.Combine(root, safe));

        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSep, StringComparison.Ordinal))
            throw new InvalidOperationException($"Resolved path escapes the target directory: '{candidateFileName}'.");
        return combined;
    }

    /// <summary>
    /// Returns a path that does not exist yet by appending " (2)", " (3)"… before the extension.
    /// </summary>
    public static string MakeUnique(string desiredPath, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (!exists(desiredPath)) return desiredPath;

        var dir = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);
        for (int i = 2; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!exists(candidate)) return candidate;
        }
        throw new IOException("Could not find an unused file name.");
    }

    /// <summary>
    /// Derives the suggested name for a produced file, e.g. report.pdf + "final" -> report - final.pdf.
    /// </summary>
    public static string DeriveOutputName(string sourcePath, string suffix, string extension = ".pdf")
    {
        var stem = Sanitize(Path.GetFileNameWithoutExtension(sourcePath));
        var safeSuffix = Sanitize(suffix, "copy");
        return $"{stem} - {safeSuffix}{extension}";
    }
}
