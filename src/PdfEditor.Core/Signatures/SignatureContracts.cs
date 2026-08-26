namespace PdfEditor.Core.Signatures;

/// <summary>Metadata for one stored graphical signature. Never contains the image itself.</summary>
public sealed record SignatureEntry(
    string Id,
    string DisplayName,
    DateTimeOffset CreatedUtc,
    int PixelWidth,
    int PixelHeight,
    bool IsProtected)
{
    /// <summary>Aspect ratio used to keep placement proportional.</summary>
    public double AspectRatio => PixelHeight == 0 ? 1 : (double)PixelWidth / PixelHeight;
}

/// <summary>
/// Local library of graphical signatures.
/// </summary>
/// <remarks>
/// Signatures are personal data. Implementations must store them only under the current user's
/// local application data, must never write them to a roaming or synchronised location, and must
/// never transmit them. On Windows the payload is protected with DPAPI in
/// <c>CurrentUser</c> scope, which ties it to the Windows user profile on that machine.
/// </remarks>
public interface ISignatureLibrary
{
    Task<IReadOnlyList<SignatureEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores a PNG with transparency and returns its metadata.</summary>
    Task<SignatureEntry> AddAsync(string displayName, byte[] pngBytes, CancellationToken cancellationToken = default);

    /// <summary>Returns the PNG bytes, or null when the entry is missing or cannot be decrypted.</summary>
    Task<byte[]?> GetImageAsync(string id, CancellationToken cancellationToken = default);

    Task RenameAsync(string id, string newDisplayName, CancellationToken cancellationToken = default);

    /// <summary>Permanently removes one signature, overwriting the file before deleting it.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Permanently removes every stored signature.</summary>
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Trims fully transparent margins and normalises a drawn or imported signature.</summary>
public interface ISignatureImageProcessor
{
    /// <summary>
    /// Crops transparent borders, optionally removes a near-white background, and returns PNG bytes
    /// with an alpha channel.
    /// </summary>
    byte[] Normalize(byte[] imageBytes, bool removeWhiteBackground, out int width, out int height);
}
