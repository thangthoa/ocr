using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
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

    private static OcrApiClient Create(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://ocr.test/") };
        http.DefaultRequestHeaders.Add(OcrApiClient.ApiKeyHeader, "secret");
        return new OcrApiClient(http, NullLogger<OcrApiClient>.Instance);
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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            if (_exception is not null)
                throw _exception;

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, _mediaType.Split(';')[0]),
            };
        }
    }
}
