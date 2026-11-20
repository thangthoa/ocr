using Microsoft.AspNetCore.Components.Forms;

namespace OppOcr.Web.Features.Ocr;

/// <summary>
/// A browser file that is opened only when it is first read. With Interactive Server the browser starts
/// sending a file as soon as its stream is opened, and every file shares the circuit's one connection:
/// files opened together but read one after another stall each other until the read times out
/// ("Did not receive any data in the allotted time"). Pages are read in order, so with this wrapper only
/// the file being read is being sent.
/// </summary>
internal sealed class DeferredBrowserFileStream(IBrowserFile file, CancellationToken cancellationToken) : Stream
{
    private Stream? _inner;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    // The size is re-checked against the bytes read by the use case; a smaller limit here would turn
    // a lying client into an unhandled IOException instead of the "file too large" message.
    private Stream Inner => _inner ??= file.OpenReadStream(maxAllowedSize: Math.Max(file.Size, 1), cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Inner.ReadAsync(buffer, cancellationToken);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask DisposeAsync()
    {
        if (_inner is not null)
            await _inner.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner?.Dispose();
        base.Dispose(disposing);
    }
}
