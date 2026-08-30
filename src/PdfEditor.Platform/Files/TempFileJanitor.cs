using PdfEditor.Core.Storage;

namespace PdfEditor.Platform.Files;

/// <summary>
/// Tracks and removes the temporary files the application creates, and clears anything a crash
/// left behind on the next start.
/// </summary>
/// <remarks>
/// Print jobs are built from user documents, so leaving one on disk would leak the content of a
/// document the user may consider private. Deletion is confined to the application's own temporary
/// directory: a path outside it is refused rather than followed.
/// </remarks>
public sealed class TempFileJanitor : IDisposable
{
    private readonly string _root;
    private readonly HashSet<string> _tracked = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _disposed;

    public TempFileJanitor(AppPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).Temp) { }

    public TempFileJanitor(string temporaryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        _root = Path.GetFullPath(temporaryRoot);
    }

    public string Root => _root;

    /// <summary>Registers a file for deletion. Throws when it is not inside the temporary root.</summary>
    public void Track(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (!IsInsideRoot(full))
            throw new InvalidOperationException($"Refusing to track a path outside the temporary directory: '{path}'.");

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _tracked.Add(full);
        }
    }

    /// <summary>Deletes one tracked file immediately.</summary>
    public bool Release(string path)
    {
        var full = Path.GetFullPath(path);
        lock (_gate) _tracked.Remove(full);
        return TryDelete(full);
    }

    /// <summary>Deletes every file tracked in this session.</summary>
    public int ReleaseAll()
    {
        string[] paths;
        lock (_gate)
        {
            paths = [.. _tracked];
            _tracked.Clear();
        }
        return paths.Count(TryDelete);
    }

    /// <summary>
    /// Removes leftovers older than <paramref name="maxAge"/>. Called at startup so a crash cannot
    /// leave a document fragment on disk indefinitely.
    /// </summary>
    public int CleanupOrphans(TimeSpan maxAge)
    {
        if (!Directory.Exists(_root)) return 0;
        var cutoff = DateTime.UtcNow - maxAge;
        int removed = 0;

        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(path);
                if (info.LinkTarget is not null) { info.Delete(); removed++; continue; }
                if (info.LastWriteTimeUtc > cutoff) continue;
                info.Delete();
                removed++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }

    private bool IsInsideRoot(string fullPath)
    {
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryDelete(string path)
    {
        if (!IsInsideRoot(path)) return false;
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        ReleaseAll();
        lock (_gate) _disposed = true;
    }
}
