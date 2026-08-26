namespace PdfEditor.Core.Files;

/// <summary>
/// Writes a file so that an interrupted save can never leave a truncated or corrupted document
/// behind: content goes to a sibling temporary file first, is flushed to disk, and only then
/// replaces the target.
/// </summary>
public static class AtomicFileWriter
{
    private const string TempSuffix = ".pdfeditor-tmp";
    private const string BackupSuffix = ".pdfeditor-bak";

    /// <summary>
    /// Runs <paramref name="write"/> against a temporary file and atomically moves it onto
    /// <paramref name="targetPath"/>. The original file is left untouched if anything fails.
    /// </summary>
    public static async Task WriteAsync(
        string targetPath,
        Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(write);

        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new IOException($"Cannot determine directory for '{targetPath}'.");
        Directory.CreateDirectory(directory);

        var temp = fullTarget + "." + Guid.NewGuid().ToString("N")[..8] + TempSuffix;
        var backup = fullTarget + BackupSuffix;

        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, bufferSize: 81920, FileOptions.SequentialScan))
            {
                await write(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(fullTarget))
            {
                // File.Replace keeps the destination's identity and is atomic on NTFS.
                try
                {
                    File.Replace(temp, fullTarget, backup, ignoreMetadataErrors: true);
                    TryDelete(backup);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temp, fullTarget, overwrite: true);
                }
                catch (IOException)
                {
                    // Replace fails across volumes or on some network shares; fall back.
                    File.Move(temp, fullTarget, overwrite: true);
                }
            }
            else
            {
                File.Move(temp, fullTarget);
            }
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
        finally
        {
            TryDelete(backup);
        }
    }

    public static Task WriteAsync(string targetPath, byte[] content, CancellationToken cancellationToken = default) =>
        WriteAsync(targetPath, async (s, ct) => await s.WriteAsync(content, ct).ConfigureAwait(false), cancellationToken);

    /// <summary>Removes temporary files left behind by an interrupted save in a directory.</summary>
    public static int CleanupOrphans(string directory)
    {
        if (!Directory.Exists(directory)) return 0;
        int removed = 0;
        foreach (var pattern in new[] { "*" + TempSuffix, "*" + BackupSuffix })
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                if (TryDelete(file)) removed++;
        }
        return removed;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) { File.Delete(path); return true; }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }
}
