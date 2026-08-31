using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Core.Storage;
using PdfEditor.Pdf.Storage;
using Xunit;

namespace PdfEditor.Pdf.Tests;

/// <summary>
/// Covers the sidecar contract the shell depends on: work is recorded while it is unsaved, is
/// offered back only when the run that owned it is gone, and disappears the moment it is saved
/// or discarded.
/// </summary>
public sealed class AutosaveRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "pdfeditor-autosave-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;

    public AutosaveRecoveryTests()
    {
        _paths = AppPaths.ForRoot(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static Annotation Note(string text) => new TextBoxAnnotation
    {
        PageIndex = 0,
        Rect = new PdfRect(10, 10, 100, 20),
        Text = text
    };

    private string SourceFile()
    {
        var path = Path.Combine(_root, "source.pdf");
        File.WriteAllText(path, "not a real pdf, only its identity matters here");
        return path;
    }

    [Fact]
    public async Task Annotations_survive_a_round_trip_through_the_sidecar()
    {
        var source = SourceFile();
        await using var service = new FileSystemAutosaveService(_paths);

        var session = service.BeginSession(source, SourceFingerprint.For(source));
        await service.SaveAsync(session, [Note("שלום"), Note("second")]);

        var restored = await service.RestoreAsync(session);

        Assert.Equal(2, restored.Count);
        Assert.Equal("שלום", Assert.IsType<TextBoxAnnotation>(restored[0]).Text);
        Assert.Equal("second", Assert.IsType<TextBoxAnnotation>(restored[1]).Text);
    }

    [Fact]
    public async Task A_session_owned_by_this_process_is_not_offered_back()
    {
        var source = SourceFile();
        await using var service = new FileSystemAutosaveService(_paths);

        var session = service.BeginSession(source, SourceFingerprint.For(source));
        await service.SaveAsync(session, [Note("still editing")]);

        Assert.Empty(await service.FindRecoverableSessionsAsync());
    }

    [Fact]
    public async Task A_session_left_by_a_dead_process_is_offered_back()
    {
        var source = SourceFile();
        await using (var crashed = new FileSystemAutosaveService(_paths))
        {
            var session = crashed.BeginSession(source, SourceFingerprint.For(source));
            await crashed.SaveAsync(session, [Note("unsaved work")]);
        }

        // Rewrite the manifest as though the owning process is long gone.
        var manifestPath = Path.Combine(_paths.Recovery, "manifest.json");
        var manifest = RecoveryManifest.Load(manifestPath);
        var only = manifest.Sessions.Single();
        manifest.Sessions[0] = only with { ProcessId = -1 };
        await manifest.SaveAsync(manifestPath);

        await using var service = new FileSystemAutosaveService(_paths);
        var offered = await service.FindRecoverableSessionsAsync();

        var recovered = Assert.Single(offered);
        Assert.Equal(source, recovered.SourcePath);
        Assert.Equal(1, recovered.AnnotationCount);
        Assert.Single(await service.RestoreAsync(recovered.SessionId));
    }

    [Fact]
    public async Task Discarding_removes_both_the_entry_and_the_sidecar()
    {
        var source = SourceFile();
        await using var service = new FileSystemAutosaveService(_paths);

        var session = service.BeginSession(source, SourceFingerprint.For(source));
        await service.SaveAsync(session, [Note("temporary")]);
        Assert.NotEmpty(Directory.GetFiles(_paths.Recovery, "*.json"));

        await service.DiscardAsync(session);

        var manifest = RecoveryManifest.Load(Path.Combine(_paths.Recovery, "manifest.json"));
        Assert.Empty(manifest.Sessions);
        Assert.Empty(await service.RestoreAsync(session));
        Assert.False(File.Exists(Path.Combine(_paths.Recovery, session + ".json")));
    }

    [Fact]
    public async Task Autosaving_an_empty_set_clears_an_earlier_sidecar()
    {
        var source = SourceFile();
        await using var service = new FileSystemAutosaveService(_paths);

        var session = service.BeginSession(source, SourceFingerprint.For(source));
        await service.SaveAsync(session, [Note("added then undone")]);
        await service.SaveAsync(session, []);

        var manifest = RecoveryManifest.Load(Path.Combine(_paths.Recovery, "manifest.json"));
        Assert.Empty(manifest.Sessions);
    }

    [Fact]
    public async Task A_replaced_source_is_reported_as_stale()
    {
        var source = SourceFile();
        await using var service = new FileSystemAutosaveService(_paths);

        var session = service.BeginSession(source, SourceFingerprint.For(source));
        await service.SaveAsync(session, [Note("against the old bytes")]);

        var manifestPath = Path.Combine(_paths.Recovery, "manifest.json");
        var stored = RecoveryManifest.Load(manifestPath).Sessions.Single();
        Assert.False(stored.IsStale(SourceFingerprint.For(source)));

        await Task.Delay(10);
        File.WriteAllText(source, "the file has been replaced since the autosave was taken");

        Assert.True(stored.IsStale(SourceFingerprint.For(source)));
    }

    [Fact]
    public async Task Clearing_removes_every_recovery_file()
    {
        var source = SourceFile();
        await using var service = new FileSystemAutosaveService(_paths);

        var first = service.BeginSession(source, SourceFingerprint.For(source));
        var second = service.BeginSession(source, SourceFingerprint.For(source));
        await service.SaveAsync(first, [Note("one")]);
        await service.SaveAsync(second, [Note("two")]);

        Assert.True(await service.ClearAllAsync() > 0);
        Assert.Empty(Directory.GetFiles(_paths.Recovery, "*.json"));
        Assert.Empty(await service.FindRecoverableSessionsAsync());
    }

    [Fact]
    public void A_fingerprint_survives_a_file_that_cannot_be_read()
    {
        Assert.Equal("missing", SourceFingerprint.For(Path.Combine(_root, "nothing-here.pdf")));
    }
}
