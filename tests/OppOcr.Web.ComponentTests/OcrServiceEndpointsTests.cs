using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.GetOcrSourceFile;
using Modules.Ocr.Application.Features.ReceiveOcrJobCallback;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.ComponentTests;

/// <summary>The endpoints the OCR service calls (ADR 0006), driven without a server.</summary>
public sealed class OcrServiceEndpointsTests
{
    private const string Timestamp = "1763371312";
    private const string Signature = "sha256=0123abcd";

    private readonly InMemoryOcr _ocr = new();
    private readonly StubCallbackReader _reader = new();
    private readonly RecordingNotifier _notifier = new();

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task Accepted_callback_is_answered_before_anyone_is_told()
    {
        _ocr.Seed(InMemoryOcr.NewJob("job_a1", "a.png", OcrJobStatus.Running));
        _reader.Next = InMemoryOcr.NewJob("job_a1", "a.png", OcrJobStatus.Done);
        var (http, response) = Callback("""{"job_id":"job_a1"}""");

        var result = await ReceiveAsync(http);

        Assert.Equal(StatusCodes.Status200OK, StatusCode(result));
        Assert.Empty(_notifier.Changes);
        await response.CompleteAsync();
        var change = Assert.Single(_notifier.Changes);
        Assert.Equal(("job_a1", OcrJobStatus.Done), (change.JobId.Value, change.Status));
    }

    [Fact]
    public async Task Raw_body_and_signature_headers_reach_the_check_unchanged()
    {
        _reader.Next = InMemoryOcr.NewJob("job_a1", "a.png", OcrJobStatus.Done);
        const string body = """{ "job_id" : "job_a1", "text": "لما كان" }""";

        await ReceiveAsync(Callback(body).Http);

        Assert.Equal(Encoding.UTF8.GetBytes(body), _reader.Last!.Body.ToArray());
        Assert.Equal((Timestamp, Signature), (_reader.Last.Timestamp, _reader.Last.Signature));
    }

    [Fact]
    public async Task Repeated_callback_is_acknowledged_without_telling_anyone()
    {
        _ocr.Seed(InMemoryOcr.NewJob("job_a1", "a.png", OcrJobStatus.Done));
        _reader.Next = InMemoryOcr.NewJob("job_a1", "a.png", OcrJobStatus.Done);
        var (http, response) = Callback("{}");

        var result = await ReceiveAsync(http);
        await response.CompleteAsync();

        Assert.Equal(StatusCodes.Status200OK, StatusCode(result));
        Assert.Empty(_notifier.Changes);
    }

