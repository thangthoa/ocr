using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Domain.Jobs;
using Modules.Ocr.Infrastructure.Api;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Infrastructure.Tests;

/// <summary>Exercises the HTTP adapter against canned responses shaped like the API document; no network.</summary>
public sealed class OcrApiClientTests
{
    private const string DoneJob = """
        {
          "job_id": "job_3f9c2a1b7d4e5f60", "status": "done", "file_name": "hop_dong.jpg", "doc_type": "printed",
          "pages_total": 1, "pages_done": 1, "current_step": null, "queue_position": null,
          "created_at": 1763371200.123, "started_at": 1763371201.5, "finished_at": 1763371312.9,
          "result": {
            "pages": [{ "page": 1, "type": "printed", "text": "( ٢ )\nلما كان", "seconds": 111.4, "engine": "A v2",
                        "warnings": ["the full-page read repeated itself; this page was read block by block"], "cached": false }],
            "full_text": "( ٢ )\nلما كان"
          },
          "error": null,
          "links": { "self": "/v1/jobs/job_3f9c2a1b7d4e5f60" }
        }
        """;

    private static readonly OcrJobId JobX = OcrJobId.Parse("job_x");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_reports_not_supported_while_the_service_has_no_list_endpoint()
    {
        var result = await Create(new StubHandler(HttpStatusCode.MethodNotAllowed, """{"detail":"Method Not Allowed"}""")).ListAsync(1, 20, null, Ct);

        Assert.Equal(OcrServiceErrorCodes.ListNotSupported, result.Error!.Code);
    }

