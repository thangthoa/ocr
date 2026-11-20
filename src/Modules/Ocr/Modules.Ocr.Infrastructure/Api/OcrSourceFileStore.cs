using System.Collections.Concurrent;
using System.Security.Cryptography;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Infrastructure.Api;

/// <summary>
/// Keeps uploaded files in memory while the OCR service downloads them (ADR 0006). A link is an
/// unguessable token; it stops working when the submit request ends, and at the latest when it expires.
/// </summary>
internal sealed class OcrSourceFileStore(TimeProvider time) : IOcrSourceFiles
{
    private const string FallbackContentType = "application/octet-stream";

    private readonly ConcurrentDictionary<string, Entry> _files = new(StringComparer.Ordinal);

    internal int Count => _files.Count;

    /// <param name="content">Not copied: the caller must not change it afterwards.</param>
    /// <returns>The link's token; disposing the lease removes the file.</returns>
    public Lease Add(byte[] content, string fileName, TimeSpan lifetime)
    {
        // Leases are always disposed; this only guards against a file outliving its link if one is not.
        RemoveExpired();

        var token = RandomNumberGenerator.GetHexString(64, lowercase: true);
        var contentType = OcrFileFormats.FromFileName(fileName)?.ContentType() ?? FallbackContentType;
        _files[token] = new Entry(content, fileName, contentType, time.GetUtcNow() + lifetime);
        return new Lease(this, token);
    }

    public FileContent? Open(string token)
    {
        if (!_files.TryGetValue(token, out var entry))
            return null;
        if (entry.ExpiresAt <= time.GetUtcNow())
        {
            _files.TryRemove(token, out _);
            return null;
        }

        return new FileContent(new MemoryStream(entry.Content, writable: false), entry.ContentType, entry.FileName);
    }

    private void RemoveExpired()
    {
        var now = time.GetUtcNow();
        foreach (var (token, entry) in _files)
        {
            if (entry.ExpiresAt <= now)
                _files.TryRemove(token, out _);
        }
    }

    internal sealed class Lease(OcrSourceFileStore owner, string token) : IDisposable
    {
        public string Token => token;

        public void Dispose() => owner._files.TryRemove(token, out _);
    }

    private sealed record Entry(byte[] Content, string FileName, string ContentType, DateTimeOffset ExpiresAt);
}
