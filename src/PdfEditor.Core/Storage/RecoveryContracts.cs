using System.Text.Json;
using System.Text.Json.Serialization;
using PdfEditor.Core.Annotations;
using PdfEditor.Core.Files;

namespace PdfEditor.Core.Storage;

/// <summary>
/// Describes one interrupted editing session so the user can be offered a recovery on next start.
/// </summary>
/// <remarks>
/// The manifest stores a path and a fingerprint, plus a sidecar file holding the unsaved
/// annotations. The original document is never modified and never copied.
/// </remarks>
public sealed record RecoverySession
{
    public required string SessionId { get; init; }
    public required string SourcePath { get; init; }
    public required string SourceFingerprint { get; init; }
    public required DateTimeOffset LastAutosaveUtc { get; init; }
    public required int AnnotationCount { get; init; }
    public required string AnnotationsFileName { get; init; }
    public int ProcessId { get; init; }

    /// <summary>The source file has been replaced since the autosave was taken.</summary>
    public bool IsStale(string currentFingerprint) =>
        !string.Equals(SourceFingerprint, currentFingerprint, StringComparison.Ordinal);
}

public sealed record RecoveryManifest
{
    public List<RecoverySession> Sessions { get; init; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static RecoveryManifest Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new RecoveryManifest();
            return JsonSerializer.Deserialize<RecoveryManifest>(File.ReadAllText(path), Json)
                   ?? new RecoveryManifest();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new RecoveryManifest();
        }
    }

    public Task SaveAsync(string path, CancellationToken cancellationToken = default) =>
        AtomicFileWriter.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(this, Json), cancellationToken);
}

/// <summary>
/// Identifies the bytes a recovery session was taken against, so a sidecar is not silently
/// re-applied to a file that has been replaced since.
/// </summary>
/// <remarks>
/// Length and last-write time rather than a content hash: a PDF can be hundreds of megabytes and
/// this runs on every open. It detects replacement, which is what the offer needs to know; it is
/// not a integrity check and is not relied on as one.
/// </remarks>
public static class SourceFingerprint
{
    public static string For(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "missing";
            return $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "unreadable";
        }
    }
}

/// <summary>Periodically persists unsaved work and offers it back after a crash.</summary>
public interface IAutosaveService : IAsyncDisposable
{
    /// <summary>
    /// Starts tracking an open document and returns the identifier its autosaves are filed under.
    /// A session is owned by this process until it is discarded or the process dies.
    /// </summary>
    string BeginSession(string sourcePath, string sourceFingerprint);

    /// <summary>
    /// Writes the current unsaved annotations for a tracked session. Called on a timer and again
    /// whenever the document is about to be left, so the sidecar never trails the editor by more
    /// than one interval.
    /// </summary>
    Task SaveAsync(string sessionId, IReadOnlyList<Annotation> annotations, CancellationToken cancellationToken = default);

    /// <summary>Reads back the annotations held for a session so they can be re-applied.</summary>
    Task<IReadOnlyList<Annotation>> RestoreAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Sessions found on start that can be offered to the user.</summary>
    Task<IReadOnlyList<RecoverySession>> FindRecoverableSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes a session's files once it has been recovered or explicitly discarded.</summary>
    Task DiscardAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every recovery file. Exposed in settings as "נקה קובצי שחזור".</summary>
    Task<int> ClearAllAsync(CancellationToken cancellationToken = default);
}
