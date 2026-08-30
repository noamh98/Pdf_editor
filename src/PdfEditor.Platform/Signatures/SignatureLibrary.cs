using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdfEditor.Core.Files;
using PdfEditor.Core.Signatures;
using PdfEditor.Core.Storage;

namespace PdfEditor.Platform.Signatures;

/// <summary>
/// The local library of graphical signatures.
/// </summary>
/// <remarks>
/// Storage rules, all of which are deliberate:
/// <list type="bullet">
/// <item>Files live under <c>%LOCALAPPDATA%\PdfEditor\signatures</c>. Local, not roaming, so nothing
/// is picked up by profile synchronisation and copied to a server.</item>
/// <item>On Windows the image is protected with DPAPI in <c>CurrentUser</c> scope. That ties it to
/// the Windows account on that machine: copying the portable folder to another machine or another
/// user makes stored signatures unreadable, which is the intended trade-off.</item>
/// <item>On other platforms the image is stored unprotected and <see cref="SignatureEntry.IsProtected"/>
/// is false, so the interface can say so rather than implying protection that is not there.</item>
/// <item>Deleting overwrites the payload with random bytes first. On an SSD with wear levelling that
/// reduces recoverability but cannot guarantee erasure.</item>
/// </list>
/// A signature is never transmitted, never logged and never written to the repository.
/// </remarks>
public sealed class SignatureLibrary : ISignatureLibrary
{
    private const string MetadataExtension = ".json";
    private const string PayloadExtension = ".bin";

    /// <summary>Additional entropy mixed into DPAPI so another application's blob cannot be read.</summary>
    private static readonly byte[] Entropy = "PdfEditor.Signatures.v1"u8.ToArray();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _directory;

    public SignatureLibrary(AppPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).Signatures) { }

    public SignatureLibrary(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public string Directory => _directory;

    /// <summary>True when payloads are protected by the operating system on this platform.</summary>
    public static bool ProtectionAvailable => OperatingSystem.IsWindows();

    public Task<IReadOnlyList<SignatureEntry>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SignatureEntry>>(() =>
        {
            if (!System.IO.Directory.Exists(_directory)) return [];
            var entries = new List<SignatureEntry>();
            foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "*" + MetadataExtension))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ReadMetadata(path) is { } record) entries.Add(record.ToEntry());
            }
            return entries.OrderByDescending(e => e.CreatedUtc).ToList();
        }, cancellationToken);

    public async Task<SignatureEntry> AddAsync(string displayName, byte[] pngBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0) throw new ArgumentException("The signature image is empty.", nameof(pngBytes));

        System.IO.Directory.CreateDirectory(_directory);
        var id = Guid.NewGuid().ToString("N");
        var (width, height) = ReadPngSize(pngBytes);

        var payload = Protect(pngBytes);
        await AtomicFileWriter.WriteAsync(PayloadPath(id), payload, cancellationToken).ConfigureAwait(false);

        var record = new SignatureRecord
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "חתימה" : displayName.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow,
            PixelWidth = width,
            PixelHeight = height,
            IsProtected = ProtectionAvailable
        };
        await AtomicFileWriter.WriteAsync(MetadataPath(id),
            JsonSerializer.SerializeToUtf8Bytes(record, Json), cancellationToken).ConfigureAwait(false);

        return record.ToEntry();
    }

    public Task<byte[]?> GetImageAsync(string id, CancellationToken cancellationToken = default) =>
        Task.Run<byte[]?>(() =>
        {
            var path = PayloadPath(id);
            if (!File.Exists(path)) return null;
            try
            {
                return Unprotect(File.ReadAllBytes(path));
            }
            catch (CryptographicException)
            {
                // The blob belongs to a different Windows user or machine.
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }, cancellationToken);

    public async Task RenameAsync(string id, string newDisplayName, CancellationToken cancellationToken = default)
    {
        var path = MetadataPath(id);
        var record = ReadMetadata(path) ?? throw new FileNotFoundException("Unknown signature.", path);
        record.DisplayName = string.IsNullOrWhiteSpace(newDisplayName) ? record.DisplayName : newDisplayName.Trim();
        await AtomicFileWriter.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(record, Json), cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Task.Run(() => DeleteOne(id), cancellationToken);

    public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (!System.IO.Directory.Exists(_directory)) return 0;
            int removed = 0;
            foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "*" + MetadataExtension).ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DeleteOne(Path.GetFileNameWithoutExtension(path))) removed++;
            }
            return removed;
        }, cancellationToken);

    private bool DeleteOne(string id)
    {
        bool any = false;
        var payload = PayloadPath(id);
        try
        {
            if (File.Exists(payload))
            {
                OverwriteWithRandomBytes(payload);
                File.Delete(payload);
                any = true;
            }
            var metadata = MetadataPath(id);
            if (File.Exists(metadata))
            {
                File.Delete(metadata);
                any = true;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return any;
    }

    private static void OverwriteWithRandomBytes(string path)
    {
        try
        {
            var length = new FileInfo(path).Length;
            if (length <= 0) return;
            var noise = RandomNumberGenerator.GetBytes((int)Math.Min(length, 1 << 20));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            long written = 0;
            while (written < length)
            {
                int chunk = (int)Math.Min(noise.Length, length - written);
                stream.Write(noise, 0, chunk);
                written += chunk;
            }
            stream.Flush(flushToDisk: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static byte[] Protect(byte[] plain) =>
        OperatingSystem.IsWindows() ? ProtectWindows(plain) : plain;

    private static byte[] Unprotect(byte[] stored) =>
        OperatingSystem.IsWindows() ? UnprotectWindows(stored) : stored;

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] plain) =>
        ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] stored) =>
        ProtectedData.Unprotect(stored, Entropy, DataProtectionScope.CurrentUser);

    private string MetadataPath(string id) =>
        Path.Combine(_directory, SafeFileName.Sanitize(id, "signature") + MetadataExtension);

    private string PayloadPath(string id) =>
        Path.Combine(_directory, SafeFileName.Sanitize(id, "signature") + PayloadExtension);

    private static SignatureRecord? ReadMetadata(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<SignatureRecord>(File.ReadAllText(path), Json);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads width and height from a PNG's IHDR chunk without decoding the image.</summary>
    /// <summary>Width and height from a PNG IHDR chunk, without decoding the image.</summary>
    public static (int Width, int Height) ReadPngSize(byte[] png)
    {
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(signature)) return (0, 0);
        int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        return (Math.Max(0, width), Math.Max(0, height));
    }

    private sealed class SignatureRecord
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public bool IsProtected { get; set; }

        public SignatureEntry ToEntry() =>
            new(Id, DisplayName, CreatedUtc, PixelWidth, PixelHeight, IsProtected);
    }
}