    [Fact]
    public async Task Unsigned_callback_is_unauthorized()
    {
        _reader.Next = Error.Unauthorized(OcrServiceErrorCodes.CallbackRejected, "bad signature");

        var result = await ReceiveAsync(Callback("{}").Http);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusCode(result));
        Assert.Empty(_ocr.Registry);
    }

    [Fact]
    public async Task Signed_body_that_is_not_a_job_is_a_bad_request()
    {
        _reader.Next = Error.Validation(OcrServiceErrorCodes.CallbackInvalid, "not a job");

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(await ReceiveAsync(Callback("[]").Http)));
    }

    [Fact]
    public async Task Oversized_body_is_refused_before_it_is_checked()
    {
        var (declared, _) = Callback("{}");
        declared.Request.ContentLength = OcrServiceEndpoints.MaxCallbackBytes + 1;
        var (streamed, _) = Callback(new string('x', OcrServiceEndpoints.MaxCallbackBytes + 1));
        streamed.Request.ContentLength = null;

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, StatusCode(await ReceiveAsync(declared)));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, StatusCode(await ReceiveAsync(streamed)));
        Assert.Null(_reader.Last);
    }

    [Fact]
    public async Task Repeated_signature_header_counts_as_missing()
    {
        _reader.Next = Error.Unauthorized(OcrServiceErrorCodes.CallbackRejected, "no signature");
        var (http, _) = Callback("{}");
        http.Request.Headers[OcrServiceEndpoints.SignatureHeader] = new[] { Signature, Signature };

        await ReceiveAsync(http);

        Assert.Null(_reader.Last!.Signature);
    }

    [Fact]
    public void Source_file_is_served_with_its_type_and_never_cached()
    {
        var files = new StubSourceFiles();
        files.Files["t1"] = new FileContent(new MemoryStream([1, 2]), "application/pdf", "hợp đồng.pdf");
        var http = new DefaultHttpContext();

        var result = Assert.IsType<FileStreamHttpResult>(OcrServiceEndpoints.GetSourceFile("t1", new GetOcrSourceFileHandler(files), http));

        Assert.Equal(("application/pdf", "hợp đồng.pdf"), (result.ContentType, result.FileDownloadName));
        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions);
        Assert.Equal("no-store", http.Response.Headers.CacheControl);
    }

    [Fact]
    public void Unknown_or_released_source_file_is_not_found()
    {
        var result = OcrServiceEndpoints.GetSourceFile("gone", new GetOcrSourceFileHandler(new StubSourceFiles()), new DefaultHttpContext());

        Assert.Equal(StatusCodes.Status404NotFound, StatusCode(result));
    }

    [Theory]
    [InlineData("/api/ocr-files/84a2d68700aa98cb/a.png", "/api/ocr-files/***")]
    [InlineData("/API/OCR-FILES/84a2d68700aa98cb", "/api/ocr-files/***")]
    [InlineData("/api/ocr-callback", "/api/ocr-callback")]
    [InlineData("/documents/DOC-1", "/documents/DOC-1")]
    public void File_link_tokens_are_hidden_from_request_logs(string path, string logged) =>
        Assert.Equal(logged, OcrServiceEndpoints.PathForLogs(path));

    private static int? StatusCode(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static (DefaultHttpContext Http, RecordingResponseFeature Response) Callback(string body)
    {
        var http = new DefaultHttpContext();
        var response = new RecordingResponseFeature();
        http.Features.Set<IHttpResponseFeature>(response);
        var bytes = Encoding.UTF8.GetBytes(body);
        http.Request.Method = HttpMethods.Post;
        http.Request.Body = new MemoryStream(bytes);
        http.Request.ContentLength = bytes.Length;
        http.Request.ContentType = "application/json";
        http.Request.Headers[OcrServiceEndpoints.TimestampHeader] = Timestamp;
        http.Request.Headers[OcrServiceEndpoints.SignatureHeader] = Signature;
        http.Request.Headers["X-OCR-Event"] = "job.done";
        return (http, response);
    }

    private Task<IResult> ReceiveAsync(HttpContext http) =>
        OcrServiceEndpoints.ReceiveCallbackAsync(
            http,
            new ReceiveOcrJobCallbackHandler(_reader, _ocr, NullLogger<ReceiveOcrJobCallbackHandler>.Instance),
            _notifier,
            new StubLifetime(),
            Ct);

    /// <summary>Holds what would run once the response has been sent, until <see cref="CompleteAsync"/>.</summary>
    private sealed class RecordingResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _completed = [];

        public override void OnCompleted(Func<object, Task> callback, object state) => _completed.Add((callback, state));

        public async Task CompleteAsync()
        {
            foreach (var (callback, state) in _completed)
                await callback(state);
        }
    }

    private sealed class StubCallbackReader : IOcrCallbackReader
    {
        public Result<OcrJob> Next { get; set; } = Error.Validation("unset", "no job set");

        public OcrCallback? Last { get; private set; }

        public Result<OcrJob> Read(OcrCallback callback)
        {
            Last = callback;
            return Next;
        }
    }

    private sealed class RecordingNotifier : IOcrJobNotifier
    {
        public List<OcrJobChange> Changes { get; } = [];

        public Task NotifyAsync(OcrJobChange change, CancellationToken cancellationToken)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }

    private sealed class StubSourceFiles : IOcrSourceFiles
    {
        public Dictionary<string, FileContent> Files { get; } = [];

        public FileContent? Open(string token) => Files.GetValueOrDefault(token);
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
