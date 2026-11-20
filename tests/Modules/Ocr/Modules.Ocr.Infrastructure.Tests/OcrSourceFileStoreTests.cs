using Microsoft.Extensions.Time.Testing;
using Modules.Ocr.Infrastructure.Api;

namespace Modules.Ocr.Infrastructure.Tests;

public sealed class OcrSourceFileStoreTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Link_serves_the_file_with_the_type_of_its_name()
    {
        var store = new OcrSourceFileStore(_time);
        using var lease = store.Add([1, 2, 3], "scan.pdf", Lifetime);

        var file = store.Open(lease.Token)!;

        using var copy = new MemoryStream();
        file.Content.CopyTo(copy);
        Assert.Equal([1, 2, 3], copy.ToArray());
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("scan.pdf", file.FileName);
    }

    [Fact]
    public void Link_stops_working_when_released()
    {
        var store = new OcrSourceFileStore(_time);
        var lease = store.Add([1], "a.png", Lifetime);

        lease.Dispose();

        Assert.Null(store.Open(lease.Token));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Link_expires_even_if_never_released()
    {
        var store = new OcrSourceFileStore(_time);
        var lease = store.Add([1], "a.png", Lifetime);

        _time.Advance(Lifetime - TimeSpan.FromSeconds(1));
        Assert.NotNull(store.Open(lease.Token));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(store.Open(lease.Token));
    }

    [Fact]
    public void Expired_files_are_dropped_when_another_is_added()
    {
        var store = new OcrSourceFileStore(_time);
        store.Add([1], "forgotten.png", Lifetime);
        _time.Advance(Lifetime);

        using var lease = store.Add([2], "b.png", Lifetime);

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Tokens_are_long_random_and_unknown_ones_find_nothing()
    {
        var store = new OcrSourceFileStore(_time);
        using var first = store.Add([1], "a.png", Lifetime);
        using var second = store.Add([1], "a.png", Lifetime);

        Assert.NotEqual(first.Token, second.Token);
        Assert.Matches("^[0-9a-f]{64}$", first.Token);
        Assert.Null(store.Open("0000"));
        Assert.Null(store.Open(first.Token.ToUpperInvariant()));
    }
}
