using System.Text.Json;
using System.Text.Json.Serialization;
using PdfEditor.Core.Files;

namespace PdfEditor.Core.Settings;

public enum ThemePreference
{
    /// <summary>Follow the Windows app theme.</summary>
    System,
    Light,
    Dark
}

/// <summary>
/// User preferences. Everything here is local; nothing is ever transmitted.
/// </summary>
public sealed class AppSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>Honour the Windows "show animations" accessibility setting.</summary>
    public bool ReducedMotion { get; set; }

    public bool RememberRecentFiles { get; set; } = true;
    public int MaxRecentFiles { get; set; } = 12;

    public bool AutosaveEnabled { get; set; } = true;
    public int AutosaveIntervalSeconds { get; set; } = 45;

    public bool OcrCacheEnabled { get; set; } = true;
    public int OcrCacheMaxAgeDays { get; set; } = 30;
    public int OcrRenderDpi { get; set; } = 300;

    /// <summary>Upper bound for the rendered-page cache, in megabytes.</summary>
    public int PageCacheMemoryBudgetMb { get; set; } = 384;

    public bool SeparateSheetsPerContentPageDefault { get; set; }

    /// <summary>Remembers signatures between sessions ("זכור את החתימה במחשב זה").</summary>
    public bool RememberSignatures { get; set; } = true;

    public double DefaultZoom { get; set; } = 1.0;

    public bool ShowThumbnails { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    // ---- persistence -----------------------------------------------------------------------
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, Json)?.Validated() ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged settings file must never stop the application from starting.
            return new AppSettings();
        }
    }

    public Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Validated(), Json);
        return AtomicFileWriter.WriteAsync(path, bytes, cancellationToken);
    }

    /// <summary>Clamps values that would otherwise destabilise the application.</summary>
    public AppSettings Validated()
    {
        MaxRecentFiles = Math.Clamp(MaxRecentFiles, 0, 50);
        AutosaveIntervalSeconds = Math.Clamp(AutosaveIntervalSeconds, 10, 600);
        OcrCacheMaxAgeDays = Math.Clamp(OcrCacheMaxAgeDays, 1, 365);
        OcrRenderDpi = Math.Clamp(OcrRenderDpi, 150, 600);
        PageCacheMemoryBudgetMb = Math.Clamp(PageCacheMemoryBudgetMb, 64, 2048);
        DefaultZoom = Math.Clamp(DefaultZoom, 0.1, 8.0);
        return this;
    }
}
