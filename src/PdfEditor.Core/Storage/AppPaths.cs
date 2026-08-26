namespace PdfEditor.Core.Storage;

/// <summary>
/// Every location the application is allowed to write to. All of them live under the current
/// user's local application data so the app never needs administrator rights and nothing is
/// written to a roaming or synchronised profile.
/// </summary>
public sealed class AppPaths
{
    public const string FolderName = "PdfEditor";

    private AppPaths(string root)
    {
        Root = root;
        Settings = Path.Combine(root, "settings.json");
        RecentFiles = Path.Combine(root, "recent.json");
        Signatures = Path.Combine(root, "signatures");
        OcrCache = Path.Combine(root, "ocr-cache");
        Recovery = Path.Combine(root, "recovery");
        Temp = Path.Combine(root, "temp");
        Logs = Path.Combine(root, "logs");
    }

    /// <summary>
    /// %LOCALAPPDATA%\PdfEditor on Windows. LocalApplicationData is deliberately used instead of
    /// ApplicationData so signatures and caches are never synchronised to a roaming profile.
    /// </summary>
    public static AppPaths ForCurrentUser()
    {
        var baseDir = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(Path.GetTempPath(), FolderName + "-fallback");
        return new AppPaths(Path.Combine(baseDir, FolderName));
    }

    /// <summary>Creates an instance rooted at an arbitrary directory. Used by tests.</summary>
    public static AppPaths ForRoot(string root) => new(Path.GetFullPath(root));

    public string Root { get; }
    public string Settings { get; }
    public string RecentFiles { get; }
    public string Signatures { get; }
    public string OcrCache { get; }
    public string Recovery { get; }
    public string Temp { get; }
    public string Logs { get; }

    public IEnumerable<string> AllDirectories =>
        [Root, Signatures, OcrCache, Recovery, Temp, Logs];

    public void EnsureCreated()
    {
        foreach (var dir in AllDirectories) Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Deletes everything under <paramref name="directory"/> without following symbolic links and
    /// without escaping the application data root.
    /// </summary>
    public int ClearDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);
        var rootWithSep = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.Ordinal) &&
            !string.Equals(full, Root, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to clear a directory outside the application data root.");
        if (!Directory.Exists(full)) return 0;

        int removed = 0;
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LinkTarget is not null) { info.Delete(); removed++; continue; }
                File.Delete(file);
                removed++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var dir in Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try { Directory.Delete(dir, recursive: false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return removed;
    }
}
