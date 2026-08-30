using System.Diagnostics;
using System.Text.Json;
using PdfEditor.Core.Annotations;
using PdfEditor.Core.Files;
using PdfEditor.Core.Storage;
using PdfEditor.Pdf.Annotations;

namespace PdfEditor.Pdf.Storage;

/// <summary>
/// Keeps unsaved annotations on disk beside a manifest, so work survives a crash or a power cut.
/// </summary>
/// <remarks>
/// The original document is never touched. Each tracked document gets one sidecar holding the
/// annotations as the editor's own payload — the same payload the writer embeds — and one manifest
/// entry recording where the sidecar came from. Recovery is therefore additive: restoring re-applies
/// annotations to a freshly opened document, it does not resurrect a half-written PDF.
///
/// Lives here rather than in <c>Core</c> because it round-trips through
/// <see cref="AnnotationSerializer"/>; <c>Core</c> owns the contract, this assembly implements it.
/// </remarks>
public sealed class FileSystemAutosaveService : IAutosaveService
{
    private readonly AppPaths _paths;
    private readonly string _manifestPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>What each live session was opened against, keyed by session id. Guarded by <c>_gate</c>.</summary>
    private readonly Dictionary<string, (string Path, string Fingerprint)> _tracked = [];
    private readonly int _processId = Environment.ProcessId;
    private bool _disposed;

    public FileSystemAutosaveService(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        _manifestPath = Path.Combine(paths.Recovery, "manifest.json");
    }

    public string BeginSession(string sourcePath, string sourceFingerprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentNullException.ThrowIfNull(sourceFingerprint);

        var sessionId = $"{_processId:x}-{Guid.NewGuid():N}";
        // The manifest entry is not written until there is something to record, so an opened but
        // unedited document leaves no recovery file behind at all.
        _gate.Wait();
        try { _tracked[sessionId] = (sourcePath, sourceFingerprint); }
        finally { _gate.Release(); }
        return sessionId;
    }

    public async Task SaveAsync(
        string sessionId,
        IReadOnlyList<Annotation> annotations,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(annotations);
        if (_disposed) return;

        // Nothing unsaved means nothing to offer back; drop any earlier sidecar rather than
        // leaving a stale one that would offer the user annotations they have already undone.
        if (annotations.Count == 0)
        {
            await DiscardAsync(sessionId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            annotations.Select(AnnotationSerializer.Serialize).ToArray());

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_paths.Recovery);
            var fileName = sessionId + ".json";
            await AtomicFileWriter.WriteAsync(SidecarPath(fileName), payload, cancellationToken)
                .ConfigureAwait(false);

            var manifest = RecoveryManifest.Load(_manifestPath);
            var existing = manifest.Sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (existing is not null) manifest.Sessions.Remove(existing);

            var tracked = _tracked.TryGetValue(sessionId, out var t)
                ? t
                : (Path: existing?.SourcePath ?? string.Empty,
                   Fingerprint: existing?.SourceFingerprint ?? string.Empty);

            manifest.Sessions.Add(new RecoverySession
            {
                SessionId = sessionId,
                SourcePath = tracked.Path,
                SourceFingerprint = tracked.Fingerprint,
                LastAutosaveUtc = DateTimeOffset.UtcNow,
                AnnotationCount = annotations.Count,
                AnnotationsFileName = fileName,
                ProcessId = _processId
            });

            await manifest.SaveAsync(_manifestPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<Annotation>> RestoreAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var manifest = RecoveryManifest.Load(_manifestPath);
        var session = manifest.Sessions.FirstOrDefault(s => s.SessionId == sessionId);
        if (session is null) return [];

        try
        {
            var bytes = await File.ReadAllBytesAsync(SidecarPath(session.AnnotationsFileName), cancellationToken)
                .ConfigureAwait(false);
            var payloads = JsonSerializer.Deserialize<string[]>(bytes) ?? [];
            return payloads.Select(AnnotationSerializer.Deserialize)
                .OfType<Annotation>()
                .ToArray();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A sidecar that cannot be read is not worth failing an open over; the user is told
            // the recovery is unavailable and continues with the document as it is on disk.
            return [];
        }
    }

    public Task<IReadOnlyList<RecoverySession>> FindRecoverableSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var manifest = RecoveryManifest.Load(_manifestPath);
        IReadOnlyList<RecoverySession> stranded = manifest.Sessions
            .Where(s => s.AnnotationCount > 0 && !IsOwnedByALiveProcess(s))
            .OrderByDescending(s => s.LastAutosaveUtc)
            .ToArray();
        return Task.FromResult(stranded);
    }

    public async Task DiscardAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = RecoveryManifest.Load(_manifestPath);
            var session = manifest.Sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session is null) return;

            manifest.Sessions.Remove(session);
            _tracked.Remove(sessionId);
            TryDelete(SidecarPath(session.AnnotationsFileName));
            await manifest.SaveAsync(_manifestPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = _paths.ClearDirectory(_paths.Recovery);
            _tracked.Clear();
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        // Deliberately does not wait on the gate. The composition root disposes synchronously, so
        // any wait here happens on the UI thread; blocking it stalls the dispatcher, and an
        // in-flight autosave resuming on that same thread could not then complete. Nothing here
        // owns an unmanaged handle either — the gate is a SemaphoreSlim whose wait handle is never
        // taken — so there is nothing that must be released.
        //
        // Sessions worth keeping are already gone: the shell ends them on save and on close.
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private string SidecarPath(string fileName) =>
        Path.Combine(_paths.Recovery, Path.GetFileName(fileName));

    /// <summary>
    /// A session belonging to a process that is still running is being actively edited somewhere,
    /// so it must not be offered as a recovery.
    /// </summary>
    private bool IsOwnedByALiveProcess(RecoverySession session)
    {
        if (session.ProcessId == _processId) return true;
        if (session.ProcessId <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(session.ProcessId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
