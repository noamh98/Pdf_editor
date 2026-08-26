using System.Security.Cryptography;
using System.Text;

namespace PdfEditor.Core.Ocr;

/// <summary>
/// Identifies a cached OCR result. The key deliberately contains no file path and no document
/// content: only a fingerprint, so the cache directory leaks neither file names nor text.
/// </summary>
public readonly record struct OcrCacheKey(string DocumentFingerprint, int PageIndex, OcrLanguage Language, int Dpi)
{
    /// <summary>File-system-safe name for this entry.</summary>
    public string ToFileName()
    {
        var raw = $"{DocumentFingerprint}|{PageIndex}|{Language}|{Dpi}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant() + ".json";
    }

    /// <summary>
    /// Builds a fingerprint from a document's bytes. Uses the file length plus a SHA-256 over the
    /// head, tail and length so very large files do not have to be hashed in full.
    /// </summary>
    public static string FingerprintFile(string path, int sampleBytes = 1 << 20)
    {
        using var stream = File.OpenRead(path);
        return FingerprintStream(stream, sampleBytes);
    }

    public static string FingerprintStream(Stream stream, int sampleBytes = 1 << 20)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek) throw new ArgumentException("A seekable stream is required.", nameof(stream));

        long length = stream.Length;
        using var sha = SHA256.Create();
        Span<byte> lengthBytes = stackalloc byte[8];
        BitConverter.TryWriteBytes(lengthBytes, length);
        sha.TransformBlock(lengthBytes.ToArray(), 0, 8, null, 0);

        int take = (int)Math.Min(sampleBytes, length);
        var buffer = new byte[take];

        stream.Position = 0;
        ReadExactly(stream, buffer, take);
        sha.TransformBlock(buffer, 0, take, null, 0);

        if (length > take)
        {
            stream.Position = length - take;
            ReadExactly(stream, buffer, take);
            sha.TransformBlock(buffer, 0, take, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();

        static void ReadExactly(Stream s, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n <= 0) break;
                read += n;
            }
        }
    }
}