    [Fact]
    public async Task List_maps_the_services_page_shape()
    {
        // Shape observed from GET /v1/jobs: jobs come without result text.
        const string body = """
            {"total":53,"limit":20,"offset":20,"jobs":[
              {"job_id":"job_fea57b698b784699","status":"done","file_name":"test-direct.png","doc_type":"printed",
               "pages_total":1,"pages_done":1,"current_step":null,"queue_position":null,"created_at":1794985190.5,
               "started_at":1794985191.0,"finished_at":1794985225.2,"error":null,"files":["test-direct.png"],"word_ready":true,
               "links":{"self":"/v1/jobs/job_fea57b698b784699"}}]}
            """;

        var result = await Create(new StubHandler(HttpStatusCode.OK, body)).ListAsync(2, 20, null, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(53, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
        var job = Assert.Single(result.Value.Items);
        Assert.Equal("test-direct.png", job.FileName);
        Assert.Empty(job.Pages);
    }

    [Fact]
    public async Task List_passes_paging_and_status_filter()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"total":0,"limit":10,"offset":10,"jobs":[]}""");

        await Create(handler).ListAsync(2, 10, OcrJobStatus.Running, Ct);

        Assert.Equal("/v1/jobs", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Equal("?limit=10&offset=10&status=running", handler.Request.RequestUri.Query);
    }

    [Fact]
    public async Task Submit_sends_multipart_file_and_doc_type_and_parses_the_job()
    {
        var handler = new StubHandler(HttpStatusCode.Accepted, """{"job_id":"job_a","status":"queued","pages_total":1,"pages_done":0,"queue_position":3}""");
        var client = Create(handler);

        var result = await client.SubmitAsync(new MemoryStream([1, 2, 3]), "hợp đồng.png", OcrDocumentType.Handwritten, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("job_a", result.Value.Id.Value);
        Assert.Equal(OcrJobStatus.Queued, result.Value.Status);
        Assert.Equal(3, result.Value.QueuePosition);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/v1/jobs", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal("secret", handler.Request.Headers.GetValues("X-API-Key").Single());
        Assert.Contains("name=doc_type", handler.Body, StringComparison.Ordinal);
        Assert.Contains("handwritten", handler.Body, StringComparison.Ordinal);
        Assert.Contains("name=file", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_leaves_the_callers_stream_open()
    {
        // Regression: the upload handler reads the buffer after submitting.
        var content = new MemoryStream([1, 2, 3]);

        await Create(new StubHandler(HttpStatusCode.Accepted, """{"job_id":"job_a","status":"queued"}""")).SubmitAsync(content, "a.png", OcrDocumentType.Printed, Ct);

        Assert.True(content.CanRead);
        Assert.Equal(3, content.Length);
    }

    [Fact]
    public async Task Get_maps_documented_job_with_pages_and_warnings()
    {
        var result = await Create(new StubHandler(HttpStatusCode.OK, DoneJob)).GetAsync(OcrJobId.Parse("job_3f9c2a1b7d4e5f60"), Ct);

        Assert.True(result.IsSuccess);
        var job = result.Value;
        Assert.Equal(OcrJobStatus.Done, job.Status);
        Assert.Equal("hop_dong.jpg", job.FileName);
        Assert.Equal(new DateTime(2025, 11, 17, 9, 20, 0, 123, DateTimeKind.Utc), job.CreatedAtUtc!.Value, TimeSpan.FromMilliseconds(1));
        var page = Assert.Single(job.Pages);
        Assert.Equal("( ٢ )\nلما كان", page.Text);
        Assert.Equal("A v2", page.Engine);
        Assert.Single(page.Warnings);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "too_many_pages", ErrorType.Validation)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, "bad_image", ErrorType.Validation)]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", ErrorType.Validation)]
    [InlineData(HttpStatusCode.NotFound, "not_found", ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, "not_cancellable", ErrorType.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError, "internal_error", ErrorType.Failure)]
    public async Task Documented_errors_keep_service_code_and_retry_classification(HttpStatusCode status, string code, ErrorType expectedType)
    {
        var body = "{\"error\":{\"code\":\"" + code + "\",\"message\":\"x\"}}";

        var result = await Create(new StubHandler(status, body)).GetAsync(JobX, Ct);

        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(expectedType, result.Error.Type);
    }

    [Fact]
    public async Task Unreachable_service_is_a_transient_failure()
    {
        var result = await Create(new StubHandler(new HttpRequestException("connection refused"))).GetAsync(JobX, Ct);

        Assert.Equal(OcrServiceErrorCodes.ServiceUnavailable, result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    [Fact]
    public async Task Gateway_error_page_without_json_is_a_transient_failure()
    {
        var result = await Create(new StubHandler(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html")).GetAsync(JobX, Ct);

        Assert.Equal(ErrorType.Failure, result.Error!.Type);
    }

    [Fact]
    public async Task Download_streams_the_file_with_its_content_type()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "النص", "text/plain; charset=utf-8");

        var result = await Create(handler).DownloadAsync(JobX, OcrResultFormat.Txt, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("/v1/jobs/job_x/download", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Equal("?format=txt", handler.Request.RequestUri.Query);
        await using var content = result.Value.Content;
        using var reader = new StreamReader(content, Encoding.UTF8);
        Assert.Equal("النص", await reader.ReadToEndAsync(Ct));
    }

    [Fact]
    public async Task Submit_with_an_app_url_sends_a_link_the_service_downloads_during_the_request()
    {
        var store = new OcrSourceFileStore(TimeProvider.System);
        var handler = new StubHandler(HttpStatusCode.Accepted, """{"job_id":"job_a","status":"queued"}""");
        byte[]? downloaded = null;
        string? contentType = null;
        handler.OnRequest = () =>
        {
            // What the service does before it answers: fetch the file from the link.
            var file = store.Open(Token(FormValue(handler.Body, "file_url")!))!;
            using var copy = new MemoryStream();
            file.Content.CopyTo(copy);
            (downloaded, contentType) = (copy.ToArray(), file.ContentType);
        };

        var result = await Create(handler, LinkOptions(), store).SubmitAsync(new MemoryStream([1, 2, 3]), "hợp đồng.png", OcrDocumentType.Printed, Ct);

        Assert.True(result.IsSuccess);
        var fileUrl = FormValue(handler.Body, "file_url")!;
        Assert.StartsWith("https://ocr.example/api/ocr-files/", fileUrl, StringComparison.Ordinal);
        Assert.EndsWith("/h%E1%BB%A3p%20%C4%91%E1%BB%93ng.png", fileUrl, StringComparison.Ordinal);
        Assert.Equal(64, Token(fileUrl).Length);
        Assert.DoesNotContain("filename=", handler.Body, StringComparison.Ordinal);
        Assert.Equal("printed", FormValue(handler.Body, "doc_type"));
        Assert.Equal([1, 2, 3], downloaded);
        Assert.Equal("image/png", contentType);
    }

    [Fact]
    public async Task Submit_link_stops_working_once_the_service_has_answered()
    {
        var store = new OcrSourceFileStore(TimeProvider.System);
        var handler = new StubHandler(HttpStatusCode.UnprocessableEntity, """{"error":{"code":"file_download_failed","message":"HTTP 404"}}""");

        var result = await Create(handler, LinkOptions(), store).SubmitAsync(new MemoryStream([1, 2, 3]), "a.png", OcrDocumentType.Printed, Ct);

        Assert.Equal(OcrServiceErrorCodes.FileDownloadFailed, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Null(store.Open(Token(FormValue(handler.Body, "file_url")!)));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task Submit_link_is_removed_when_the_service_cannot_be_reached()
    {
        var store = new OcrSourceFileStore(TimeProvider.System);

        var result = await Create(new StubHandler(new HttpRequestException("connection refused")), LinkOptions(), store)
            .SubmitAsync(new MemoryStream([1, 2, 3]), "a.png", OcrDocumentType.Printed, Ct);

        Assert.Equal(ErrorType.Failure, result.Error!.Type);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task Submit_asks_for_a_callback_only_when_a_secret_is_configured()
    {
        var withSecret = new StubHandler(HttpStatusCode.Accepted, """{"job_id":"job_a","status":"queued"}""");
        var withoutSecret = new StubHandler(HttpStatusCode.Accepted, """{"job_id":"job_b","status":"queued"}""");

        await Create(withSecret, LinkOptions(callbackSecret: "s3cret")).SubmitAsync(new MemoryStream([1]), "a.png", OcrDocumentType.Printed, Ct);
        await Create(withoutSecret, LinkOptions()).SubmitAsync(new MemoryStream([1]), "a.png", OcrDocumentType.Printed, Ct);

        Assert.Equal("https://ocr.example/api/ocr-callback", FormValue(withSecret.Body, "callback_url"));
        Assert.Null(FormValue(withoutSecret.Body, "callback_url"));
        Assert.DoesNotContain("s3cret", withSecret.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_without_an_app_url_uploads_the_file_and_asks_for_no_callback()
    {
        var handler = new StubHandler(HttpStatusCode.Accepted, """{"job_id":"job_a","status":"queued"}""");

        await Create(handler, new OcrApiOptions { CallbackSecret = "s3cret" }).SubmitAsync(new MemoryStream([1]), "a.png", OcrDocumentType.Printed, Ct);

        Assert.Contains("filename=a.png", handler.Body, StringComparison.Ordinal);
        Assert.Null(FormValue(handler.Body, "file_url"));
        Assert.Null(FormValue(handler.Body, "callback_url"));
    }

    private static OcrApiOptions LinkOptions(string? callbackSecret = null) =>
        new() { AppUrl = new Uri("https://ocr.example/"), CallbackSecret = callbackSecret };

    /// <summary>The value of a text field in a multipart body, or null when there is no such field.</summary>
    private static string? FormValue(string body, string name)
    {
        var match = Regex.Match(body, $"Content-Disposition: form-data; name=\"?{name}\"?\r\n(?:[^\r\n]+\r\n)*\r\n(?<value>[^\r\n]*)");
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string Token(string fileUrl) => new Uri(fileUrl).Segments[^2].TrimEnd('/');

    private static OcrApiClient Create(StubHandler handler, OcrApiOptions? options = null, OcrSourceFileStore? store = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://ocr.test/") };
        http.DefaultRequestHeaders.Add(OcrApiClient.ApiKeyHeader, "secret");
        return new OcrApiClient(
            http,
            store ?? new OcrSourceFileStore(TimeProvider.System),
            Options.Create(options ?? new OcrApiOptions()),
            NullLogger<OcrApiClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body = string.Empty;
        private readonly string _mediaType = "application/json";
        private readonly Exception? _exception;

        public StubHandler(HttpStatusCode status, string body, string mediaType = "application/json")
        {
            _status = status;
            _body = body;
            _mediaType = mediaType;
        }

        public StubHandler(Exception exception) => _exception = exception;

        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        /// <summary>Runs while the request is "on the service", once its body has been read.</summary>
        public Action? OnRequest { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            OnRequest?.Invoke();
            if (_exception is not null)
                throw _exception;

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, _mediaType.Split(';')[0]),
            };
        }
    }
}
