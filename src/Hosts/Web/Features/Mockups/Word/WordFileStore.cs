using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace OppOcr.Web.Features.Mockups.Word;

public enum WordVersionSource
{
    /// <summary>The DOCX the OCR service produced.</summary>
    Ocr,

    /// <summary>Built by this app from the text (samples, or when the OCR service could not be reached).</summary>
    Generated,

    /// <summary>Saved from the browser editor.</summary>
    Edited,
}

public sealed record WordVersion(int Number, DateTime SavedAtUtc, string Author, WordVersionSource Source, long SizeBytes, string Sha256);

/// <summary>Word files of a document, one per saved version (port; disk adapter below).</summary>
public interface IWordFileStore
{
    Task<IReadOnlyList<WordVersion>> ListAsync(string documentId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a version, unless it is byte-for-byte the latest one (the editor can report the same
    /// content twice: on Save and again when it closes). Returns the version and whether it is new.
    /// </summary>
    Task<(WordVersion Version, bool Created)> SaveAsync(string documentId, Stream content, string author, WordVersionSource source, CancellationToken cancellationToken);

    Task<Stream?> OpenAsync(string documentId, int number, CancellationToken cancellationToken);

    /// <summary>How many page images of a version are kept, or <c>null</c> when it has not been rendered.</summary>
    Task<int?> PreviewPageCountAsync(string documentId, int number, CancellationToken cancellationToken);

    /// <summary>Keeps the page images (PNG) of a version. A version's pages are saved once; later calls keep the first.</summary>
    Task SavePreviewAsync(string documentId, int number, IReadOnlyList<byte[]> pages, CancellationToken cancellationToken);

    Task<Stream?> OpenPreviewPageAsync(string documentId, int number, int page, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps each document's versions as <c>{root}/{id}/{n}.docx</c> plus a <c>versions.json</c> manifest, and
/// their page images as <c>{root}/{id}/preview/{n}/{page}.png</c>. Files and preview folders are written
/// under a temporary name and moved into place, so a crash never leaves half a version or preview listed.
/// </summary>
public sealed partial class DiskWordFileStore : IWordFileStore, IDisposable
{
    private const string ManifestName = "versions.json";
    private const string PreviewFolderName = "preview";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time;
    private readonly string _root;

    public DiskWordFileStore(IOptions<WordStorageOptions> options, IHostEnvironment environment, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        _time = time;
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.Path));
    }

    public void Dispose() => _gate.Dispose();

    public async Task<IReadOnlyList<WordVersion>> ListAsync(string documentId, CancellationToken cancellationToken)
    {
        var folder = FolderFor(documentId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadManifestAsync(folder, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(WordVersion Version, bool Created)> SaveAsync(string documentId, Stream content, string author, WordVersionSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var folder = FolderFor(documentId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(folder);
            var temp = Path.Combine(folder, $".{Guid.NewGuid():N}.tmp");
            string hash;
            long size;
            await using (var file = File.Create(temp))
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                hash = Convert.ToHexStringLower(sha.GetHashAndReset());
                size = file.Length;
            }

            var versions = await ReadManifestAsync(folder, cancellationToken);
            if (versions.Count > 0 && versions[^1].Sha256 == hash)
            {
                File.Delete(temp);
                return (versions[^1], false);
            }

            var version = new WordVersion(versions.Count + 1, _time.GetUtcNow().UtcDateTime, author, source, size, hash);
            File.Move(temp, FileFor(folder, version.Number), overwrite: true);
            await WriteManifestAsync(folder, [.. versions, version], cancellationToken);
            return (version, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<Stream?> OpenAsync(string documentId, int number, CancellationToken cancellationToken)
    {
        var path = FileFor(FolderFor(documentId), number);
        Stream? stream = number >= 1 && File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task<int?> PreviewPageCountAsync(string documentId, int number, CancellationToken cancellationToken)
    {
        var folder = PreviewFolder(documentId, number);
        int? count = number >= 1 && Directory.Exists(folder) ? Directory.GetFiles(folder, "*.png").Length : null;
        return Task.FromResult(count);
    }

    public async Task SavePreviewAsync(string documentId, int number, IReadOnlyList<byte[]> pages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        var target = PreviewFolder(documentId, number);
        if (Directory.Exists(target))
            return;

        var parent = Path.GetDirectoryName(target)!;
        var temp = Path.Combine(parent, $".{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(temp);
        try
        {
            for (var i = 0; i < pages.Count; i++)
                await File.WriteAllBytesAsync(Path.Combine(temp, PageFile(i + 1)), pages[i], cancellationToken);
            Directory.Move(temp, target);
        }
        catch (IOException) when (Directory.Exists(target))
        {
            // Another request rendered the same version first; its pages are identical.
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    public Task<Stream?> OpenPreviewPageAsync(string documentId, int number, int page, CancellationToken cancellationToken)
    {
        var path = Path.Combine(PreviewFolder(documentId, number), PageFile(page));
        Stream? stream = number >= 1 && page >= 1 && File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    private string PreviewFolder(string documentId, int number) =>
        Path.Combine(FolderFor(documentId), PreviewFolderName, number.ToString(CultureInfo.InvariantCulture));

    private static string PageFile(int page) => string.Create(CultureInfo.InvariantCulture, $"{page}.png");

    /// <summary>Ids come from URLs: only plain characters may become a folder name.</summary>
    private string FolderFor(string documentId)
    {
        if (!SafeId().IsMatch(documentId))
            throw new ArgumentException("Unsupported document id.", nameof(documentId));
        return Path.Combine(_root, documentId);
    }

    private static string FileFor(string folder, int number) => Path.Combine(folder, $"{number}.docx");

    private static async Task<IReadOnlyList<WordVersion>> ReadManifestAsync(string folder, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, ManifestName);
        if (!File.Exists(path))
            return [];
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<WordVersion>>(stream, Json, cancellationToken) ?? [];
    }

    private static async Task WriteManifestAsync(string folder, IReadOnlyList<WordVersion> versions, CancellationToken cancellationToken)
    {
        var temp = Path.Combine(folder, $".{ManifestName}.{Guid.NewGuid():N}.tmp");
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, versions, Json, cancellationToken);
        File.Move(temp, Path.Combine(folder, ManifestName), overwrite: true);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SafeId();
}
