using PdfEditor.Core.Storage;

namespace PdfEditor.App;

/// <summary>
/// Records a fatal error locally.
/// </summary>
/// <remarks>
/// Nothing is ever uploaded and no document content, file name or recognised text is written —
/// only the exception type, message and stack, which describe the application's own code.
/// </remarks>
internal static class CrashLog
{
    public static void Write(Exception exception)
    {
        try
        {
            var paths = AppPaths.ForCurrentUser();
            Directory.CreateDirectory(paths.Logs);
            var file = Path.Combine(paths.Logs, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(file,
                $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}" +
                $"{exception.GetType().FullName}: {exception.Message}{Environment.NewLine}" +
                exception.StackTrace);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing the log is preferable to masking the original failure.
        }
    }
}
